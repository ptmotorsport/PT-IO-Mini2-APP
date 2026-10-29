using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IOMini2Tool.Models;
using IOMini2Tool.Services;
using Microsoft.Win32;

namespace IOMini2Tool.ViewModels;

public partial class FirmwareViewModel : ViewModelBase
{
    private readonly DeviceDiscoveryService _discovery;
    private readonly DfuUtilService _dfuUtil;
    private readonly DriverInstallerService _driverInstaller = new();
    private readonly List<string> _supportLog = new();
    private CancellationTokenSource? _uploadCancellation;

    [ObservableProperty]
    private ObservableCollection<DeviceInfo> _dfuDevices = new();

    [ObservableProperty]
    private DeviceInfo? _selectedDfuDevice;

    [ObservableProperty]
    private string _firmwareFilePath = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressStatus = "Ready";

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isInstallingDriver;

    [ObservableProperty]
    private ObservableCollection<string> _consoleLines = new();

    [ObservableProperty]
    private bool _showStatusBanner;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private Brush _statusBackground = new SolidColorBrush(Color.FromRgb(34, 197, 94));

    /// <summary>Reminder shown on the Firmware tab since DFU/bootloader mode looks identical for
    /// every board variant - the hello message's model info isn't available once in bootloader.</summary>
    public string DeviceModelReminder { get; }

    public bool IsBusy => IsUploading || IsChecking || IsInstallingDriver;

