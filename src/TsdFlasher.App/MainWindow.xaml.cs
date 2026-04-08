using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using IOMini2Tool.Models;
using IOMini2Tool.Services;

namespace IOMini2Tool;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DeviceDiscoveryService _deviceDiscoveryService = new();
    private readonly DfuUtilService _dfuUtilService;
    private readonly DispatcherTimer _refreshTimer;

    private CancellationTokenSource? _uploadCts;
    private string _firmwarePath = string.Empty;
    private DeviceInfo? _selectedComPort;
    private DeviceInfo? _selectedDfuDevice;
    private double _uploadPercent;
    private string _uploadStatus = "Idle";
    private bool _isUploading;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        var appBaseDirectory = AppContext.BaseDirectory;
        var toolsRoot = Path.Combine(appBaseDirectory, "tools");
        _dfuUtilService = new DfuUtilService(toolsRoot);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshDeviceListsAsync().ConfigureAwait(true);

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public ObservableCollection<DeviceInfo> ComPorts { get; } = new();

    public ObservableCollection<DeviceInfo> DfuDevices { get; } = new();

    public string FirmwarePath
    {
        get => _firmwarePath;
        set
        {
            if (value == _firmwarePath)
            {
                return;
            }

            _firmwarePath = value;
            OnPropertyChanged();
        }
    }

    public DeviceInfo? SelectedComPort
    {
        get => _selectedComPort;
        set
        {
            if (Equals(value, _selectedComPort))
            {
                return;
            }

            _selectedComPort = value;
            OnPropertyChanged();
        }
    }

    public DeviceInfo? SelectedDfuDevice
    {
        get => _selectedDfuDevice;
        set
        {
            if (Equals(value, _selectedDfuDevice))
            {
                return;
            }

            _selectedDfuDevice = value;
            OnPropertyChanged();
        }
    }

    public double UploadPercent
    {
        get => _uploadPercent;
        set
        {
            if (Math.Abs(value - _uploadPercent) < 0.01)
            {
                return;
            }

            _uploadPercent = value;
            OnPropertyChanged();
        }
    }

    public string UploadStatus
    {
        get => _uploadStatus;
        set
        {
            if (value == _uploadStatus)
            {
                return;
            }

            _uploadStatus = value;
            OnPropertyChanged();
        }
    }

    public bool IsUploading
    {
        get => _isUploading;
        set
        {
            if (value == _isUploading)
            {
                return;
            }

            _isUploading = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Log($"App started from {AppContext.BaseDirectory}");
        await RefreshDeviceListsAsync().ConfigureAwait(true);
        _refreshTimer.Start();

        var available = await _dfuUtilService.IsAvailableAsync(CancellationToken.None).ConfigureAwait(true);
        if (available)
        {
            Log("dfu-util check: OK");
        }
        else
        {
            Log("dfu-util check: FAILED. Ensure tools\\dfu-util\\dfu-util.exe is bundled.");
            UploadStatus = "dfu-util missing";
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
        _uploadCts?.Cancel();
        _uploadCts?.Dispose();
        _uploadCts = null;
    }

    private Task RefreshDeviceListsAsync()
    {
        var currentComId = SelectedComPort?.Id;
        var currentDfuId = SelectedDfuDevice?.Id;

        try
        {
            var comPorts = _deviceDiscoveryService.GetComPorts();
            ReplaceCollection(ComPorts, comPorts);
            SelectedComPort = SelectPreviousOrFirst(ComPorts, currentComId);
        }
        catch (Exception ex)
        {
            Log($"COM refresh error: {ex.Message}");
        }

        try
        {
            var dfuDevices = _deviceDiscoveryService.GetDfuDevices();
            ReplaceCollection(DfuDevices, dfuDevices);
            SelectedDfuDevice = SelectPreviousOrFirst(DfuDevices, currentDfuId);
            UploadStatus = IsUploading ? UploadStatus : $"Ready ({DfuDevices.Count} DFU device(s))";
        }
        catch (Exception ex)
        {
            Log($"DFU refresh error: {ex.Message}");
            if (!IsUploading)
            {
                UploadStatus = "DFU refresh failed";
            }
        }

        return Task.CompletedTask;
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDeviceListsAsync().ConfigureAwait(true);
        Log("Device list refreshed.");
    }

    private void BrowseFirmware_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select firmware file",
            Filter = "Firmware (*.bin;*.elf)|*.bin;*.elf|BIN files (*.bin)|*.bin|ELF files (*.elf)|*.elf|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        FirmwarePath = dialog.FileName;
        Log($"Firmware selected: {FirmwarePath}");
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (IsUploading)
        {
            return;
        }

        if (!ValidateBeforeUpload())
        {
            return;
        }

        IsUploading = true;
        UploadPercent = 0;
        UploadStatus = "Uploading...";

        _uploadCts = new CancellationTokenSource();
        var progress = new Progress<FlashProgress>(info =>
        {
            if (info.Percent > 0)
            {
                UploadPercent = info.Percent;
            }

            if (!string.IsNullOrWhiteSpace(info.Status))
            {
                UploadStatus = info.Status;
            }
        });

        try
        {
            Log("Starting upload...");
            var exitCode = await _dfuUtilService.FlashAsync(
                SelectedDfuDevice!,
                FirmwarePath,
                progress,
                Log,
                _uploadCts.Token).ConfigureAwait(true);

            if (exitCode == 0)
            {
                UploadPercent = 100;
                UploadStatus = "Upload complete";
                Log("Upload completed successfully.");
            }
            else
            {
                UploadStatus = $"Upload failed (exit {exitCode})";
                Log($"Upload failed with exit code {exitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            UploadStatus = "Canceled";
            Log("Upload canceled by user.");
        }
        catch (Exception ex)
        {
            UploadStatus = "Upload error";
            Log($"Upload exception: {ex.Message}");
        }
        finally
        {
            IsUploading = false;
            _uploadCts?.Dispose();
            _uploadCts = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!IsUploading)
        {
            return;
        }

        _uploadCts?.Cancel();
    }

    private bool ValidateBeforeUpload()
    {
        if (SelectedDfuDevice is null)
        {
            UploadStatus = "Select DFU device";
            Log("No DFU device selected.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(FirmwarePath) || !File.Exists(FirmwarePath))
        {
            UploadStatus = "Select firmware file";
            Log("No firmware selected or file not found.");
            return false;
        }

        var extension = Path.GetExtension(FirmwarePath).ToLowerInvariant();
        if (extension != ".bin" && extension != ".elf")
        {
            UploadStatus = "Unsupported file";
            Log("Unsupported firmware extension. Use .bin or .elf.");
            return false;
        }

        if (!File.Exists(_dfuUtilService.ExecutablePath))
        {
            UploadStatus = "dfu-util missing";
            Log($"dfu-util not found at {_dfuUtilService.ExecutablePath}");
            return false;
        }

        return true;
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                ConsoleText.AppendText(line);
                ConsoleText.ScrollToEnd();
            });
            return;
        }

        ConsoleText.AppendText(line);
        ConsoleText.ScrollToEnd();
    }

    private static void ReplaceCollection(ObservableCollection<DeviceInfo> collection, IReadOnlyList<DeviceInfo> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private static DeviceInfo? SelectPreviousOrFirst(ObservableCollection<DeviceInfo> collection, string? previousId)
    {
        if (collection.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(previousId))
        {
            var existing = collection.FirstOrDefault(x => string.Equals(x.Id, previousId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }
        }

        return collection[0];
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}