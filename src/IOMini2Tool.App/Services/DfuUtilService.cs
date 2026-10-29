using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IOMini2Tool.Models;

namespace IOMini2Tool.Services;

public sealed record DfuListDevice(
    string VidPid,
    string? UsbSerial,
    string? UsbPath,
    string RawLine,
    int? AlternateSetting = null);

public sealed record DfuCommandResult(
    string ExecutablePath,
    string Arguments,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool WasCancelled)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && !WasCancelled;
}

public sealed class DfuUtilService
{
    private static readonly Regex PercentRegex = new("(\\d{1,3})%", RegexOptions.Compiled);
    private static readonly Regex DeviceLineRegex = new(
        @"Found\s+DFU:\s+\[([0-9A-F]{4}):([0-9A-F]{4})\](.*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SerialRegex = new(@"\bserial=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PathRegex = new(@"\bpath=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AlternateSettingRegex = new(@"\balt=(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly string _dfuUtilPath;

    public DfuUtilService(string toolsRootPath)
    {
        _dfuUtilPath = Path.Combine(toolsRootPath, "dfu-util", "dfu-util.exe");
    }

    public string ExecutablePath => _dfuUtilPath;

    public async Task<DfuCommandResult> ListDevicesAsync(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(["-l"], TimeSpan.FromSeconds(20), null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_dfuUtilPath))
        {
            return false;
        }

        var result = await ExecuteAsync(["--version"], TimeSpan.FromSeconds(10), null, cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<DfuCommandResult> FlashAsync(
        DfuListDevice listedDevice,
        string firmwarePath,
        IProgress<FlashProgress>? progress,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-d", listedDevice.VidPid };

        if (!string.IsNullOrWhiteSpace(listedDevice.UsbSerial))
        {
            arguments.Add("-S");
            arguments.Add(listedDevice.UsbSerial);
        }
        else if (!string.IsNullOrWhiteSpace(listedDevice.UsbPath))
        {
            arguments.Add("-p");
            arguments.Add(listedDevice.UsbPath);
        }
        else
        {
            throw new InvalidOperationException("The selected DFU device has neither a USB serial number nor a USB path, so it cannot be targeted safely.");
        }

        arguments.AddRange(["-a", "0", "-D", firmwarePath, "-R"]);
        return await ExecuteAsync(arguments, TimeSpan.FromMinutes(5), progress, cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<DfuListDevice> ParseListOutput(string output)
    {
        var devices = new List<DfuListDevice>();

        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = DeviceLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var details = match.Groups[3].Value;
            var serialMatch = SerialRegex.Match(details);
            var pathMatch = PathRegex.Match(details);
            var alternateMatch = AlternateSettingRegex.Match(details);
            devices.Add(new DfuListDevice(
                $"{match.Groups[1].Value}:{match.Groups[2].Value}".ToLowerInvariant(),
                serialMatch.Success && serialMatch.Groups[1].Value.Length > 0 ? serialMatch.Groups[1].Value : null,
                pathMatch.Success && pathMatch.Groups[1].Value.Length > 0 ? pathMatch.Groups[1].Value : null,
                line,
                alternateMatch.Success && int.TryParse(alternateMatch.Groups[1].Value, out var alternateSetting)
                    ? alternateSetting
                    : null));
        }

        return devices;
    }

    public static bool TryMatchSelectedDevice(
        DeviceInfo selectedDevice,
        IReadOnlyList<DfuListDevice> listedDevices,
        out DfuListDevice? match,
        out string error)
    {
        match = null;
        error = string.Empty;

        if (!Regex.IsMatch(selectedDevice.VidPid ?? string.Empty, @"^[0-9a-fA-F]{4}:[0-9a-fA-F]{4}$"))
        {
            error = "The selected Windows device does not report a valid VID:PID. Refresh the device list and reconnect the board in DFU mode.";
            return false;
        }

        var matches = listedDevices
            .Where(item => string.Equals(item.VidPid, selectedDevice.VidPid, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(selectedDevice.UsbSerial))
        {
            matches = matches
                .Where(item => string.Equals(item.UsbSerial, selectedDevice.UsbSerial, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        matches = CollapseAlternateSettings(matches);
        if (matches.Count == 0)
        {
            error = $"dfu-util did not enumerate the selected device ({selectedDevice.VidPid}). Re-enter DFU mode, reconnect it, and refresh the device list.";
            return false;
        }

        if (matches.Count > 1)
        {
            error = $"More than one DFU device matches {selectedDevice.VidPid} and the selected device has no unique serial number. Disconnect other matching boards and refresh.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(matches[0].UsbSerial) && string.IsNullOrWhiteSpace(matches[0].UsbPath))
        {
            error = "dfu-util did not report a serial number or USB path for the selected device. Flashing is blocked because the target cannot be identified safely.";
            return false;
        }

        match = matches[0];
        return true;
    }

    private static List<DfuListDevice> CollapseAlternateSettings(IEnumerable<DfuListDevice> devices)
    {
        var identified = devices
            .Where(item => !string.IsNullOrWhiteSpace(item.UsbSerial) || !string.IsNullOrWhiteSpace(item.UsbPath))
            .GroupBy(item => (
                item.VidPid.ToUpperInvariant(),
                Identity: string.IsNullOrWhiteSpace(item.UsbPath)
                    ? $"serial:{item.UsbSerial?.ToUpperInvariant()}"
                    : $"path:{item.UsbPath.ToUpperInvariant()}"))
            .Select(group => group
                .OrderBy(item => item.AlternateSetting == 0 ? 0 : 1)
                .First());
        var unidentified = devices.Where(item =>
            string.IsNullOrWhiteSpace(item.UsbSerial) && string.IsNullOrWhiteSpace(item.UsbPath));
        return identified.Concat(unidentified).ToList();
    }

    private async Task<DfuCommandResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IProgress<FlashProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_dfuUtilPath))
        {
            throw new FileNotFoundException("The bundled dfu-util executable was not found.", _dfuUtilPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _dfuUtilPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_dfuUtilPath) ?? Environment.CurrentDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start dfu-util at '{_dfuUtilPath}'.");
        }

        var formattedArguments = FormatArguments(arguments);
        var stdoutTask = ReadOutputAsync(process.StandardOutput, progress);
        var stderrTask = ReadOutputAsync(process.StandardError, progress);

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var timedOut = false;
        var wasCancelled = false;
        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
            wasCancelled = cancellationToken.IsCancellationRequested;
            timedOut = !wasCancelled && timeoutSource.IsCancellationRequested;
            TryKill(process);
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"dfu-util did not stop within 10 seconds after cancellation or timeout. Executable: '{_dfuUtilPath}'.", ex);
            }
        }

        var standardOutput = await stdoutTask.ConfigureAwait(false);
        var standardError = await stderrTask.ConfigureAwait(false);
        return new DfuCommandResult(
            _dfuUtilPath,
            formattedArguments,
            process.ExitCode,
            standardOutput,
            standardError,
            timedOut,
            wasCancelled);
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, IProgress<FlashProgress>? progress)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            output.AppendLine(line);
            PublishProgress(line, progress);
        }

        return output.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            if (!process.HasExited)
            {
                throw;
            }
        }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited)
        {
        }
    }

    private static string FormatArguments(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(argument =>
            argument.Any(char.IsWhiteSpace) || argument.Contains('"')
                ? $"\"{argument.Replace("\"", "\\\"")}\""
                : argument));
    }

    private static void PublishProgress(string line, IProgress<FlashProgress>? progress)
    {
        var match = PercentRegex.Match(line);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, out var value))
        {
            progress?.Report(new FlashProgress { Status = line });
            return;
        }

        progress?.Report(new FlashProgress
        {
            Percent = Math.Clamp(value, 0, 100),
            Status = line
        });
    }
}
