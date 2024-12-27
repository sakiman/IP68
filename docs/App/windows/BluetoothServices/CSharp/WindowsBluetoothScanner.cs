using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using System.Runtime.InteropServices.WindowsRuntime;

namespace BluetoothServices;

public class WindowsBluetoothScanner : IDisposable
{
    private readonly string _targetDeviceAddress;
    private readonly BluetoothLEAdvertisementWatcher _bleWatcher;
    private DeviceWatcher? _classicWatcher;
    private bool _disposed;
    private TaskCompletionSource<bool>? _scanTaskSource;
    private int _deviceCount = 0;
    private const int MAX_DEVICES = 50;
    private readonly HashSet<string> _processedDevices = new();
    private bool _isStopRequested = false;
    private string? _foundIpadBaseAddress = null;

    public event Action<string>? LogMessage;

    public WindowsBluetoothScanner(string targetAddress)
    {
        _targetDeviceAddress = targetAddress;
        _bleWatcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            SignalStrengthFilter = 
            { 
                SamplingInterval = TimeSpan.FromMilliseconds(50),
                InRangeThresholdInDBm = -100,
                OutOfRangeThresholdInDBm = -100
            }
        };

        // 初始化傳統藍牙掃描器
        string aqsFilter = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";
        string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.SignalStrength" };
        
        _classicWatcher = DeviceInformation.CreateWatcher(
            aqsFilter,
            requestedProperties,
            DeviceInformationKind.AssociationEndpoint);

        _bleWatcher.Received += OnAdvertisementReceived;
        _bleWatcher.Stopped += OnWatcherStopped;
        
