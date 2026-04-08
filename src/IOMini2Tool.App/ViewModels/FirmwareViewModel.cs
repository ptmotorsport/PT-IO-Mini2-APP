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
    private ObservableCollection<string> _consoleLines = new();

    [ObservableProperty]
    private bool _showStatusBanner;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private Brush _statusBackground = new SolidColorBrush(Color.FromRgb(34, 197, 94));

    public FirmwareViewModel(DeviceDiscoveryService discovery, DfuUtilService dfuUtil)
    {
        _discovery = discovery;
        _dfuUtil = dfuUtil;
        
        // Initialize with empty message
        StatusMessage = string.Empty;
        ShowStatusBanner = false;
        
        // Defer device discovery to avoid blocking UI thread
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Small delay to let the page render first
            await Task.Delay(50);
            await RefreshDfuDevicesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing firmware view: {ex.Message}");
            AddConsoleLog($"Error scanning for DFU devices: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshDfuDevicesAsync()
    {
        try
        {
            DfuDevices.Clear();
            var devices = await Task.Run(() => _discovery.GetDfuDevices());
            
            foreach (var device in devices)
            {
                DfuDevices.Add(device);
            }

            if (DfuDevices.Count > 0)
            {
                SelectedDfuDevice = DfuDevices[0];
                AddConsoleLog($"Found {DfuDevices.Count} DFU device(s)");
            }
            else
            {
                AddConsoleLog("No DFU devices found. Put device in bootloader mode.");
            }
        }
        catch (Exception ex)
        {
            AddConsoleLog($"Error scanning for DFU devices: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"GetDfuDevices error: {ex}");
        }
    }

    [RelayCommand]
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
            AddConsoleLog($"Selected firmware: {Path.GetFileName(FirmwareFilePath)}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUploadFirmware))]
    private async Task UploadFirmware()
    {
        if (SelectedDfuDevice == null || string.IsNullOrWhiteSpace(FirmwareFilePath))
        {
            return;
        }

        if (!File.Exists(FirmwareFilePath))
        {
            ShowError("Firmware file not found!");
            return;
        }

        // Verify dfu-util is available
        if (!File.Exists(_dfuUtil.ExecutablePath))
        {
            ShowError("dfu-util.exe not found in tools directory!");
            return;
        }

        IsUploading = true;
        ProgressPercent = 0;
        ProgressStatus = "Starting upload...";
        ShowStatusBanner = false;
        ConsoleLines.Clear();

        _uploadCancellation = new CancellationTokenSource();

        try
        {
            AddConsoleLog($"Uploading to device: {SelectedDfuDevice.DisplayName}");
            AddConsoleLog($"Firmware file: {FirmwareFilePath}");
            AddConsoleLog($"File size: {new FileInfo(FirmwareFilePath).Length / 1024} KB");
            AddConsoleLog("Starting dfu-util...");

            var progress = new Progress<FlashProgress>(p =>
            {
                ProgressPercent = p.Percent;
                ProgressStatus = p.Status;
            });

            var exitCode = await _dfuUtil.FlashAsync(
                SelectedDfuDevice,
                FirmwareFilePath,
                progress,
                AddConsoleLog,
                _uploadCancellation.Token
            );

            if (exitCode == 0)
            {
                ProgressPercent = 100;
                ProgressStatus = "Upload complete!";
                ShowSuccess("Firmware uploaded successfully!");
                AddConsoleLog("Upload completed successfully.");
            }
            else
            {
                ProgressStatus = "Upload failed.";
                ShowError($"Upload failed with exit code {exitCode}");
                AddConsoleLog($"dfu-util exited with code {exitCode}");
            }
        }
        catch (OperationCanceledException)
        {
            ProgressStatus = "Upload cancelled.";
            ShowWarning("Upload cancelled by user.");
            AddConsoleLog("Upload cancelled.");
        }
        catch (Exception ex)
        {
            ProgressStatus = "Upload error.";
            ShowError($"Upload error: {ex.Message}");
            AddConsoleLog($"Error: {ex.Message}");
        }
        finally
        {
            IsUploading = false;
            _uploadCancellation?.Dispose();
            _uploadCancellation = null;
        }
    }

    private bool CanUploadFirmware()
    {
        return !IsUploading 
               && SelectedDfuDevice != null 
               && !string.IsNullOrWhiteSpace(FirmwareFilePath);
    }

    [RelayCommand(CanExecute = nameof(IsUploading))]
    private void CancelUpload()
    {
        _uploadCancellation?.Cancel();
        AddConsoleLog("Cancelling upload...");
    }

    partial void OnSelectedDfuDeviceChanged(DeviceInfo? value)
    {
        UploadFirmwareCommand.NotifyCanExecuteChanged();
    }

    partial void OnFirmwareFilePathChanged(string value)
    {
        UploadFirmwareCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsUploadingChanged(bool value)
    {
        UploadFirmwareCommand.NotifyCanExecuteChanged();
        CancelUploadCommand.NotifyCanExecuteChanged();
    }

    private void AddConsoleLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ConsoleLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        });
    }

    private void ShowSuccess(string message)
    {
        StatusMessage = message;
        StatusBackground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
        ShowStatusBanner = true;
    }

    private void ShowError(string message)
    {
        StatusMessage = message;
        StatusBackground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
        ShowStatusBanner = true;
    }

    private void ShowWarning(string message)
    {
        StatusMessage = message;
        StatusBackground = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Yellow/Orange
        ShowStatusBanner = true;
    }
}
