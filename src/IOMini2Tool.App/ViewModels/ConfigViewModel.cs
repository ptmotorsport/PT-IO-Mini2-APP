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
public partial class ConfigViewModel : ViewModelBase
{
    private readonly DeviceCommunicationService _communication;
    private bool _isLoading = false; // Prevent auto-save during config load

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private ObservableCollection<InputPullupItem> _inputPullups = new();

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

        // Initialize input pullups
        for (int i = 0; i < 8; i++)
        {
            InputPullups.Add(new InputPullupItem { Channel = i + 1, Label = $"DI{i + 1} - [ ]", IsEnabled = false });
        }

        // Initialize output configs
        for (int i = 0; i < 8; i++)
        {
            OutputConfigs.Add(new OutputConfigItem 
            { 
                Channel = i + 1, 
                Label = $"DPO{i + 1}",
                SafeHigh = false,
                ActiveHigh = true,
                SerialOverride = false,
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
        }
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
                for (int i = 0; i < 8; i++)
                {
                    bool enabled = (config.InputPullupMask & (1 << i)) != 0;
                    InputPullups[i].IsEnabled = enabled;
                    InputPullups[i].Label = $"DI{i + 1} - [{(enabled ? "x" : " ")}]";
                }

                // Load output configs
                for (int i = 0; i < 8; i++)
                {
                    bool safeHigh = (config.SafeMask & (1 << i)) != 0;
                    bool activeHigh = (config.ActiveMask & (1 << i)) != 0;
                    bool serialOverride = (config.SerialOverrideMask & (1 << i)) != 0;
                    OutputConfigs[i].SafeHigh = safeHigh;
                    OutputConfigs[i].ActiveHigh = activeHigh;
                    OutputConfigs[i].SerialOverride = serialOverride;
                    OutputConfigs[i].Frequency = config.OutFreq[i];
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
            await _communication.SetSerialOverrideAsync(item.Channel, item.SerialOverride);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set serial override: {ex.Message}", "Error",
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
    private int _frequency;

    [ObservableProperty]
    private int _duty;
}

public class CanModeItem
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
}
