using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IOMini2Tool.Models;

namespace IOMini2Tool.Services;

public sealed class DfuUtilService
{
    private static readonly Regex PercentRegex = new("(\\d{1,3})%", RegexOptions.Compiled);
    private readonly string _dfuUtilPath;

    public DfuUtilService(string toolsRootPath)
    {
        _dfuUtilPath = Path.Combine(toolsRootPath, "dfu-util", "dfu-util.exe");
    }

    public string ExecutablePath => _dfuUtilPath;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_dfuUtilPath))
        {
            return false;
        }

        var exitCode = await RunRawAsync("--version", cancellationToken).ConfigureAwait(false);
        return exitCode == 0;
    }

    public async Task<int> FlashAsync(
        DeviceInfo device,
        string firmwarePath,
        IProgress<FlashProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var args = BuildFlashArgs(device, firmwarePath);
        return await RunWithOutputAsync(args, progress, log, cancellationToken).ConfigureAwait(false);
    }

    private string BuildFlashArgs(DeviceInfo device, string firmwarePath)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(device.VidPid))
        {
            builder.Append("-d ").Append(device.VidPid).Append(' ');
        }

        if (!string.IsNullOrWhiteSpace(device.UsbSerial))
        {
            builder.Append("-S \"").Append(device.UsbSerial).Append("\" ");
        }

        builder.Append("-a 0 -D \"").Append(firmwarePath).Append("\" -R");
        return builder.ToString();
    }

    private async Task<int> RunRawAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = BuildStartInfo(arguments);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();

        using var registration = cancellationToken.Register(() =>
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private async Task<int> RunWithOutputAsync(
        string arguments,
        IProgress<FlashProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var startInfo = BuildStartInfo(arguments);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            try
            {
                log?.Invoke(e.Data);
                PublishProgress(e.Data, progress);
            }
            catch
            {
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            try
            {
                log?.Invoke(e.Data);
                PublishProgress(e.Data, progress);
            }
            catch
            {
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private ProcessStartInfo BuildStartInfo(string arguments)
    {
        return new ProcessStartInfo
        {
            FileName = _dfuUtilPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_dfuUtilPath) ?? Environment.CurrentDirectory
        };
    }

    private static void PublishProgress(string line, IProgress<FlashProgress>? progress)
    {
        var match = PercentRegex.Match(line);
        if (!match.Success)
        {
            progress?.Report(new FlashProgress { Status = line });
            return;
        }

        if (!double.TryParse(match.Groups[1].Value, out var value))
        {
            progress?.Report(new FlashProgress { Status = line });
            return;
        }

        var bounded = Math.Max(0, Math.Min(100, value));
        progress?.Report(new FlashProgress
        {
            Percent = bounded,
            Status = line
        });
    }
}