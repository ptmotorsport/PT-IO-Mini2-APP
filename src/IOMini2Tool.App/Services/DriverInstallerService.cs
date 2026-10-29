using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using IOMini2Tool.Models;

namespace IOMini2Tool.Services;

public sealed record DriverInstallResult(bool Succeeded, bool WasCancelled, string Message, IReadOnlyList<string> Diagnostics);

public sealed record DriverProcessResult(
    string ExecutablePath,
    string Arguments,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

public static class ArduinoRenesasDriverPackage
{
    private static readonly HashSet<string> SupportedHardwareIds = new(StringComparer.OrdinalIgnoreCase)
    {
        @"USB\VID_2341&PID_0069&REV_0100&MI_02",
        @"USB\VID_2341&PID_0369&REV_0100",
        @"USB\VID_2341&PID_006E&REV_0100&MI_02",
        @"USB\VID_2341&PID_016E&REV_0100",
        @"USB\VID_2341&PID_0071&REV_0100&MI_02",
        @"USB\VID_2341&PID_0171&REV_0100",
        @"USB\VID_2341&PID_0068&REV_0100&MI_02",
        @"USB\VID_2341&PID_0368&REV_0100",
        @"USB\VID_2341&PID_006C&REV_0100&MI_02",
        @"USB\VID_2341&PID_016C&REV_0100",
        @"USB\VID_2341&PID_0074&REV_0100&MI_02",
        @"USB\VID_2341&PID_0374&REV_0100"
    };

    private static readonly Regex TokenRegex = new(@"%([^%]+)%", RegexOptions.Compiled);

    public static bool IsSupportedHardwareId(string hardwareId)
    {
        return SupportedHardwareIds.Contains(NormalizeHardwareId(hardwareId));
    }

    public static bool IsTrustedCatalogSignature(string status, string signer)
    {
        return string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase)
               && signer.Contains("Arduino", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryValidateInf(string infContents, IEnumerable<string> targetHardwareIds, out string? matchedHardwareId, out string error)
    {
        matchedHardwareId = null;
        error = string.Empty;
        var sections = ParseSections(infContents);

        if (!TryGetSection(sections, "Strings", out var strings)
            || !string.Equals(GetValue(strings, "ManufacturerName"), "Arduino", StringComparison.OrdinalIgnoreCase)
            || !TryGetSection(sections, "Version", out var version)
            || !string.Equals(Expand(GetValue(version, "Provider"), strings), "Arduino", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(GetValue(version, "Class"), "USBDevice", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(GetValue(version, "CatalogFile"), "renesas.cat", StringComparison.OrdinalIgnoreCase))
        {
            error = "The selected INF is not the Arduino Renesas WinUSB driver package (expected Arduino provider, USBDevice class, and renesas.cat).";
            return false;
        }

        if (!TryGetSection(sections, "Manufacturer", out var manufacturer)
            || !manufacturer.Any(line => line.Contains("Standard", StringComparison.OrdinalIgnoreCase)
                                         && line.Contains("NTamd64", StringComparison.OrdinalIgnoreCase))
            || !TryGetSection(sections, "Standard.NTamd64", out var models)
            || !HasWinUsbInstallSections(sections))
        {
            error = "The INF does not contain the expected 64-bit Windows WinUSB installation sections.";
            return false;
        }

        var listedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in models)
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                error = $"The INF contains an invalid model entry: '{line}'.";
                return false;
            }

            var hardwareId = NormalizeHardwareId(Expand(parts[^1], strings));
            if (!hardwareId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
            {
                error = $"The INF includes a non-USB model entry ('{hardwareId}'). It will not be installed.";
                return false;
            }

            var assignment = parts[0].Split('=', 2, StringSplitOptions.TrimEntries);
            if (assignment.Length != 2 || !string.Equals(assignment[1], "USB_Install", StringComparison.OrdinalIgnoreCase))
            {
                error = $"The INF maps '{hardwareId}' to an unexpected installation section. It will not be installed.";
                return false;
            }

            if (!SupportedHardwareIds.Contains(hardwareId))
            {
                error = $"The INF includes unsupported USB hardware ID '{hardwareId}'. It will not be installed.";
                return false;
            }

            listedIds.Add(hardwareId);
        }

        if (listedIds.Count == 0)
        {
            error = "The INF does not list a supported Arduino Renesas USB hardware ID.";
            return false;
        }

        matchedHardwareId = targetHardwareIds
            .Select(NormalizeHardwareId)
            .FirstOrDefault(listedIds.Contains);

        if (matchedHardwareId == null)
        {
            error = "The selected device's hardware IDs are not listed by the Arduino Renesas INF. Custom VID/PID devices are not supported by this installer.";
            return false;
        }

        return true;
    }

    private static bool HasWinUsbInstallSections(IReadOnlyDictionary<string, List<string>> sections)
    {
        return TryGetSection(sections, "USB_Install", out var install)
               && install.Any(line => line.Contains("Include", StringComparison.OrdinalIgnoreCase)
                                      && line.Contains("winusb.inf", StringComparison.OrdinalIgnoreCase))
               && install.Any(line => line.Contains("Needs", StringComparison.OrdinalIgnoreCase)
                                      && line.Contains("WINUSB.NT", StringComparison.OrdinalIgnoreCase))
               && TryGetSection(sections, "USB_Install.Services", out var services)
               && services.Any(line => line.Contains("Include", StringComparison.OrdinalIgnoreCase)
                                       && line.Contains("winusb.inf", StringComparison.OrdinalIgnoreCase))
               && services.Any(line => line.Contains("Needs", StringComparison.OrdinalIgnoreCase)
                                       && line.Contains("WINUSB.NT.Services", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, List<string>> ParseSections(string infContents)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? currentSection = null;

        foreach (var rawLine in infContents.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Split(';', 2)[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (!sections.TryGetValue(name, out currentSection))
                {
                    currentSection = new List<string>();
                    sections.Add(name, currentSection);
                }

                continue;
            }

            currentSection?.Add(line);
        }

        return sections;
    }

    private static bool TryGetSection(IReadOnlyDictionary<string, List<string>> sections, string name, out List<string> lines)
    {
        return sections.TryGetValue(name, out lines!);
    }

    private static string GetValue(IEnumerable<string> lines, string key)
    {
        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator > 0 && string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim().Trim('"');
            }
        }

        return string.Empty;
    }

    private static string Expand(string value, IReadOnlyList<string> strings)
    {
        var definitions = strings
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('"'), StringComparer.OrdinalIgnoreCase);

        return TokenRegex.Replace(value, match =>
            definitions.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement : match.Value);
    }

    private static string NormalizeHardwareId(string hardwareId)
    {
        return hardwareId.Trim().Trim('"').Replace('/', '\\').ToUpperInvariant();
    }
}

public sealed class DriverInstallerService
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ElevatedProcessTimeout = TimeSpan.FromMinutes(4);
    private const string ElevatedInstallArgument = "--elevated-driver-install";

    public async Task<DriverInstallResult> InstallOrRepairAsync(DeviceInfo device, string infPath)
    {
        var diagnostics = new List<string>
        {
            $"Selected USB ID: {device.VidPid ?? "unknown"}",
            $"Selected PnP ID: {device.Id}",
            $"Selected hardware IDs: {string.Join("; ", device.HardwareIds)}",
            $"Current driver: service={device.DriverService}; provider={device.DriverProvider}; signer={device.DriverSigner}; version={device.DriverVersion}; INF={device.DriverInfName}; WMI signed={device.DriverIsSigned?.ToString() ?? "unknown"}; status={device.PnpStatus}; configError={device.ConfigManagerErrorCode?.ToString() ?? "unknown"}",
            $"Driver package INF: {infPath}"
        };

        try
        {
            var validation = await VerifyPackageAsync(infPath, device.HardwareIds, diagnostics).ConfigureAwait(false);
            if (!validation.Succeeded || validation.MatchedHardwareId == null)
            {
                return new DriverInstallResult(false, false, validation.Message, diagnostics);
            }

            var appPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(appPath))
            {
                return new DriverInstallResult(false, false, "Cannot determine the application executable to request administrator approval.", diagnostics);
            }

            var resultPath = Path.Combine(Path.GetTempPath(), $"iomini2-driver-{Guid.NewGuid():N}.json");
            var elevatedArguments = new[] { ElevatedInstallArgument, infPath, validation.MatchedHardwareId, resultPath };
            var startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var argument in elevatedArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            diagnostics.Add($"Elevated command: {appPath} {FormatArguments(elevatedArguments)}");

            int? elevatedExitCode = null;
            try
            {
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Windows did not start the elevated driver installer.");
                using var timeoutSource = new CancellationTokenSource(ElevatedProcessTimeout);
                try
                {
                    await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
                {
                    try
                    {
                        TryKill(process);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is TimeoutException or Win32Exception)
                    {
                        diagnostics.Add($"Could not stop the elevated installer after timeout: {ex.Message}");
                        return new DriverInstallResult(false, false, "The elevated installer timed out and Windows could not confirm that it stopped. Check Task Manager and Device Manager before retrying.", diagnostics);
                    }

                    diagnostics.Add("Elevated installer exit code: unknown; timed out=true");
                    return new DriverInstallResult(false, false, "The elevated driver installation timed out and was stopped. Reconnect the device and check Windows Device Manager before trying again.", diagnostics);
                }

                elevatedExitCode = process.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                diagnostics.Add("Elevated installer exit status: cancelled by the user at the Windows administrator prompt.");
                return new DriverInstallResult(false, true, "Administrator approval was cancelled. No driver changes were made.", diagnostics);
            }
            catch (Win32Exception ex)
            {
                diagnostics.Add($"Elevated installer could not start: Win32 error {ex.NativeErrorCode}: {ex.Message}");
                return new DriverInstallResult(false, false, "Windows could not start the administrator-elevated installer. Check Windows security policy and retry.", diagnostics);
            }
            catch (OperationCanceledException)
            {
                return new DriverInstallResult(false, false, "The elevated driver installation timed out. Reconnect the device and check Windows Device Manager before trying again.", diagnostics);
            }

            if (!File.Exists(resultPath))
            {
                diagnostics.Add($"Elevated installer exit code: {elevatedExitCode?.ToString() ?? "unknown"}");
                return new DriverInstallResult(false, false, "The elevated installer did not return its result log. Check administrator approval and Windows security policy, then retry.", diagnostics);
            }

            var helperResult = JsonSerializer.Deserialize<ElevatedInstallResult>(await File.ReadAllTextAsync(resultPath).ConfigureAwait(false));
            File.Delete(resultPath);
            if (helperResult == null)
            {
                return new DriverInstallResult(false, false, "The elevated installer returned an unreadable result. Check Windows Device Manager before retrying.", diagnostics);
            }

            diagnostics.AddRange(helperResult.Diagnostics);
            return new DriverInstallResult(
                helperResult.Succeeded,
                false,
                helperResult.Message,
                diagnostics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or Win32Exception or System.Text.Json.JsonException or ArgumentException or System.Security.SecurityException)
        {
            diagnostics.Add($"Driver installation error: {ex.GetType().Name}: {ex.Message}");
            return new DriverInstallResult(false, false, ex.Message, diagnostics);
        }
    }

    public static async Task<int> RunElevatedInstallHelperAsync(string infPath, string targetHardwareId, string resultPath)
    {
        if (!IsSafeHelperResultPath(resultPath))
        {
            return 87;
        }

        var diagnostics = new List<string>();
        try
        {
            var validation = await VerifyPackageAsync(infPath, [targetHardwareId], diagnostics).ConfigureAwait(false);
            if (!validation.Succeeded)
            {
                await WriteHelperResultAsync(resultPath, false, validation.Message, diagnostics).ConfigureAwait(false);
                return 87;
            }

            var pnputilPath = GetPnPUtilPath();
            var pnputilArguments = new[] { "/add-driver", infPath, "/install" };
            diagnostics.Add($"Executing: {pnputilPath} {FormatArguments(pnputilArguments)}");
            var installResult = await RunProcessAsync(pnputilPath, pnputilArguments, ProcessTimeout).ConfigureAwait(false);
            diagnostics.Add(FormatProcessResult(installResult));

            var succeeded = installResult.ExitCode == 0 && !installResult.TimedOut;
            var message = succeeded
                ? "Driver package installation completed. Refresh the device list and run the DFU preflight again."
                : installResult.TimedOut
                    ? "Windows driver installation timed out. Check Device Manager and retry only after the device state is known."
                    : $"Windows rejected the driver package (pnputil exit code {installResult.ExitCode?.ToString() ?? "unknown"}). Check the package signature, Windows compatibility, and the diagnostic output.";
            await WriteHelperResultAsync(resultPath, succeeded, message, diagnostics).ConfigureAwait(false);
            return succeeded ? 0 : installResult.ExitCode ?? 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or Win32Exception or ArgumentException or System.Security.SecurityException)
        {
            diagnostics.Add($"Elevated installer error: {ex.GetType().Name}: {ex.Message}");
            await WriteHelperResultAsync(resultPath, false, ex.Message, diagnostics).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<(bool Succeeded, string Message, string? MatchedHardwareId)> VerifyPackageAsync(
        string infPath,
        IEnumerable<string> targetHardwareIds,
        List<string> diagnostics)
    {
        if (!File.Exists(infPath))
        {
            return (false, "The selected driver INF file no longer exists.", null);
        }

        var infContents = await File.ReadAllTextAsync(infPath).ConfigureAwait(false);
        if (!ArduinoRenesasDriverPackage.TryValidateInf(infContents, targetHardwareIds, out var matchedHardwareId, out var error))
        {
            diagnostics.Add($"INF validation failed: {error}");
            return (false, error, null);
        }

        var catalogPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(infPath))!, "renesas.cat");
        if (!File.Exists(catalogPath))
        {
            const string message = "The matching renesas.cat catalog is missing. Use the complete Arduino Renesas driver package; an INF by itself is not installable.";
            diagnostics.Add(message);
            return (false, message, null);
        }

        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var escapedCatalogPath = catalogPath.Replace("'", "''");
        var signatureScript =
            "$signature = Get-AuthenticodeSignature -LiteralPath '" + escapedCatalogPath + "'; " +
            "Write-Output ('Signature status: ' + $signature.Status); " +
            "$signer = ''; if ($null -ne $signature.SignerCertificate) { $signer = $signature.SignerCertificate.Subject }; " +
            "Write-Output ('Signer: ' + $signer); " +
            "if ($signature.Status -ne 'Valid' -or $signer -notmatch 'Arduino') { exit 1 }";
        var signatureArguments = new[] { "-NoProfile", "-NonInteractive", "-Command", signatureScript };
        diagnostics.Add($"Verifying catalog signature: {powerShellPath} {FormatArguments(signatureArguments)}");
        var signatureResult = await RunProcessAsync(powerShellPath, signatureArguments, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        diagnostics.Add(FormatProcessResult(signatureResult));

        var signatureLines = signatureResult.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var signatureStatus = signatureLines
            .FirstOrDefault(line => line.StartsWith("Signature status:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1].Trim() ?? "unknown";
        var signer = signatureLines
            .FirstOrDefault(line => line.StartsWith("Signer:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1].Trim() ?? string.Empty;
        if (signatureResult.TimedOut
            || signatureResult.ExitCode != 0
            || !ArduinoRenesasDriverPackage.IsTrustedCatalogSignature(signatureStatus, signer))
        {
            const string message = "Windows could not verify the renesas.cat as a valid signature from Arduino. Obtain a complete, signed driver package from the official Arduino Renesas board package source.";
            return (false, message, null);
        }

        return (true, "The signed Arduino Renesas driver package matches this device.", matchedHardwareId);
    }

    private static async Task<DriverProcessResult> RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start '{executablePath}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"Process '{executablePath}' did not stop within 10 seconds after timeout.", ex);
            }
        }

        return new DriverProcessResult(
            executablePath,
            FormatArguments(arguments),
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false),
            timedOut);
    }

    private static string GetPnPUtilPath()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
        {
            systemDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative");
        }

        return Path.Combine(systemDirectory, "pnputil.exe");
    }

    private static bool IsSafeHelperResultPath(string resultPath)
    {
        try
        {
            var tempDirectory = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(resultPath);
            return string.Equals(Path.GetDirectoryName(fullPath), tempDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                   && Regex.IsMatch(Path.GetFileName(fullPath), @"^iomini2-driver-[0-9a-f]{32}\.json$", RegexOptions.IgnoreCase)
                   && !File.Exists(fullPath)
                   && !Directory.Exists(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
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
        catch (Win32Exception) when (process.HasExited)
        {
        }
    }

    private static string FormatProcessResult(DriverProcessResult result)
    {
        return $"Command result: {result.ExecutablePath} {result.Arguments}{Environment.NewLine}" +
               $"Exit code: {result.ExitCode?.ToString() ?? "unknown"}; timed out: {result.TimedOut}{Environment.NewLine}" +
               $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
               $"stderr:{Environment.NewLine}{result.StandardError}";
    }

    private static string FormatArguments(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(argument =>
            argument.Any(char.IsWhiteSpace) || argument.Contains('"')
                ? $"\"{argument.Replace("\"", "\\\"")}\""
                : argument));
    }

    private static async Task WriteHelperResultAsync(string resultPath, bool succeeded, string message, IReadOnlyList<string> diagnostics)
    {
        var result = new ElevatedInstallResult(succeeded, message, diagnostics);
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result)).ConfigureAwait(false);
    }

    private sealed record ElevatedInstallResult(bool Succeeded, string Message, IReadOnlyList<string> Diagnostics);

}
