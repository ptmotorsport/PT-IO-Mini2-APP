using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using IOMini2Tool.Models;
using IOMini2Tool.Services;

namespace IOMini2Tool.ViewModels;

/// <summary>
/// ViewModel for Dashboard page - displays real-time telemetry
/// </summary>
public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly DeviceCommunicationService _communication;
    private bool _isSubscribed;

    [ObservableProperty]
    private ObservableCollection<string> _analogInputs = new();

    [ObservableProperty]
    private ObservableCollection<string> _digitalInputs = new();

    [ObservableProperty]
    private ObservableCollection<string> _digitalOutputs = new();

    [ObservableProperty]
    private string _canSpeed = "N/A";

    [ObservableProperty]
    private string _canMode = "N/A";

    [ObservableProperty]
    private string _canTxRate = "N/A";

    [ObservableProperty]
    private string _canTxBaseId = "N/A";

    [ObservableProperty]
    private string _canRxBaseId = "N/A";

    [ObservableProperty]
    private string _canRxTimeout = "N/A";

    [ObservableProperty]
    private string _canSafeState = "N/A";

    [ObservableProperty]
    private string _canSafeStateColor = "#000000";

    [ObservableProperty]
    private string _canCounts = "N/A";

    public DashboardViewModel(DeviceCommunicationService communication)
    {
        _communication = communication;
        _communication.TelemetryReceived += OnTelemetryReceived;

        // Initialize with placeholder data
        for (int i = 0; i < 8; i++)
        {
            AnalogInputs.Add($"AV{i + 1} - (voltage)");
            DigitalInputs.Add($"DI{i + 1} - STATE - Duty% - Freq Hz");
            DigitalOutputs.Add($"DPO{i + 1} - STATE - Duty% - Freq Hz");
        }

        // Start telemetry if connected
        if (_communication.IsConnected)
        {
            _ = SubscribeToTelemetryAsync();
        }
    }

    private async Task SubscribeToTelemetryAsync()
    {
        if (_isSubscribed) return;

        try
        {
            await _communication.SubscribeAsync(100); // 10 Hz
            _isSubscribed = true;
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Failed to subscribe to telemetry: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    private void OnTelemetryReceived(object? sender, DeviceData data)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateAnalogInputs(data);
            UpdateDigitalInputs(data);
            UpdateDigitalOutputs(data);
            UpdateCanInfo(data);
        });
    }

    private void UpdateAnalogInputs(DeviceData data)
    {
        for (int i = 0; i < 8 && i < data.Analog.Length; i++)
        {
            double voltage = (data.Analog[i] / 16383.0) * 5.0;
            AnalogInputs[i] = $"AV{i + 1} - {voltage:F2}V";
        }
    }

    private void UpdateDigitalInputs(DeviceData data)
    {
        for (int i = 0; i < 8 && i < data.Digital.Length; i++)
        {
            var di = data.Digital[i];
            string state = di.State ? "HIGH" : "LOW";
            string duty = di.Duty ?? "N/A";
            string freq = di.Freq ?? "N/A";
            DigitalInputs[i] = $"DI{i + 1} - {state} - {duty}% - {freq} Hz";
        }
    }

    private void UpdateDigitalOutputs(DeviceData data)
    {
        for (int i = 0; i < 8 && i < data.Outputs.Length; i++)
        {
            var output = data.Outputs[i];
            double duty = double.TryParse(output.Duty, out var d) ? d : 0;
            string state = duty > 0 ? "ACTIVE" : "INACTIVE";
            DigitalOutputs[i] = $"DPO{i + 1} - {state} - {output.Duty}% - {output.Freq} Hz";
        }
    }

    private void UpdateCanInfo(DeviceData data)
    {
        var can = data.Can;
        
        // Only update values that aren't already set from config or set them better from telemetry
        // CanSpeed, TxBaseId, RxBaseId, TxRate come from LoadConfigDataAsync
        // Only update if not already loaded from config (would be numeric, not "N/A")
        if (CanSpeed == "N/A")
        {
            CanSpeed = can.Init ? "Active" : "Inactive";
        }
        
        CanMode = $"Mode {can.Mode}";
        CanRxTimeout = $"{can.RxTimeout} ms";
        CanSafeState = can.SafeState ? "YES" : "NO";
        CanSafeStateColor = can.SafeState ? "#E81123" : "#107C10"; // Red if safe state, green if normal
        CanCounts = $"{can.RxCount} / {can.TxCount}";
    }

    public async Task LoadConfigDataAsync()
    {
        // Optionally load config to fill in CAN details not in telemetry
        if (!_communication.IsConnected) return;

        try
        {
            var config = await _communication.GetConfigAsync();
            CanSpeed = $"{config.CanSpeed} kbps";
            CanTxBaseId = $"0x{config.TxBaseId:X3}";
            CanRxBaseId = $"0x{config.RxBaseId:X3}";
            CanTxRate = $"{config.TxRate} Hz";
        }
        catch
        {
            // Ignore errors, telemetry will continue to work
        }
    }

    public void Dispose()
    {
        _communication.TelemetryReceived -= OnTelemetryReceived;
        
        if (_isSubscribed)
        {
            _isSubscribed = false;
            try
            {
                _ = _communication.UnsubscribeAsync();
            }
            catch
            {
                // Best effort
            }
        }
    }
}