        _classicWatcher.Added += OnClassicDeviceAdded;
        _classicWatcher.Updated += OnClassicDeviceUpdated;
        _classicWatcher.Removed += OnClassicDeviceRemoved;
        _classicWatcher.EnumerationCompleted += OnClassicEnumerationCompleted;
        _classicWatcher.Stopped += OnClassicWatcherStopped;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(message);
    }

    private string UlongToMacAddress(ulong address)
    {
        byte[] bytes = BitConverter.GetBytes(address);
        Array.Reverse(bytes);
        return string.Join(":", bytes.Skip(2).Take(6).Select(b => b.ToString("X2")));
    }

    private string GetBaseAddress(string address)
    {
        return string.Join(":", address.Split(':').Take(4).Select(s => s.ToUpper()));
    }

    private (string hostAddress, string deviceAddress) ParseBluetoothDeviceId(string id)
    {
        try
        {
            string cleanId = id.Replace("BluetoothLE#BluetoothLE", "")
                             .Replace("Bluetooth#Bluetooth", "");
            
            var parts = cleanId.Split('-');
            if (parts.Length == 2)
            {
                // 將地址轉換為大寫
                return (parts[0].Trim().ToUpper(), parts[1].Trim().ToUpper());
            }
        }
        catch { }
        return ("未知", "未知");
    }

    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            Log("掃描器已被釋放");
            return;
        }

        try
        {
            Log("開始搜索...");
            _deviceCount = 0;
            _processedDevices.Clear();
            _isStopRequested = false;
            _foundIpadBaseAddress = null;
            _scanTaskSource = new TaskCompletionSource<bool>();

            using var registration = cancellationToken.Register(() =>
            {
                StopScanning("掃描被取消");
            });

            _bleWatcher.Start();
            _classicWatcher?.Start();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 掃描超時的時間，預設為 10 秒
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await using var registration2 = linkedCts.Token.Register(() => StopScanning("掃描超時"));
                await _scanTaskSource.Task;
            }
            catch (OperationCanceledException)
            {
                Log("掃描被取消");
                throw;
            }
        }
        finally
        {
            StopScanning("掃描結束");
        }
    }

    private void OnClassicDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        if (_isStopRequested) return;

        try
        {
            string deviceId = deviceInfo.Id;
            if (!_processedDevices.Add(deviceId)) return;

            // 如果沒有名稱或名稱以 Bluetooth 開頭，則不顯示
            if (string.IsNullOrEmpty(deviceInfo.Name) || 
                deviceInfo.Name.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _deviceCount++;

            var deviceInfoBuilder = new System.Text.StringBuilder();
            deviceInfoBuilder.AppendLine("發現新設備 (BR/EDR):");
            
            // 先獲取廣播地址
            string broadcastAddress = "";
            if (deviceInfo.Properties.ContainsKey("System.Devices.Aep.DeviceAddress"))
            {
                broadcastAddress = deviceInfo.Properties["System.Devices.Aep.DeviceAddress"].ToString() ?? "";
            }
            
            deviceInfoBuilder.AppendLine($"  廣播地址: {broadcastAddress.ToUpper()}");
            deviceInfoBuilder.AppendLine($"  地址類型: Public - BR/EDR 只使用 Public 地址（固定的 MAC 地址）");
            
            // 獲取 RSSI
            if (deviceInfo.Properties.ContainsKey("System.Devices.Aep.SignalStrength"))
            {
                var rssi = deviceInfo.Properties["System.Devices.Aep.SignalStrength"];
                deviceInfoBuilder.AppendLine($"  RSSI: {rssi}dBm");
            }

            deviceInfoBuilder.AppendLine($"  廣播類型: BR/EDR 沒有廣播類型的概念，它使用不同的發現機制（Inquiry/Page）");
            
            deviceInfoBuilder.AppendLine($"  藍牙類型: BR/EDR");
            deviceInfoBuilder.AppendLine($"  設備名稱: {deviceInfo.Name}");
            
            // 身份信息
            var (hostAddress, deviceAddress) = ParseBluetoothDeviceId(deviceInfo.Id);
            deviceInfoBuilder.AppendLine($"  身份信息:");
            deviceInfoBuilder.AppendLine($"    電腦藍牙地址: {hostAddress}");
            deviceInfoBuilder.AppendLine($"    設備藍牙地址: {deviceAddress}");

            if (deviceInfo.Name.Contains("iPad", StringComparison.OrdinalIgnoreCase))
            {
                _foundIpadBaseAddress = GetBaseAddress(broadcastAddress);
                deviceInfoBuilder.AppendLine($"  *** 這是 iPad 的 BR/EDR 模組 ***");
                deviceInfoBuilder.AppendLine($"  基礎地址: {_foundIpadBaseAddress} - 若設備支援雙模 BR/EDR & BLE 則有基礎地址");
            }

            Log(deviceInfoBuilder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            Log($"處理傳統藍牙設備時發生錯誤: {ex.Message}");
        }
    }

    private void OnClassicDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
    {
        // 可以處理設備更新事件
    }

    private void OnClassicDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
    {
        // 可以處理設備移除事件
    }

    private void OnClassicEnumerationCompleted(DeviceWatcher sender, object args)
    {
        Log("傳統藍牙設備掃描完成");
    }

    private void OnClassicWatcherStopped(DeviceWatcher sender, object args)
    {
        Log("傳統藍牙掃描器已停止");
    }

    private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (_isStopRequested) return;

        try
        {
            string advertisingAddress = UlongToMacAddress(args.BluetoothAddress);
            if (!_processedDevices.Add(advertisingAddress)) return;

            bool hasValidName = false;
            string? deviceName = null;

            try
            {
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                if (device != null && !string.IsNullOrEmpty(device.Name) && 
                    !device.Name.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))
                {
                    hasValidName = true;
                    deviceName = device.Name;
                }
            }
            catch (Exception ex)
            {
                Log($"連接設備時發生錯誤: {ex.Message}");
                return;
            }

            // 如果沒有有效的名稱，則不顯示
            if (!hasValidName)
            {
                return;
            }

            _deviceCount++;

            var deviceInfo = new System.Text.StringBuilder();
            deviceInfo.AppendLine("發現新設備 (BLE):");
            deviceInfo.AppendLine($"  廣播地址: {advertisingAddress.ToUpper()}");
            deviceInfo.AppendLine($"  地址類型: {args.BluetoothAddressType}");
            deviceInfo.AppendLine($"  RSSI: {args.RawSignalStrengthInDBm}dBm");
            deviceInfo.AppendLine($"  廣播類型: {args.AdvertisementType}");
            deviceInfo.AppendLine($"  藍牙類型: BLE");
            deviceInfo.AppendLine($"  設備名稱: {deviceName}");

            if (args.Advertisement != null && !string.IsNullOrEmpty(args.Advertisement.LocalName))
            {
                deviceInfo.AppendLine($"  廣播名稱: {args.Advertisement.LocalName}");
            }

            if (args.Advertisement?.ServiceUuids.Count > 0)
            {
                deviceInfo.AppendLine("  服務列表:");
                foreach (var uuid in args.Advertisement.ServiceUuids)
                {
                    deviceInfo.AppendLine($"    {uuid}");
                }
            }

            try
            {
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                if (device?.BluetoothDeviceId?.Id != null)
                {
                    var (hostAddr, deviceAddr) = ParseBluetoothDeviceId(device.BluetoothDeviceId.Id);
                    deviceInfo.AppendLine($"  身份信息:");
                    deviceInfo.AppendLine($"    電腦藍牙地址: {hostAddr}");
                    deviceInfo.AppendLine($"    設備藍牙地址: {deviceAddr}");
                }
            }
            catch (Exception ex)
            {
                deviceInfo.AppendLine($"獲取身份信息時發生錯誤: {ex.Message}");
            }

            string baseAddress = GetBaseAddress(advertisingAddress);
            if ((_foundIpadBaseAddress != null && baseAddress == _foundIpadBaseAddress) ||
                (!string.IsNullOrEmpty(deviceName) && deviceName.Contains("iPad", StringComparison.OrdinalIgnoreCase)))
            {
                deviceInfo.AppendLine($"  *** 這是 iPad 的 BLE 模組 ***");
                deviceInfo.AppendLine($"  基礎地址: {baseAddress} - 若設備支援雙模 BR/EDR & BLE 則有基礎地址");
            }

            Log(deviceInfo.ToString().TrimEnd());

            if (_deviceCount >= MAX_DEVICES)
            {
                StopScanning("已找到足夠的設備");
            }
        }
        catch (Exception ex)
        {
            Log($"處理設備時發生錯誤: {ex.Message}");
            if (ex.InnerException != null)
            {
                Log($"內部錯誤: {ex.InnerException.Message}");
            }
        }
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        Log("BLE 掃描器已停止");
        _scanTaskSource?.TrySetResult(true);
    }

    private void StopScanning(string reason)
    {
        if (!_isStopRequested)
        {
            _isStopRequested = true;
            Log($"掃描結束 - {reason}");
            _bleWatcher.Stop();
            _classicWatcher?.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _disposed = true;
            _bleWatcher.Received -= OnAdvertisementReceived;
            _bleWatcher.Stopped -= OnWatcherStopped;

            if (_classicWatcher != null)
            {
                _classicWatcher.Added -= OnClassicDeviceAdded;
                _classicWatcher.Updated -= OnClassicDeviceUpdated;
                _classicWatcher.Removed -= OnClassicDeviceRemoved;
                _classicWatcher.EnumerationCompleted -= OnClassicEnumerationCompleted;
                _classicWatcher.Stopped -= OnClassicWatcherStopped;
            }

            StopScanning("掃描器被釋放");
            _processedDevices.Clear();
        }
        catch (Exception ex)
        {
            Log($"釋放資源時發生錯誤: {ex.Message}");
        }
    }
}
