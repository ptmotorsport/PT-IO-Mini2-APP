using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IOMini2Tool.Models;
using IOMini2Tool.Services;

namespace IOMini2Tool.ViewModels;

/// <summary>
/// ViewModel for Config page - handles device configuration with auto-save
/// </summary>
public partial class ConfigViewModel : ViewModelBase, IDisposable
{
    private readonly DeviceCommunicationService _communication;
    private bool _isLoading = false; // Prevent auto-save during config load

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _canSafeState = "N/A";

    [ObservableProperty]
    private string _canSafeStateColor = "#000000";

    [ObservableProperty]
    private ObservableCollection<InputPullupItem> _inputPullups = new();

    [ObservableProperty]
    private ObservableCollection<InputPullupItem> _analogPullups = new();

    [ObservableProperty]
    private bool _hasAnalogPullups;

    [ObservableProperty]
    private ObservableCollection<OutputConfigItem> _outputConfigs = new();

    [ObservableProperty]
    private int _debounceMs = 20;

    [ObservableProperty]
    private bool _canSpeed1000 = true;

    [ObservableProperty]
    private bool _canSpeed500;

    [ObservableProperty]
    private bool _canSpeed250;

    [ObservableProperty]
    private bool _canSpeed125;

    [ObservableProperty]
    private string _canTxBaseId = "700";

    [ObservableProperty]
    private string _canRxBaseId = "640";

    [ObservableProperty]
    private int _canTxRate = 10;

    [ObservableProperty]
    private int _canRxTimeout = 2000;

    [ObservableProperty]
    private ObservableCollection<CanModeItem> _canModes = new();

    [ObservableProperty]
    private int _selectedCanModeIndex;

    public ConfigViewModel(DeviceCommunicationService communication)
    {
        _communication = communication;
        _isConnected = _communication.IsConnected;

        // Size config rows to the connected device's actual channel counts (Mini2: 8/8, DTM2: 4/4)
        var info = _communication.ConnectedDeviceInfo;
        int digitalInCount = info?.DigitalIn > 0 ? info.DigitalIn : 8;
        int digitalOutCount = info?.DigitalOut > 0 ? info.DigitalOut : 8;

        // Initialize input pullups
        for (int i = 0; i < digitalInCount; i++)
        {
            InputPullups.Add(new InputPullupItem { Channel = i + 1, Label = $"DI{i + 1} - [ ]", IsEnabled = false });
        }

        // Initialize analog input (AV1-4) external 1k pullups - DTM2 only. Gated on
        // OutputVoltageChannels (a DTM2-only hardware capability) rather than hardcoding 4,
        // so older DTM2 firmware that predates this field just hides the section.
        int analogPullupCount = (info?.OutputVoltageChannels ?? 0) > 0 ? 4 : 0;
        HasAnalogPullups = analogPullupCount > 0;
        for (int i = 0; i < analogPullupCount; i++)
        {
            AnalogPullups.Add(new InputPullupItem { Channel = i + 1, Label = $"AV{i + 1} - [ ]", IsEnabled = false });
        }

        // Initialize output configs
        for (int i = 0; i < digitalOutCount; i++)
        {
            OutputConfigs.Add(new OutputConfigItem 
            { 
                Channel = i + 1, 
                Label = $"DPO{i + 1}",
                SafeHigh = false,
                ActiveHigh = true,
                SerialOverride = false,
                FreqOverride = false,
                SafeOverride = false,
                ActiveOverride = false,
                Frequency = 300,
                Duty = 0
            });
        }

        // Initialize CAN modes (0-15)
        var canModeNames = new[]
        {
            "PT_Default1",
            "Haltech IO12A",
            "Haltech IO12B",
            "Haltech IO12A&B",
            "Haltech IO16A",
            "Haltech IO16B",
            "ECU Master CANSWB v3",
            "Motec E888",
            "Emtron",
            "reserved",
            "reserved",
            "reserved",
            "reserved",
            "reserved",
            "reserved",
            "reserved"
        };

        for (int i = 0; i < canModeNames.Length; i++)
        {
            CanModes.Add(new CanModeItem { Index = i, Name = $"{i} - {canModeNames[i]}" });
        }

        // Load config if connected - defer to avoid blocking
        if (_isConnected)
        {
            _ = LoadConfigAsync();
            _ = _communication.SubscribeAsync(100);
        }

        // Live DPO state (ACTIVE/INACTIVE, duty/freq, CAN safe-state) so the user can see
        // the effect of a config change without switching to the Dashboard tab.
        _communication.TelemetryReceived += OnTelemetryReceived;
    }