    public FirmwareViewModel(DeviceDiscoveryService discovery, DfuUtilService dfuUtil, string lastKnownDeviceModel = "")
    {
        _discovery = discovery;
        _dfuUtil = dfuUtil;

        DeviceModelReminder = string.IsNullOrEmpty(lastKnownDeviceModel) || lastKnownDeviceModel == "Unknown"
            ? "Mini2 and DTM2 firmware are NOT interchangeable. Verify you selected the correct .bin for your board before uploading."
            : $"Last connected device was detected as {lastKnownDeviceModel}. Mini2 and DTM2 firmware are NOT interchangeable - verify this .bin matches that board.";

        StatusMessage = string.Empty;
        ShowStatusBanner = false;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await Task.Delay(50);
        await RefreshDfuDevicesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshDfuDevices))]
    private async Task RefreshDfuDevicesAsync()
    {
        try
        {
            var oldSelectionId = SelectedDfuDevice?.Id;
            var devices = await Task.Run(() => _discovery.GetDfuDevices());
            DfuDevices.Clear();
            foreach (var device in devices)
            {
                DfuDevices.Add(device);
            }

            SelectedDfuDevice = devices.FirstOrDefault(device =>
                                    string.Equals(device.Id, oldSelectionId, StringComparison.OrdinalIgnoreCase))
                                ?? devices.FirstOrDefault();

            if (DfuDevices.Count > 0)
            {
                AddConsoleLog($"Found {DfuDevices.Count} DFU-like Windows device(s)");
            }
            else
            {
                AddConsoleLog("No DFU-like Windows devices found. Put the board in bootloader mode, reconnect it, and refresh.");
            }
        }
        catch (Exception ex)
        {
            AddConsoleLog($"Windows USB device scan failed: {ex.GetType().Name}: {ex.Message}");
            ShowError("Windows could not read USB device information. Check Device Manager permissions and retry Refresh.");
        }
    }

    private bool CanRefreshDfuDevices() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBrowseFirmware))]
    private void BrowseFirmware()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Firmware Binary",
            Filter = "Binary Files (*.bin)|*.bin|DFU Files (*.dfu)|*.dfu|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            FirmwareFilePath = dialog.FileName;
            AddConsoleLog($"Selected firmware: {FirmwareFilePath}");
        }
    }

    private bool CanBrowseFirmware() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUploadFirmware))]
    private async Task UploadFirmware()
    {
        var selectedDevice = SelectedDfuDevice;
        var firmwarePath = FirmwareFilePath;
        if (selectedDevice == null || string.IsNullOrWhiteSpace(firmwarePath))
        {
            return;
        }

        if (!File.Exists(firmwarePath))
        {
            ShowError("Firmware file not found. Select an existing firmware file and retry.");
            AddConsoleLog($"Upload blocked: firmware file does not exist: {firmwarePath}");
            return;
        }

        if (!File.Exists(_dfuUtil.ExecutablePath))
        {
            ShowError("Bundled dfu-util.exe is missing. Restore the complete portable app folder.");
            AddConsoleLog($"Upload blocked: dfu-util executable missing at {_dfuUtil.ExecutablePath}");
            return;
        }

        IsUploading = true;
        ProgressPercent = 0;
        ProgressStatus = "Checking DFU prerequisites...";
        ShowStatusBanner = false;
        _uploadCancellation = new CancellationTokenSource();

        try
        {
            AddConsoleLog($"Upload requested for '{selectedDevice.DisplayName}'");
            AddConsoleLog($"Firmware file: {firmwarePath}; size: {new FileInfo(firmwarePath).Length} bytes");
            var target = await RunPreflightAsync(_uploadCancellation.Token);
            if (target == null)
            {
                ProgressStatus = "Preflight blocked upload.";
                return;
            }

            AddConsoleLog("DFU preflight passed. Starting firmware transfer.");
            ProgressStatus = "Starting upload...";
            var progress = new Progress<FlashProgress>(item =>
            {
                ProgressPercent = item.Percent;
                ProgressStatus = item.Status;
            });
            var result = await _dfuUtil.FlashAsync(target, firmwarePath, progress, _uploadCancellation.Token);
            LogDfuResult("Firmware upload", result);

            if (result.WasCancelled)
            {
                ProgressStatus = "Upload cancelled.";
                ShowWarning("Upload cancelled. Re-enter DFU mode and run the preflight again before retrying.");
                AddConsoleLog("Upload process was cancelled and terminated.");
            }
            else if (result.TimedOut)
            {
                ProgressStatus = "Upload timed out.";
                ShowError("Firmware upload exceeded five minutes and was stopped. Check the USB cable and board state before retrying.");
            }
            else if (result.Succeeded)
            {
                ProgressPercent = 100;
                ProgressStatus = "Upload complete. Device reset requested.";
                ShowSuccess("Firmware upload completed successfully.");
                AddConsoleLog("dfu-util reported a successful upload and reset request.");
            }
            else
            {
                ProgressStatus = "Upload failed.";
                ShowError($"Upload failed (dfu-util exit code {result.ExitCode?.ToString() ?? "unknown"}). The device may have disconnected; reconnect it, re-enter DFU mode, and rerun preflight.");
                AddConsoleLog("Do not assume the firmware was updated. Check the device state before attempting another upload.");
            }
        }
        catch (OperationCanceledException)
        {
            ProgressStatus = "Upload cancelled.";
            ShowWarning("Upload cancelled. Re-enter DFU mode and run the preflight again before retrying.");
            AddConsoleLog("Upload cancelled before the DFU operation completed.");
        }
        catch (Exception ex)
        {
            ProgressStatus = "Upload error.";
            ShowError($"Upload could not complete: {ex.Message}");
            AddConsoleLog($"Upload error: {ex.GetType().Name}: {ex}");
        }
        finally
        {
            IsChecking = true;
            IsUploading = false;
            _uploadCancellation?.Dispose();
            _uploadCancellation = null;
            await RefreshDfuDevicesAsync();
            IsChecking = false;
        }
    }

    private bool CanUploadFirmware()
    {
        return !IsBusy
               && SelectedDfuDevice != null
               && !string.IsNullOrWhiteSpace(FirmwareFilePath);
    }

    [RelayCommand(CanExecute = nameof(CanCancelUpload))]
    private void CancelUpload()
    {
        _uploadCancellation?.Cancel();
        AddConsoleLog("User requested upload cancellation.");
    }

    private bool CanCancelUpload() => IsUploading;

    [RelayCommand(CanExecute = nameof(CanCheckDevice))]
    private async Task CheckDevicePrerequisitesAsync()
    {
        IsChecking = true;
        ProgressStatus = "Checking DFU prerequisites...";
        ShowStatusBanner = false;
        try
        {
            var target = await RunPreflightAsync(CancellationToken.None);
            if (target != null)
            {
                ProgressStatus = "DFU prerequisites passed.";
                ShowSuccess($"DFU preflight passed for {target.VidPid}.");
            }
            else
            {
                ProgressStatus = "DFU preflight failed.";
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool CanCheckDevice() => !IsBusy && SelectedDfuDevice != null;

    [RelayCommand(CanExecute = nameof(CanInstallDriver))]
    private async Task InstallOrRepairDriverAsync()
    {
        var selectedDevice = SelectedDfuDevice;
        if (selectedDevice == null)
        {
            return;
        }

        var supportedHardwareId = selectedDevice.HardwareIds.FirstOrDefault(ArduinoRenesasDriverPackage.IsSupportedHardwareId);
        if (supportedHardwareId == null)
        {
            var message = "The selected device's USB hardware IDs are not supported by the Arduino Renesas driver package. No driver will be installed; custom VID/PID devices need their own vendor-supplied driver.";
            AddConsoleLog(message);
            ShowError(message);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select the signed Arduino Renesas driver package",
            Filter = "Windows driver INF (*.inf)|*.inf",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            $"Windows will verify and install/repair the signed Arduino Renesas WinUSB driver for {supportedHardwareId}. Administrator approval is required. Continue?",
            "Install or repair USB driver",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            AddConsoleLog("Driver installation was not confirmed; no changes were made.");
            return;
        }

        IsInstallingDriver = true;
        try
        {
            AddConsoleLog($"Driver installation requested for {supportedHardwareId} using '{dialog.FileName}'.");
            var result = await _driverInstaller.InstallOrRepairAsync(selectedDevice, dialog.FileName);
            foreach (var line in result.Diagnostics)
            {
                AddConsoleLog(line);
            }

            if (result.Succeeded)
            {
                ShowSuccess(result.Message);
                AddConsoleLog(result.Message);
            }
            else if (result.WasCancelled)
            {
                ShowWarning(result.Message);
                AddConsoleLog(result.Message);
            }
            else
            {
                ShowError(result.Message);
                AddConsoleLog($"Driver installation failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Driver installation failed: {ex.Message}");
            AddConsoleLog($"Driver installation exception: {ex.GetType().Name}: {ex}");
        }
        finally
        {
            await RefreshDfuDevicesAsync();
            IsInstallingDriver = false;
        }
    }

    private bool CanInstallDriver() => !IsBusy && SelectedDfuDevice != null;

    [RelayCommand]
    private async Task ExportSupportLogAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export support log",
            Filter = "Text files (*.txt)|*.txt",
            FileName = $"IO-Mini2-support-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var header = new[]
            {
                "IO-Mini2-Tool support log",
                $"Generated: {DateTimeOffset.Now:O}",
                $"Windows: {Environment.OSVersion}",
                $"Application version: {typeof(FirmwareViewModel).Assembly.GetName().Version}",
                "This log may contain USB serial numbers and local firmware paths. Review/redact it before sharing."
            };
            await File.WriteAllTextAsync(dialog.FileName, string.Join(Environment.NewLine, header.Concat(_supportLog)));
            AddConsoleLog($"Support log exported to '{dialog.FileName}'.");
            ShowSuccess("Support log exported. Review it for USB serial numbers and local paths before sharing.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not export support log: {ex.Message}");
            AddConsoleLog($"Support-log export failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<DfuListDevice?> RunPreflightAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedDfuDevice;
        if (selected == null)
        {
            const string message = "Select a DFU device before running the preflight.";
            AddConsoleLog(message);
            ShowError(message);
            return null;
        }

        AddDeviceDiagnostics(selected);
        if (!RegexIsValidVidPid(selected.VidPid))
        {
            const string message = "The selected Windows device does not report a valid VID:PID. Refresh the device list and reconnect the board in DFU mode.";
            AddConsoleLog($"Preflight blocked: {message}");
            ShowError(message);
            return null;
        }

        IReadOnlyList<DeviceInfo> currentDevices;
        try
        {
            currentDevices = await Task.Run(() => _discovery.GetDfuDevices(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AddConsoleLog("DFU preflight was cancelled before enumeration.");
            ShowWarning("DFU preflight was cancelled; no firmware was written.");
            return null;
        }
        catch (Exception ex)
        {
            AddConsoleLog($"Preflight Windows device query failed: {ex.GetType().Name}: {ex}");
            ShowError("Windows could not verify the current USB device and driver state. Check Device Manager, then refresh and retry.");
            return null;
        }

        var currentDevice = currentDevices.FirstOrDefault(device =>
            string.Equals(device.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        if (currentDevice == null)
        {
            const string message = "The selected device is no longer present in Windows. Reconnect it in DFU mode and refresh the device list.";
            AddConsoleLog($"Preflight blocked: {message}");
            ShowError(message);
            return null;
        }

        SelectedDfuDevice = currentDevice;
        AddDeviceDiagnostics(currentDevice);
        if (!DeviceDiscoveryService.TryValidateWinUsbBinding(currentDevice, out var driverError))
        {
            AddConsoleLog($"Preflight blocked: {driverError}");
            ShowError(driverError);
            return null;
        }

        DfuCommandResult listing;
        try
        {
            listing = await _dfuUtil.ListDevicesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AddConsoleLog("DFU enumeration was cancelled before it could complete.");
            ShowWarning("DFU preflight was cancelled; no firmware was written.");
            return null;
        }
        catch (Exception ex)
        {
            AddConsoleLog($"dfu-util -l could not run: {ex.GetType().Name}: {ex}");
            ShowError($"DFU enumeration could not start: {ex.Message}. Check the bundled dfu-util files and retry.");
            return null;
        }

        LogDfuResult("DFU device enumeration", listing);
        if (listing.WasCancelled)
        {
            ShowWarning("DFU preflight was cancelled; no firmware was written.");
            return null;
        }

        if (listing.TimedOut)
        {
            const string message = "dfu-util -l timed out. Check the USB connection, close other tools using the device, and retry.";
            AddConsoleLog(message);
            ShowError(message);
            return null;
        }

        if (listing.ExitCode != 0)
        {
            var message = $"dfu-util -l failed with exit code {listing.ExitCode?.ToString() ?? "unknown"}. Verify the USB driver and inspect stderr in the support log.";
            AddConsoleLog(message);
            ShowError(message);
            return null;
        }

        var listedDevices = DfuUtilService.ParseListOutput(listing.StandardOutput + Environment.NewLine + listing.StandardError);
        if (!DfuUtilService.TryMatchSelectedDevice(currentDevice, listedDevices, out var match, out var matchError))
        {
            AddConsoleLog($"Preflight blocked: {matchError}");
            ShowError(matchError);
            return null;
        }

        AddConsoleLog($"Preflight match: VID:PID={match!.VidPid}; serial={match.UsbSerial ?? "not reported"}; USB path={match.UsbPath ?? "not reported"}");
        ProgressStatus = "DFU prerequisites passed.";
        return match;
    }

    private static bool RegexIsValidVidPid(string? value)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, @"^[0-9a-fA-F]{4}:[0-9a-fA-F]{4}$");
    }

    private void AddDeviceDiagnostics(DeviceInfo device)
    {
        AddConsoleLog($"USB device: name='{device.DisplayName}'; VID:PID={device.VidPid ?? "unknown"}; serial={device.UsbSerial ?? "not reported"}; PnP ID='{device.Id}'");
        AddConsoleLog($"Driver: service={device.DriverService}; provider={device.DriverProvider}; signer={device.DriverSigner}; version={device.DriverVersion}; INF={device.DriverInfName}; WMI signed={device.DriverIsSigned?.ToString() ?? "unknown"}; status={device.PnpStatus}; configError={device.ConfigManagerErrorCode?.ToString() ?? "unknown"}");
        AddConsoleLog($"Windows hardware IDs: {string.Join("; ", device.HardwareIds)}");
    }

    private void LogDfuResult(string operation, DfuCommandResult result)
    {
        AddConsoleLog($"{operation} command: \"{result.ExecutablePath}\" {result.Arguments}");
        AddConsoleLog($"{operation} exit code: {result.ExitCode?.ToString() ?? "unknown"}; timed out={result.TimedOut}; cancelled={result.WasCancelled}");
        AddConsoleLog($"{operation} stdout:{Environment.NewLine}{result.StandardOutput}");
        AddConsoleLog($"{operation} stderr:{Environment.NewLine}{result.StandardError}");
    }

    private void AddConsoleLog(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Application.Current.Dispatcher.Invoke(() =>
        {
            _supportLog.Add(timestampedMessage);
            ConsoleLines.Add(timestampedMessage);
        });
    }

    private void ShowSuccess(string message)
    {
        StatusMessage = message;
        StatusBackground = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        ShowStatusBanner = true;
    }

    private void ShowError(string message)
    {
        StatusMessage = message;
        StatusBackground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        ShowStatusBanner = true;
    }

    private void ShowWarning(string message)
    {
        StatusMessage = message;
        StatusBackground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        ShowStatusBanner = true;
    }

    partial void OnSelectedDfuDeviceChanged(DeviceInfo? value)
    {
        NotifyCommandsCanExecuteChanged();
    }

    partial void OnFirmwareFilePathChanged(string value)
    {
        NotifyCommandsCanExecuteChanged();
    }

    partial void OnIsUploadingChanged(bool value)
    {
        NotifyCommandsCanExecuteChanged();
    }

    partial void OnIsCheckingChanged(bool value)
    {
        NotifyCommandsCanExecuteChanged();
    }

    partial void OnIsInstallingDriverChanged(bool value)
    {
        NotifyCommandsCanExecuteChanged();
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        UploadFirmwareCommand.NotifyCanExecuteChanged();
        CancelUploadCommand.NotifyCanExecuteChanged();
        RefreshDfuDevicesCommand.NotifyCanExecuteChanged();
        BrowseFirmwareCommand.NotifyCanExecuteChanged();
        CheckDevicePrerequisitesCommand.NotifyCanExecuteChanged();
        InstallOrRepairDriverCommand.NotifyCanExecuteChanged();
    }
}
