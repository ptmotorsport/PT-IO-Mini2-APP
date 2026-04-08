using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IOMini2Tool.Models;

namespace IOMini2Tool.Services;

/// <summary>
/// Handles serial communication with PT-IO-Mini2 device using JSON protocol
/// </summary>
public sealed class DeviceCommunicationService : IDisposable
{
    private SerialPort? _serialPort;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private bool _isSubscribed;
    private bool _isClosing;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private TaskCompletionSource<JsonNode?>? _pendingResponse;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event EventHandler<DeviceData>? TelemetryReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? ConnectionLost;
    
    public bool IsConnected => _serialPort?.IsOpen ?? false;

    /// <summary>
    /// Opens connection to device and retrieves hello message
    /// </summary>
    public async Task<DeviceInfo> OpenConnectionAsync(string portName, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            throw new InvalidOperationException("Already connected");

        try
        {
            _isClosing = false;
            _serialPort = new SerialPort(portName, 115200)
            {
                Encoding = Encoding.UTF8,
                NewLine = "\n",
                ReadTimeout = 2000,
                WriteTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true
            };
            
            _serialPort.Open();
            
            // Clear any buffered data
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
            
            // Start background read loop
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);
            
            // Delay to let device initialize and read loop start
            await Task.Delay(500, cancellationToken);
            
            // Request hello message with longer timeout
            var hello = await GetHelloAsync(cancellationToken);
            
            return hello;
        }
        catch
        {
            CloseConnection();
            throw;
        }
    }

    /// <summary>
    /// Closes the serial connection
    /// </summary>
    public void CloseConnection()
    {
        _isClosing = true;
        _isSubscribed = false;

        try
        {
            _pendingResponse?.TrySetCanceled();
        }
        catch { /* Best effort */ }

        try
        {
            _readCts?.Cancel();
        }
        catch { /* Best effort */ }

        try
        {
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.Close();
            }
        }
        catch { /* Best effort */ }

        _serialPort?.Dispose();
        _serialPort = null;

        _readCts?.Dispose();
        _readCts = null;
        _readTask = null;
    }

    /// <summary>
    /// Requests device information
    /// </summary>
    public async Task<DeviceInfo> GetHelloAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync("{\"cmd\":\"getHello\"}", 3000, cancellationToken);
        
        if (response == null)
            throw new InvalidOperationException("No response received from device");
        
        // Debug: Log the raw response
        System.Diagnostics.Debug.WriteLine($"Hello Response: {response.ToJsonString()}");
        
        var type = response["type"]?.GetValue<string>();
        if (type != "hello")
            throw new InvalidOperationException($"Expected 'hello' message, got '{type}'. Full response: {response.ToJsonString()}");
        
        try
        {
            string[]? capabilities = null;
            try
            {
                capabilities = response["capabilities"]?.Deserialize<string[]>(JsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to deserialize capabilities: {ex.Message}");
            }
            
            return new DeviceInfo
            {
                ProtocolVersion = response["version"]?.GetValue<int>() ?? 0,
                FirmwareVersion = response["fw"]?.GetValue<int>() ?? 0,
                CanMode = response["canMode"]?.GetValue<int>() ?? 0,
                CanModeName = response["canModeName"]?.GetValue<string>() ?? string.Empty,
                Capabilities = capabilities ?? Array.Empty<string>(),
                AnalogChannels = response["analogChannels"]?.GetValue<int>() ?? 0,
                DigitalIn = response["digitalIn"]?.GetValue<int>() ?? 0,
                DigitalOut = response["digitalOut"]?.GetValue<int>() ?? 0
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse hello message: {ex.Message}. Response: {response.ToJsonString()}", ex);
        }
    }

    /// <summary>
    /// Subscribe to telemetry stream
    /// </summary>
    public async Task SubscribeAsync(int intervalMs = 100, CancellationToken cancellationToken = default)
    {
        var cmd = $"{{\"cmd\":\"subscribe\",\"stream\":\"telemetry\",\"interval\":{intervalMs}}}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response == null)
            throw new InvalidOperationException("No response to subscribe command");
        
        // Check if we got a response type message    
        var type = response["type"]?.GetValue<string>();
        if (type == "error")
        {
            var msg = response["msg"]?.GetValue<string>() ?? "Unknown error";
            throw new InvalidOperationException($"Subscribe failed: {msg}");
        }
            
        var status = response["status"]?.GetValue<string>();
        if (status != "ok")
            throw new InvalidOperationException($"Subscribe failed: status='{status}' (type='{type}')");
        
        _isSubscribed = true;
    }

    /// <summary>
    /// Unsubscribe from telemetry stream
    /// </summary>
    public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync("{\"cmd\":\"unsubscribe\"}", 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException("Failed to unsubscribe");
        
        _isSubscribed = false;
    }

    /// <summary>
    /// Get current device status (one-shot telemetry)
    /// </summary>
    public async Task<DeviceData> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync("{\"cmd\":\"getStatus\"}", 2000, cancellationToken);
        
        if (response == null || response["type"]?.GetValue<string>() != "telemetry")
            throw new InvalidOperationException("Did not receive telemetry");
        
        return ParseTelemetry(response);
    }

    /// <summary>
    /// Get device configuration
    /// </summary>
    public async Task<DeviceConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync("{\"cmd\":\"getConfig\"}", 2000, cancellationToken);
        
        if (response == null || response["type"]?.GetValue<string>() != "config")
            throw new InvalidOperationException("Did not receive config");
        
        return new DeviceConfig
        {
            CanSpeed = response["canSpeed"]?.GetValue<int>() ?? 0,
            TxBaseId = response["txBaseId"]?.GetValue<int>() ?? 0,
            RxBaseId = response["rxBaseId"]?.GetValue<int>() ?? 0,
            TxRate = response["txRate"]?.GetValue<int>() ?? 0,
            RxTimeout = response["rxTimeout"]?.GetValue<int>() ?? 0,
            CanMode = response["canMode"]?.GetValue<int>() ?? 0,
            SafeMask = response["safeMask"]?.GetValue<byte>() ?? 0,
            ActiveMask = response["activeMask"]?.GetValue<byte>() ?? 0,
            InputPullupMask = response["inputPullupMask"]?.GetValue<byte>() ?? 0,
            DiDebounceMs = response["diDebounceMs"]?.GetValue<int>() ?? 0,
            OutFreq = response["outFreq"]?.Deserialize<int[]>(JsonOptions) ?? new int[8]
        };
    }

    /// <summary>
    /// Set output duty cycle (automatically enables serial override for channel)
    /// </summary>
    public async Task SetOutputAsync(int channel, int duty, CancellationToken cancellationToken = default)
    {
        if (channel < 1 || channel > 8)
            throw new ArgumentException("Channel must be 1-8", nameof(channel));

        if (duty < 0 || duty > 100)
            throw new ArgumentException("Duty must be 0-100", nameof(duty));
        
        var cmd = $"{{\"cmd\":\"setOutput\",\"ch\":{channel},\"duty\":{duty}}}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException($"Failed to set output: {response?["msg"]?.GetValue<string>()}");
    }

    /// <summary>
    /// Set output frequency (paired channels share frequency)
    /// </summary>
    public async Task SetOutputFreqAsync(int channel, int freq, CancellationToken cancellationToken = default)
    {
        if (channel < 1 || channel > 8)
            throw new ArgumentException("Channel must be 1-8", nameof(channel));
        
        if (freq <= 0)
            throw new ArgumentException("Frequency must be > 0", nameof(freq));
        
        var cmd = $"{{\"cmd\":\"setOutputFreq\",\"ch\":{channel},\"freq\":{freq}}}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException($"Failed to set frequency: {response?["msg"]?.GetValue<string>()}");
    }

    /// <summary>
    /// Set safe state level for channel
    /// </summary>
    public async Task SetSafeAsync(int channel, bool value, CancellationToken cancellationToken = default)
    {
        if (channel < 1 || channel > 8)
            throw new ArgumentException("Channel must be 1-8", nameof(channel));
        
        var cmd = $"{{\"cmd\":\"setSafe\",\"ch\":{channel},\"value\":{(value ? "true" : "false")}}}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException($"Failed to set safe state: {response?["msg"]?.GetValue<string>()}");
    }

    /// <summary>
    /// Set active polarity for channel
    /// </summary>
    public async Task SetActiveAsync(int channel, bool activeHigh, CancellationToken cancellationToken = default)
    {
        if (channel < 1 || channel > 8)
            throw new ArgumentException("Channel must be 1-8", nameof(channel));
        
        var cmd = $"{{\"cmd\":\"setActive\",\"ch\":{channel},\"activeHigh\":{(activeHigh ? "true" : "false")}}}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException($"Failed to set active polarity: {response?["msg"]?.GetValue<string>()}");
    }

    /// <summary>
    /// Set input pullup enable for channel
    /// </summary>
    public async Task SetInputPullupAsync(int channel, bool enabled, CancellationToken cancellationToken = default)
    {
        if (channel < 1 || channel > 8)
            throw new ArgumentException("Channel must be 1-8", nameof(channel));
        
        var cmd = $"{{\"cmd\":\"setInputPullup\",\"ch\":{channel},\"enabled\":{(enabled ? "true" : "false")}}}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException($"Failed to set input pullup: {response?["msg"]?.GetValue<string>()}");
    }

    /// <summary>
    /// Set debounce time for all digital inputs
    /// </summary>
    public async Task SetDebounceAsync(int ms, CancellationToken cancellationToken = default)
    {
        if (ms < 0 || ms > 100)
            throw new ArgumentException("Debounce must be 0-100 ms", nameof(ms));
        
        var cmd = $"{{\"cmd\":\"setDebounce\",\"ms\":{ms}}}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException($"Failed to set debounce: {response?["msg"]?.GetValue<string>()}");
    }

    /// <summary>
    /// Set CAN configuration
    /// </summary>
    public async Task SetCanConfigAsync(int? canSpeed = null, int? txBaseId = null, int? rxBaseId = null, 
        int? txRate = null, int? rxTimeout = null, int? canMode = null, CancellationToken cancellationToken = default)
    {
        var parts = new List<string> { "\"cmd\":\"setCanConfig\"" };
        
        if (canSpeed.HasValue) parts.Add($"\"canSpeed\":{canSpeed.Value}");
        if (txBaseId.HasValue) parts.Add($"\"txBaseId\":{txBaseId.Value}");
        if (rxBaseId.HasValue) parts.Add($"\"rxBaseId\":{rxBaseId.Value}");
        if (txRate.HasValue) parts.Add($"\"txRate\":{txRate.Value}");
        if (rxTimeout.HasValue) parts.Add($"\"rxTimeout\":{rxTimeout.Value}");
        if (canMode.HasValue) parts.Add($"\"canMode\":{canMode.Value}");
        
        var cmd = "{" + string.Join(",", parts) + "}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException($"Failed to set CAN config: {response?["msg"]?.GetValue<string>()}");
    }

    /// <summary>
    /// Reset all configuration to defaults
    /// </summary>
    public async Task ResetDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync("{\"cmd\":\"resetDefaults\"}", 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException("Failed to reset defaults");
    }

    /// <summary>
    /// Set serial override for channel
    /// </summary>
    public async Task SetSerialOverrideAsync(int channel, bool enabled, CancellationToken cancellationToken = default)
    {
        if (channel < 1 || channel > 8)
            throw new ArgumentException("Channel must be 1-8", nameof(channel));
        
        var cmd = $"{{\"cmd\":\"setSerialOverride\",\"ch\":{channel},\"enabled\":{(enabled ? "true" : "false")}}}";
        var response = await SendCommandAsync(cmd, 2000, cancellationToken);
        
        if (response?["status"]?.GetValue<string>() != "ok")
            throw new InvalidOperationException($"Failed to set serial override: {response?["msg"]?.GetValue<string>()}");
    }

    private async Task<JsonNode?> SendCommandAsync(string command, int timeoutMs, CancellationToken cancellationToken)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected");
        
        await _commandLock.WaitAsync(cancellationToken);
        
        try
        {
            _pendingResponse = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingResponse = _pendingResponse;
            
            // Debug: Log what we're sending
            System.Diagnostics.Debug.WriteLine($"TX: {command}");
            
            _serialPort!.WriteLine(command);
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            
            // Register cancellation
            using var registration = cts.Token.Register(() => pendingResponse.TrySetCanceled());
            
            return await pendingResponse.Task;
        }
        finally
        {
            _pendingResponse = null;
            _commandLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                try
                {
                    var line = await Task.Run(() => _serialPort!.ReadLine(), cancellationToken);
                    
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    
                    // Debug: Log raw data
                    System.Diagnostics.Debug.WriteLine($"RX: {line}");
                    
                    var json = JsonNode.Parse(line);
                    if (json != null)
                    {
                        ProcessMessage(json);
                    }
                }
                catch (TimeoutException)
                {
                    // Normal - just means no data received
                    continue;
                }
                catch (JsonException ex)
                {
                    ErrorOccurred?.Invoke(this, $"JSON parse error: {ex.Message}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!_isClosing && IsConnected)
                    {
                        ErrorOccurred?.Invoke(this, $"Read error: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception)
        {
            if (!_isClosing && !cancellationToken.IsCancellationRequested)
            {
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void ProcessMessage(JsonNode json)
    {
        var type = json["type"]?.GetValue<string>();
        
        if (type == "telemetry")
        {
            try
            {
                var telemetry = ParseTelemetry(json);
                TelemetryReceived?.Invoke(this, telemetry);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to parse telemetry: {ex.Message}");
            }
        }
        else if (type == "error")
        {
            var msg = json["msg"]?.GetValue<string>() ?? "Unknown error";
            _pendingResponse?.TrySetResult(json);
            ErrorOccurred?.Invoke(this, msg);
        }
        else
        {
            // Any other message type (hello, config, response, status, etc.) is treated as a command response
            if (_pendingResponse != null)
            {
                _pendingResponse.TrySetResult(json);
            }
        }
    }

    private DeviceData ParseTelemetry(JsonNode json)
    {
        var analog = json["analog"]?.Deserialize<int[]>(JsonOptions) ?? new int[8];
        
        var digitalArray = json["digital"]?.AsArray();
        var digital = new DigitalInputState[8];
        for (int i = 0; i < 8 && i < (digitalArray?.Count ?? 0); i++)
        {
            var item = digitalArray![i];
            digital[i] = new DigitalInputState
            {
                State = item?["state"]?.GetValue<bool>() ?? false,
                Freq = GetStringOrNull(item?["freq"]),
                Duty = GetStringOrNull(item?["duty"])
            };
        }
        
        var outputsArray = json["outputs"]?.AsArray();
        var outputs = new OutputState[8];
        for (int i = 0; i < 8 && i < (outputsArray?.Count ?? 0); i++)
        {
            var item = outputsArray![i];
            outputs[i] = new OutputState
            {
                Duty = GetStringOrNull(item?["duty"]) ?? "0.0",
                Freq = item?["freq"]?.GetValue<int>() ?? 0,
                Safe = item?["safe"]?.GetValue<bool>() ?? false,
                ActiveHigh = item?["activeHigh"]?.GetValue<bool>() ?? true
            };
        }
        
        var canNode = json["can"];
        var can = new CanStatus
        {
            Init = canNode?["init"]?.GetValue<bool>() ?? false,
            SafeState = canNode?["safeState"]?.GetValue<bool>() ?? false,
            RxCount = canNode?["rxCount"]?.GetValue<int>() ?? 0,
            TxCount = canNode?["txCount"]?.GetValue<int>() ?? 0,
            TxFail = canNode?["txFail"]?.GetValue<int>() ?? 0,
            Mode = canNode?["mode"]?.GetValue<int>() ?? 0,
            LastRxMs = canNode?["lastRxMs"]?.GetValue<long>() ?? 0,
            RxTimeout = canNode?["rxTimeout"]?.GetValue<int>() ?? 0
        };
        
        return new DeviceData
        {
            Timestamp = json["timestamp"]?.GetValue<long>() ?? 0,
            Analog = analog,
            Digital = digital,
            Outputs = outputs,
            Can = can
        };
    }

    /// <summary>
    /// Helper to get string value from JSON node, handling null and number types
    /// </summary>
    private static string? GetStringOrNull(JsonNode? node)
    {
        if (node == null) return null;
        
        // Try to get as string first
        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            // If it's a number, convert it to string
            try
            {
                var num = node.GetValue<double>();
                return num.ToString("F2");
            }
            catch
            {
                return null;
            }
        }
    }

    public void Dispose()
    {
        CloseConnection();
        _commandLock.Dispose();
    }
}
