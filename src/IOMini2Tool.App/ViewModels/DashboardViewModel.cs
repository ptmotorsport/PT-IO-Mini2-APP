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
    private ObservableCollection<string> _outputVoltageInputs = new();

    [ObservableProperty]
    private bool _hasOutputVoltageInputs;

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

        // Size placeholder rows to the connected device's actual channel counts (Mini2: 8/8/8,
        // DTM2: 8 analog / 4 digital in / 4 digital out) instead of assuming 8 everywhere.
        var info = _communication.ConnectedDeviceInfo;
        int analogCount = info?.AnalogChannels > 0 ? info.AnalogChannels : 8;
        int digitalInCount = info?.DigitalIn > 0 ? info.DigitalIn : 8;
        int digitalOutCount = info?.DigitalOut > 0 ? info.DigitalOut : 8;

        // DTM2 only has 4 physical DIs but 8 analog channels - the last 4 analog channels
        // (AV5-8) are the DI1-4 line voltage sense taps, so label them as such. Mini2 has
        // 8 DIs and 8 unrelated analog channels, so no suffix is added there.
        int diSenseOffset = digitalInCount < analogCount ? analogCount - digitalInCount : -1;

        for (int i = 0; i < analogCount; i++)
        {
            AnalogInputs.Add($"{AnalogLabel(i, diSenseOffset)} - (voltage)");
        }
        for (int i = 0; i < digitalInCount; i++)
        {
            DigitalInputs.Add($"DI{i + 1} - STATE - Duty% - Freq Hz");
        }
        for (int i = 0; i < digitalOutCount; i++)
        {
            DigitalOutputs.Add($"DPO{i + 1} - STATE - Duty% - Freq Hz");
        }

        // DTM2-only: DPO1-4 output voltage sense (0-30V, via ADS1115), shown as AV9-12.
        // Firmware reports this via hello's outputVoltageChannels field (absent/0 on older firmware
        // and on Mini2, which has no output voltage sense hardware).
        int outputVoltageCount = info?.OutputVoltageChannels ?? 0;
        HasOutputVoltageInputs = outputVoltageCount > 0;
        for (int i = 0; i < outputVoltageCount; i++)
        {
            OutputVoltageInputs.Add($"AV{analogCount + i + 1} - DPO{i + 1} - (voltage)");
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
            UpdateOutputVoltageInputs(data);
            UpdateDigitalInputs(data);
            UpdateDigitalOutputs(data);
            UpdateCanInfo(data);
        });
    }

    private void UpdateAnalogInputs(DeviceData data)
    {
        int diSenseOffset = DigitalInputs.Count < AnalogInputs.Count ? AnalogInputs.Count - DigitalInputs.Count : -1;
        for (int i = 0; i < AnalogInputs.Count && i < data.Analog.Length; i++)
        {
            double voltage = (data.Analog[i] / 16383.0) * 5.0;
            AnalogInputs[i] = $"{AnalogLabel(i, diSenseOffset)} - {voltage:F2}V";
        }
    }

    private void UpdateOutputVoltageInputs(DeviceData data)
    {
        int analogCount = AnalogInputs.Count;
        for (int i = 0; i < OutputVoltageInputs.Count && i < data.OutputVoltage.Length; i++)
        {
            string label = $"AV{analogCount + i + 1} - DPO{i + 1}";
            if (!data.OutputVoltageReady)
            {
                OutputVoltageInputs[i] = $"{label} - N/A";
                continue;
            }
            double voltage = data.OutputVoltage[i] / 1000.0;
            OutputVoltageInputs[i] = $"{label} - {voltage:F2}V";
        }
    }

    private static string AnalogLabel(int index, int diSenseOffset)
    {
        string label = $"AV{index + 1}";
        if (diSenseOffset >= 0 && index >= diSenseOffset)
        {
            label += $" - DI{index - diSenseOffset + 1}";
        }
        return label;
    }

    private void UpdateDigitalInputs(DeviceData data)
    {
        for (int i = 0; i < DigitalInputs.Count && i < data.Digital.Length; i++)
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
        for (int i = 0; i < DigitalOutputs.Count && i < data.Outputs.Length; i++)
        {
            var output = data.Outputs[i];
            double duty = double.TryParse(output.Duty, out var d) ? d : 0;
            // "duty" is the commanded value before polarity is applied (matches firmware's
            // outputDuty[]); the physical pin only actually drives current according to
            // activeHigh - e.g. duty=0% with ActiveHigh unchecked (active-low) still drives
            // the pin, so ACTIVE/INACTIVE must factor polarity in, not just duty > 0.
            double physicalDuty = output.ActiveHigh ? duty : 100 - duty;
            string state = physicalDuty > 0 ? "ACTIVE" : "INACTIVE";

            // When the app overrides a parameter, show the applied value plus the raw CAN
            // value in brackets (if CAN is actually driving different data) so the user can
            // see what the ECU is sending without it taking effect.
            bool dutyOverridden = (data.Can.DutyOverrideMask & (1 << i)) != 0;
            string dutyText = dutyOverridden && output.CanDuty != output.Duty
                ? $"{output.Duty}% (CAN: {output.CanDuty}%)"
                : $"{output.Duty}%";

            bool freqOverridden = (data.Can.FreqOverrideMask & (1 << i)) != 0;
            string freqText = freqOverridden && output.CanFreq != output.Freq
                ? $"{output.Freq} Hz (CAN: {output.CanFreq} Hz)"
                : $"{output.Freq} Hz";

            DigitalOutputs[i] = $"DPO{i + 1} - {state} - {dutyText} - {freqText}";
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