    private void OnTelemetryReceived(object? sender, DeviceData data)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            CanSafeState = data.Can.SafeState ? "YES" : "NO";
            CanSafeStateColor = data.Can.SafeState ? "#E81123" : "#107C10";

            for (int i = 0; i < OutputConfigs.Count && i < data.Outputs.Length; i++)
            {
                var output = data.Outputs[i];
                double duty = double.TryParse(output.Duty, out var d) ? d : 0;
                // Mirrors Dashboard's ACTIVE/INACTIVE logic: "duty" is pre-polarity, so the
                // physical on/off state also depends on ActiveHigh.
                double physicalDuty = output.ActiveHigh ? duty : 100 - duty;
                OutputConfigs[i].LiveStateText = physicalDuty > 0 ? "ACTIVE" : "INACTIVE";

                bool dutyOverridden = (data.Can.DutyOverrideMask & (1 << i)) != 0;
                OutputConfigs[i].LiveDutyText = dutyOverridden && output.CanDuty != output.Duty
                    ? $"{output.Duty}% (CAN: {output.CanDuty}%)"
                    : $"{output.Duty}%";

                bool freqOverridden = (data.Can.FreqOverrideMask & (1 << i)) != 0;
                OutputConfigs[i].LiveFreqText = freqOverridden && output.CanFreq != output.Freq
                    ? $"{output.Freq} Hz (CAN: {output.CanFreq} Hz)"
                    : $"{output.Freq} Hz";
            }
        });
    }

    public void Dispose()
    {
        _communication.TelemetryReceived -= OnTelemetryReceived;
    }

    private async Task LoadConfigAsync()
    {
        _isLoading = true;
        try
        {
            var config = await _communication.GetConfigAsync();

            // Update UI properties on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Load CAN speed
                CanSpeed1000 = config.CanSpeed == 1000;
                CanSpeed500 = config.CanSpeed == 500;
                CanSpeed250 = config.CanSpeed == 250;
                CanSpeed125 = config.CanSpeed == 125;

                // Load CAN IDs and settings
                CanTxBaseId = config.TxBaseId.ToString("X");
                CanRxBaseId = config.RxBaseId.ToString("X");
                CanTxRate = config.TxRate;
                CanRxTimeout = config.RxTimeout;
                SelectedCanModeIndex = config.CanMode;

                // Load debounce
                DebounceMs = config.DiDebounceMs;

                // Load input pullups
                for (int i = 0; i < InputPullups.Count; i++)
                {
                    bool enabled = (config.InputPullupMask & (1 << i)) != 0;
                    InputPullups[i].IsEnabled = enabled;
                    InputPullups[i].Label = $"DI{i + 1} - [{(enabled ? "x" : " ")}]";
                }

                // Load analog pullups (DTM2 only)
                for (int i = 0; i < AnalogPullups.Count; i++)
                {
                    bool enabled = (config.AnalogPullupMask & (1 << i)) != 0;
                    AnalogPullups[i].IsEnabled = enabled;
                    AnalogPullups[i].Label = $"AV{i + 1} - [{(enabled ? "x" : " ")}]";
                }

                // Load output configs
                for (int i = 0; i < OutputConfigs.Count; i++)
                {
                    bool safeHigh = (config.SafeMask & (1 << i)) != 0;
                    bool activeHigh = (config.ActiveMask & (1 << i)) != 0;
                    bool dutyOverride = (config.DutyOverrideMask & (1 << i)) != 0;
                    bool freqOverride = (config.FreqOverrideMask & (1 << i)) != 0;
                    bool safeOverride = (config.SafeOverrideMask & (1 << i)) != 0;
                    bool activeOverride = (config.ActiveOverrideMask & (1 << i)) != 0;
                    OutputConfigs[i].SafeHigh = safeHigh;
                    OutputConfigs[i].ActiveHigh = activeHigh;
                    OutputConfigs[i].SerialOverride = dutyOverride;
                    OutputConfigs[i].FreqOverride = freqOverride;
                    OutputConfigs[i].SafeOverride = safeOverride;
                    OutputConfigs[i].ActiveOverride = activeOverride;
                    if (i < config.OutFreq.Length)
                    {
                        OutputConfigs[i].Frequency = config.OutFreq[i];
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during disconnect/navigation
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Failed to load configuration: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleInputPullup(InputPullupItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetInputPullupAsync(item.Channel, item.IsEnabled);
            item.Label = $"DI{item.Channel} - [{(item.IsEnabled ? "x" : " ")}]";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set input pullup: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ToggleAnalogPullup(InputPullupItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetAnalogPullupAsync(item.Channel, item.IsEnabled);
            item.Label = $"AV{item.Channel} - [{(item.IsEnabled ? "x" : " ")}]";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set analog pullup: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OutputSafeChanged(OutputConfigItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetSafeAsync(item.Channel, item.SafeHigh);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set safe state: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OutputActiveChanged(OutputConfigItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetActiveAsync(item.Channel, item.ActiveHigh);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set active polarity: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OutputOverrideChanged(OutputConfigItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetOverrideAsync("duty", item.Channel, item.SerialOverride);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set duty override: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OutputFreqOverrideChanged(OutputConfigItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetOverrideAsync("freq", item.Channel, item.FreqOverride);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set frequency override: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OutputSafeOverrideChanged(OutputConfigItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetOverrideAsync("safe", item.Channel, item.SafeOverride);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set safe-state override: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OutputActiveOverrideChanged(OutputConfigItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetOverrideAsync("active", item.Channel, item.ActiveOverride);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set active-polarity override: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OutputFrequencyChanged(OutputConfigItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetOutputFreqAsync(item.Channel, item.Frequency);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set output frequency: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OutputDutyChanged(OutputConfigItem item)
    {
        if (!IsConnected) return;

        try
        {
            await _communication.SetOutputAsync(item.Channel, item.Duty);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set output duty: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task CanSpeedChanged(string speed)
    {
        if (!IsConnected) return;

        try
        {
            int speedValue = int.Parse(speed);
            await _communication.SetCanConfigAsync(canSpeed: speedValue);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set CAN speed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ResetDefaults()
    {
        var result = MessageBox.Show("Reset all configuration to factory defaults?", "Confirm Reset",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _communication.ResetDefaultsAsync();
            await LoadConfigAsync(); // Reload to show new values
            MessageBox.Show("Configuration reset to defaults", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to reset defaults: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    partial void OnDebounceMsChanged(int value)
    {
        if (_isLoading || !_isConnected) return;
        _ = SaveDebounceAsync();
    }

    private async Task SaveDebounceAsync()
    {
        try
        {
            await _communication.SetDebounceAsync(DebounceMs);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set debounce: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    partial void OnCanTxBaseIdChanged(string value)
    {
        if (_isLoading || !_isConnected) return;
        _ = SaveCanBaseIdAsync();
    }

    partial void OnCanRxBaseIdChanged(string value)
    {
        if (_isLoading || !_isConnected) return;
        _ = SaveCanBaseIdAsync();
    }

    partial void OnCanTxRateChanged(int value)
    {
        if (_isLoading || !_isConnected) return;
        _ = SaveCanTxRateAsync();
    }

    partial void OnCanRxTimeoutChanged(int value)
    {
        if (_isLoading || !_isConnected) return;
        _ = SaveCanRxTimeoutAsync();
    }

    partial void OnSelectedCanModeIndexChanged(int value)
    {
        if (_isLoading || !_isConnected) return;
        _ = SaveCanModeAsync();
    }

    private async Task SaveCanBaseIdAsync()
    {
        try
        {
            int txBaseId = Convert.ToInt32(CanTxBaseId, 16);
            int rxBaseId = Convert.ToInt32(CanRxBaseId, 16);
            await _communication.SetCanConfigAsync(txBaseId: txBaseId, rxBaseId: rxBaseId);
        }
        catch
        {
            // Invalid hex value, ignore
        }
    }

    private async Task SaveCanTxRateAsync()
    {
        try
        {
            await _communication.SetCanConfigAsync(txRate: CanTxRate);
        }
        catch { }
    }

    private async Task SaveCanRxTimeoutAsync()
    {
        try
        {
            await _communication.SetCanConfigAsync(rxTimeout: CanRxTimeout);
        }
        catch { }
    }

    private async Task SaveCanModeAsync()
    {
        try
        {
            await _communication.SetCanConfigAsync(canMode: SelectedCanModeIndex);
        }
        catch { }
    }
}

public partial class InputPullupItem : ObservableObject
{
    public int Channel { get; set; }

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;
}

public partial class OutputConfigItem : ObservableObject
{
    public int Channel { get; set; }

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private bool _safeHigh;

    [ObservableProperty]
    private bool _activeHigh;

    [ObservableProperty]
    private bool _serialOverride;

    [ObservableProperty]
    private bool _freqOverride;

    [ObservableProperty]
    private bool _safeOverride;

    [ObservableProperty]
    private bool _activeOverride;

    [ObservableProperty]
    private int _frequency;

    [ObservableProperty]
    private int _duty;

    // Live telemetry display only - not editable, not sent to the device.
    [ObservableProperty]
    private string _liveStateText = "-";

    [ObservableProperty]
    private string _liveDutyText = "-";

    [ObservableProperty]
    private string _liveFreqText = "-";
}

public class CanModeItem
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
}
