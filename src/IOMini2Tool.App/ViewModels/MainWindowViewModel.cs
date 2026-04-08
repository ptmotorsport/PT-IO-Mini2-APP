using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IOMini2Tool.Models;
using IOMini2Tool.Services;

namespace IOMini2Tool.ViewModels;

/// <summary>
/// ViewModel for MainWindow - handles global connection state and navigation
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DeviceDiscoveryService _deviceDiscovery = new();
    private readonly DeviceCommunicationService _deviceCommunication = new();
    private readonly DfuUtilService _dfuUtil;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Frame _contentFrame;
    private DashboardViewModel? _currentDashboardViewModel;

    [ObservableProperty]
    private ObservableCollection<DeviceInfo> _comPorts = new();

    [ObservableProperty]
    private DeviceInfo? _selectedComPort;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _firmwareVersion = "";

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private DeviceInfo? _connectedDevice;

    [ObservableProperty]
    private string _selectedTab = "Dashboard";

    public DeviceCommunicationService CommunicationService => _deviceCommunication;

    public MainWindowViewModel(Frame contentFrame)
    {
        _contentFrame = contentFrame;
        
        // Initialize DFU utility service with tools path
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var toolsPath = System.IO.Path.Combine(appDirectory, "tools");
        _dfuUtil = new DfuUtilService(toolsPath);
        
        // Setup auto-refresh timer for COM ports
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (s, e) => RefreshComPorts();
        _refreshTimer.Start();

        // Subscribe to communication service events
        _deviceCommunication.ConnectionLost += OnConnectionLost;
        _deviceCommunication.ErrorOccurred += OnErrorOccurred;

        // Initial refresh
        RefreshComPorts();
        
        // Navigate to Dashboard on startup
        NavigateToPage("Dashboard");
    }

    [RelayCommand]
    private void RefreshComPorts()
    {
        var ports = _deviceDiscovery.GetComPorts();
        
        ComPorts.Clear();
        foreach (var port in ports)
        {
            ComPorts.Add(port);
        }

        // Reselect if still available
        if (SelectedComPort != null && !ports.Any(p => p.Id == SelectedComPort.Id))
        {
            SelectedComPort = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedComPort == null)
            return;

        try
        {
            ConnectionStatus = "Connecting...";
            var deviceInfo = await _deviceCommunication.OpenConnectionAsync(SelectedComPort.Id);
            
            if (deviceInfo == null)
                throw new InvalidOperationException("Failed to get device information");
            
            ConnectedDevice = deviceInfo;
            FirmwareVersion = $"FW: 0x{deviceInfo.FirmwareVersion:X2}";
            IsConnected = true;
            ConnectionStatus = $"Connected to {SelectedComPort?.DisplayName ?? "device"}";

            // Navigate to Dashboard by default
            NavigateToPage("Dashboard");
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = "Connection timeout";
            System.Windows.MessageBox.Show(
                "Connection timeout. The device is not responding.\n\n" +
                "Please check:\n" +
                "• Device is powered on\n" +
                "• Correct COM port is selected\n" +
                "• No other program is using the port\n" +
                "• Device firmware supports JSON protocol", 
                "Connection Timeout", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        catch (NullReferenceException ex)
        {
            ConnectionStatus = "Protocol error";
            System.Windows.MessageBox.Show(
                $"Device responded but protocol format was unexpected.\n\n" +
                $"Error: {ex.Message}\n\n" +
                $"Location: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}", 
                "Protocol Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        catch (InvalidOperationException ex)
        {
            ConnectionStatus = "Connection failed";
            System.Windows.MessageBox.Show(
                $"{ex.Message}", 
                "Connection Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Connection failed: {ex.Message}";
            System.Windows.MessageBox.Show($"Failed to connect: {ex.Message}\n\nError Type: {ex.GetType().Name}", "Connection Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private bool CanConnect() => !IsConnected && SelectedComPort != null;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        IsConnected = false;
        ConnectionStatus = "Disconnecting...";
        FirmwareVersion = "";
        ConnectedDevice = null;

        try
        {
            await Task.Run(() => _deviceCommunication.CloseConnection());
        }
        catch
        {
            // Best effort close
        }

        ConnectionStatus = "Disconnected";

        // Navigate back to Dashboard (or could navigate to a welcome page)
        NavigateToPage("Dashboard");
    }

    private bool CanDisconnect() => IsConnected;

    [RelayCommand]
    private void NavigateToPage(string page)
    {
        SelectedTab = page;
        
        // Dispose previous Dashboard ViewModel if exists
        _currentDashboardViewModel?.Dispose();
        _currentDashboardViewModel = null;
        
        switch (page)
        {
            case "Dashboard":
                var dashboardPage = new Views.DashboardPage();
                var dashboardViewModel = new DashboardViewModel(_deviceCommunication);
                _currentDashboardViewModel = dashboardViewModel;
                dashboardPage.DataContext = dashboardViewModel;
                _contentFrame.Navigate(dashboardPage);
                
                // Load config data for CAN info
                _ = dashboardViewModel.LoadConfigDataAsync();
                break;
                
            case "Config":
                try
                {
                    var configPage = new Views.ConfigPage();
                    configPage.DataContext = new ConfigViewModel(_deviceCommunication);
                    _contentFrame.Navigate(configPage);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to open Config tab: {ex.Message}", "Navigation Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                break;
                
            case "Firmware":
                try
                {
                    var firmwarePage = new Views.FirmwarePage();
                    firmwarePage.DataContext = new FirmwareViewModel(_deviceDiscovery, _dfuUtil);
                    _contentFrame.Navigate(firmwarePage);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to open Firmware tab: {ex.Message}", "Navigation Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                break;
        }
    }

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = false;
            ConnectionStatus = "Connection lost";
            FirmwareVersion = "";
            System.Windows.MessageBox.Show("Connection to device was lost.", "Connection Lost",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        });
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (!IsConnected)
            {
                return;
            }

            ConnectionStatus = $"Error: {error}";
        });
    }

    partial void OnSelectedComPortChanged(DeviceInfo? value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        ConnectCommand?.NotifyCanExecuteChanged();
        DisconnectCommand?.NotifyCanExecuteChanged();
    }
}
