using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;  // 添加這個命名空間
using Windows.Devices.Enumeration;

namespace BluetoothServices;

public class BluetoothTester
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly string _targetDeviceAddress;
    private DeviceInformation? _foundDevice;
    private bool _isScanning;

    public event Action<string>? LogMessage;

    public BluetoothTester(string targetAddress)
    {
        _targetDeviceAddress = targetAddress;
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        _watcher.Received += Watcher_Received;
        _watcher.Stopped += Watcher_Stopped;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"[Windows BLE] {message}");
    }

    private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var addressAsString = args.BluetoothAddress.ToString("X12");
        addressAsString = string.Join(":", Enumerable.Range(0, 6).Select(i => addressAsString.Substring(i * 2, 2)));

        if (addressAsString.Equals(_targetDeviceAddress, StringComparison.OrdinalIgnoreCase))
        {
            Log($"找到目標設備！地址: {addressAsString}");
            _watcher.Stop();

            try
            {
                var deviceSelector = $"System.Devices.Aep.DeviceAddress:=\"{addressAsString}\"";
                var devices = await DeviceInformation.FindAllAsync(deviceSelector);
                _foundDevice = devices.FirstOrDefault();

                if (_foundDevice != null)
                {
                    await ConnectToDeviceAsync();
                }
                else
                {
                    Log($"未找到設備信息，地址: {addressAsString}");
                }
            }
            catch (Exception ex)
            {
                Log($"獲取設備信息時發生錯誤: {ex.Message}");
            }
        }
    }

    private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        Log("掃描已停止");
        _isScanning = false;
    }

    public async Task RunAllTests()
    {
        try
        {
            Log("開始藍牙測試...");
            Log($"正在尋找目標設備 (地址: {_targetDeviceAddress})");

            if (!_isScanning)
            {
                _isScanning = true;
                _watcher.Start();

                // 等待10秒或直到找到設備
                await Task.Delay(10000);

                if (_isScanning)
                {
                    _watcher.Stop();
                }
            }
        }
        catch (Exception ex)
        {
            Log($"測試過程中發生錯誤: {ex.Message}");
        }
    }

    private async Task ConnectToDeviceAsync()
    {
        try
        {
            Log("正在連接設備...");
            
            if (_foundDevice == null)
            {
                Log("沒有可用的設備信息");
                return;
            }

            var device = await BluetoothLEDevice.FromIdAsync(_foundDevice.Id);
            
            if (device == null)
            {
                Log("無法連接到設備");
                return;
            }

            Log($"已連接到設備: {device.Name}");

            // 獲取服務
            var services = await device.GetGattServicesAsync();
            if (services.Status == GattCommunicationStatus.Success)
            {
                foreach (var service in services.Services)
                {
                    Log($"\n服務 UUID: {service.Uuid}");

                    // 獲取特徵值
                    var characteristics = await service.GetCharacteristicsAsync();
                    if (characteristics.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var characteristic in characteristics.Characteristics)
                        {
                            Log($"  特徵值 UUID: {characteristic.Uuid}");
                            Log($"  特徵值屬性: {characteristic.CharacteristicProperties}");
                        }
                    }
                }
            }
            else
            {
                Log("獲取服務失敗");
            }

            device.Dispose();
        }
        catch (Exception ex)
        {
            Log($"連接設備時發生錯誤: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isScanning)
        {
            _watcher.Stop();
        }
        _watcher.Received -= Watcher_Received;
        _watcher.Stopped -= Watcher_Stopped;
    }
}
