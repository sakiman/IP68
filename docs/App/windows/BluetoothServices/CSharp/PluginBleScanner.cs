using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BluetoothServices;

public class PluginBleScanner : IDisposable
{
    public delegate void LogMessageHandler(string message);
    public event LogMessageHandler? LogMessage;

    private readonly string _targetDeviceAddress;
    private readonly HashSet<string> _targetDevices = new()
    {
        "18:7E:B9:6A:B8:5D"  // Rel的iPad
    };
    private readonly Dictionary<string, DeviceInfo> _discoveredDevices = new();
    private readonly Dictionary<string, string> _lastManufacturerData = new();
    private readonly Dictionary<string, HashSet<string>> _deviceAliases = new();
    private IAdapter? _adapter;
    private bool _disposed;
    private readonly List<IDevice> _devicesToConnect = new();

    private static readonly Dictionary<string, string> ServiceDescriptions = new()
    {
        // 標準藍牙服務
        { "00001800-0000-1000-8000-00805f9b34fb", "通用存取服務 (Generic Access)" },
        { "00001801-0000-1000-8000-00805f9b34fb", "通用屬性服務 (Generic Attribute)" },
        { "0000180a-0000-1000-8000-00805f9b34fb", "設備信息服務 (Device Information)" },
        { "0000180f-0000-1000-8000-00805f9b34fb", "電池服務 (Battery Service)" },
        { "00001805-0000-1000-8000-00805f9b34fb", "當前時間服務 (Current Time Service)" },
        
        // Apple 特定服務
        { "d0611e78-bbb4-4591-a5f8-487910ae4366", "Apple 連續性服務 (Apple Continuity Service)" },
        { "9fa480e0-4967-4542-9390-d343dc5d04ae", "Apple 近距離互動服務 (Apple Nearby Interaction)" },
        { "7905f431-b5ce-4e99-a40f-4b1e122d00d0", "Apple 通知中心服務 (Apple Notification Center Service)" },
        { "89d3502b-0f36-433a-8ef4-c502ad55f8dc", "Apple 媒體控制服務 (Apple Media Service)" }
    };

    private static readonly Dictionary<string, string> CharacteristicDescriptions = new()
    {
        // 標準藍牙特徵
        { "00002a00-0000-1000-8000-00805f9b34fb", "設備名稱 (Device Name)" },
        { "00002a01-0000-1000-8000-00805f9b34fb", "外觀 (Appearance)" },
        { "00002a19-0000-1000-8000-00805f9b34fb", "電池電量 (Battery Level)" },
        { "00002a24-0000-1000-8000-00805f9b34fb", "型號 (Model Number String)" },
        { "00002a29-0000-1000-8000-00805f9b34fb", "製造商名稱 (Manufacturer Name)" },
        { "00002a05-0000-1000-8000-00805f9b34fb", "服務變更 (Service Changed)" },
        
        // Apple 連續性服務特徵
        { "8667556c-9a37-4c91-84ed-54ee27d90049", "Apple 連續性特徵 (Apple Continuity Characteristic)" },
        
        // Apple 近距離互動服務特徵
        { "af0badb1-5b99-43cd-917a-a77bc549e3cc", "Apple 近距離互動特徵 (Apple Nearby Interaction Characteristic)" },
        
        // Apple 通知中心服務特徵
        { "69d1d8f3-45e1-49a8-9821-9bbdfdaad9d9", "通知控制點 (Control Point)" },
        { "9fbf120d-6301-42d9-8c58-25e699a21dbd", "通知來源 (Notification Source)" },
        { "22eac6e9-24d6-4bb5-be44-b36ace7c7bfb", "數據來源 (Data Source)" },
        
        // Apple 媒體控制服務特徵
        { "9b3c81d8-57b1-4a8a-b8df-0e56f7ca51c2", "遠程命令 (Remote Command)" },
        { "2f7cabce-808d-411f-9a0c-bb92ba96c102", "實體更新 (Entity Update)" },
        { "c6b2f38c-23ab-46d8-a6ab-a3a870bbd5d7", "實體屬性 (Entity Attribute)" }
    };

    private string GetServiceDescription(string serviceId)
    {
        serviceId = serviceId.ToLower();
        return ServiceDescriptions.TryGetValue(serviceId, out var description)
            ? $"{serviceId} ({description})"
            : $"{serviceId} (未知服務)";
    }

    private string GetCharacteristicDescription(string characteristicId)
    {
        characteristicId = characteristicId.ToLower();
        return CharacteristicDescriptions.TryGetValue(characteristicId, out var description)
            ? $"{characteristicId} ({description})"
            : $"{characteristicId} (未知特徵)";
    }

    public PluginBleScanner(string targetDeviceAddress = "")
    {
        _targetDeviceAddress = targetDeviceAddress;
        _adapter = CrossBluetoothLE.Current.Adapter;
    }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            Log("掃描器已被釋放");
            return;
        }

        try
        {
            Log("開始搜索...");

            if (_adapter == null)
            {
                Log("藍牙適配器未初始化");
                return;
            }

            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            _adapter.DeviceDiscovered += OnDeviceDiscovered;

            await _adapter.StartScanningForDevicesAsync();

            // 等待掃描完成
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);

            await _adapter.StopScanningForDevicesAsync();
            Log("掃描結束");

            // 顯示找到的目標設備
            if (_devicesToConnect.Count > 0)
            {
                Log($"\n找到 {_devicesToConnect.Count} 個目標設備:");
                foreach (var device in _devicesToConnect)
                {
                    Log($"   {device.Name ?? "未知"} ({device.Id})");
                }
            }
            else
            {
                Log("未找到目標設備");
            }
        }
        catch (Exception ex)
        {
            Log($"掃描錯誤: {ex.Message}");
        }
        finally
        {
            if (_adapter != null)
            {
                _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            }
        }
    }

    public async Task ConnectToTargetDevicesAsync()
    {
        if (_disposed || _adapter == null) return;

        if (_devicesToConnect.Count == 0)
        {
            Log("沒有可連接的目標設備");
            return;
        }

        foreach (var device in _devicesToConnect)
        {
            try
            {
                Log($"嘗試連接設備: {device.Name ?? "未知"} ({device.Id})");
                
                // 檢查設備狀態
                bool needsConnection = device.State != Plugin.BLE.Abstractions.DeviceState.Connected;
                
                if (needsConnection)
                {
                    // 嘗試連接
                    await _adapter.ConnectToDeviceAsync(device);
                    Log("成功連接到設備！");
                    // 等待一小段時間確保連接穩定
                    await Task.Delay(500);
                }
                else
                {
                    Log("設備已連接，正在讀取服務...");
                }

                // 獲取服務和特徵
                var services = await device.GetServicesAsync();
                Log($"發現 {services.Count} 個服務:");
                foreach (var service in services)
                {
                    Log($"   服務: {GetServiceDescription(service.Id.ToString())}");
                    var characteristics = await service.GetCharacteristicsAsync();
                    foreach (var characteristic in characteristics)
                    {
                        Log($"     特徵: {GetCharacteristicDescription(characteristic.Id.ToString())}");
                        Log($"       可讀: {characteristic.CanRead}");
                        Log($"       可寫: {characteristic.CanWrite}");
                        Log($"       可通知: {characteristic.CanUpdate}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"操作失敗: {ex.Message}");
                if (ex.Message.Contains("Device is disconnected") || ex.Message.Contains("connection was lost"))
                {
                    Log("設備連接已斷開，請重新嘗試連接");
                }
            }
        }
    }

    private string ParseAdvertisementFlags(byte[] data)
    {
        if (data.Length == 0) return "未知";
        var flags = data[0];
        var flagsList = new List<string>();
        
        if ((flags & 0x01) != 0) flagsList.Add("LE Limited Discoverable Mode");
        if ((flags & 0x02) != 0) flagsList.Add("LE General Discoverable Mode");
        if ((flags & 0x04) != 0) flagsList.Add("BR/EDR Not Supported");
        if ((flags & 0x08) != 0) flagsList.Add("Simultaneous LE and BR/EDR (Controller)");
        if ((flags & 0x10) != 0) flagsList.Add("Simultaneous LE and BR/EDR (Host)");
        
        var hexValue = flags.ToString("X2");
        var binaryValue = Convert.ToString(flags, 2).PadLeft(8, '0');
        var flagsText = flagsList.Count > 0
            ? $"[啟用的標誌]: {string.Join("、", flagsList)}": "[無啟用的標誌]";
        
        return $"0x{hexValue} (二進制: {binaryValue}){flagsText}";
    }

    private string ParseManufacturerData(byte[] data)
    {
        if (data.Length < 2) return "無效數據";
        
        var companyId = BitConverter.ToUInt16(new[] { data[0], data[1] }, 0);
        var company = companyId switch
        {
            0x004C => "Apple Inc.",
            0x0059 => "Nordic Semiconductor",
            0x0075 => "Samsung Electronics",
            0x0087 => "Garmin",
            0x0157 => "Xiaomi",
            0x0499 => "Ruuvi Innovations Ltd",
            0x06D6 => "Bosch Sensortec GmbH",
            0x0822 => "Telink Semiconductor",
            _ => $"未知 (0x{companyId:X4})"
        };

        if (companyId == 0x004C && data.Length >= 3)  // Apple
        {
            var type = data[2];
            var deviceType = type switch
            {
                0x01 => "iBeacon",
                0x02 => "AirPrint",
                0x03 => "AirPlay",
                0x04 => "Apple TV",
                0x05 => "AirDrop",
                0x06 => "HomeKit",
                0x07 => "未配對的 AirPods",
                0x08 => "Siri 設備",
                0x09 => "未配對的 AirPods",
                0x0A => "未配對的 AirPods Pro",
                0x0B => "未配對的 AirPods Max",
                0x0C => "Find My",
                0x0D => "未配對的 AirTag",
                0x0E => "MagSafe",
                0x0F => "未配對的配件",
                0x10 => "等待配對的配件",
                _ => $"未知 Apple 類型 (0x{type:X2})"
            };

            var subType = data.Length >= 4 ? data[3] : (byte)0;
            var subTypeDesc = type switch
            {
                0x07 when subType == 0x01 => " - 初次配對",
                0x07 when subType == 0x02 => " - 回復配對",
                0x07 when subType == 0x03 => " - 主動配對",
                0x0D when subType == 0x01 => " - 設置模式",
                0x0D when subType == 0x02 => " - 尋找模式",
                0x0D when subType == 0x03 => " - 定位模式",
                _ => ""
            };
            
            var appleDataHex = BitConverter.ToString(data.Skip(3).ToArray());
            return $"{company} - {deviceType}{subTypeDesc}, 數據: {appleDataHex}";
        }

        var dataHex = BitConverter.ToString(data.Skip(2).ToArray());
        return $"{company}, 數據: {dataHex}";
    }

    private string GetDeviceDisplayName(IDevice device, byte[]? manufacturerData = null)
    {
        // 如果有實際名稱且不是以 "Bluetooth" 開頭，直接返回
        if (!string.IsNullOrEmpty(device.Name) && !device.Name.StartsWith("Bluetooth"))
        {
            // 記錄設備別名
            var deviceId = device.Id.ToString();
            if (!_deviceAliases.ContainsKey(deviceId))
            {
                _deviceAliases[deviceId] = new HashSet<string>();
            }
            _deviceAliases[deviceId].Add(device.Name);
            return device.Name;
        }

        // 檢查製造商數據
        if (manufacturerData != null && manufacturerData.Length >= 3)
        {
            var companyId = BitConverter.ToUInt16(manufacturerData, 0);
            if (companyId == 0x004C) // Apple
            {
                var type = manufacturerData[2];
                var deviceType = type switch
                {
                    0x01 => "iBeacon",
                    0x02 => "AirPrint 設備",
                    0x03 => "AirPlay 設備",
                    0x04 => "Apple TV",
                    0x05 => "AirDrop 設備",
                    0x06 => "HomeKit 設備",
                    0x07 => "未配對的 AirPods",
                    0x08 => "Siri 設備",
                    0x09 => "未配對的 AirPods",
                    0x0A => "未配對的 AirPods Pro",
                    0x0B => "未配對的 AirPods Max",
                    0x0C => "Find My",
                    0x0D => "未配對的 AirTag",
                    0x0E => "MagSafe",
                    0x0F => "未配對的配件",
                    0x10 => "等待配對的配件",
                    _ => $"未知 Apple 設備 (0x{type:X2})"
                };

                // 檢查是否有已知的實際名稱
                var deviceId = device.Id.ToString();
                if (_deviceAliases.ContainsKey(deviceId) && _deviceAliases[deviceId].Any())
                {
                    return _deviceAliases[deviceId].First();
                }

                return deviceType;
            }
        }

        // 如果都無法識別，返回原始名稱
        return device.Name ?? "未知設備";
    }

    private async Task GetServicesInfo(IDevice device, StringBuilder deviceInfo)
    {
        try
        {
            var services = await device.GetServicesAsync();
            if (services != null && services.Any())
            {
                deviceInfo.AppendLine($"發現 {services.Count} 個服務:");
                
                foreach (var service in services)
                {
                    var serviceDesc = GetServiceDescription(service.Id.ToString());
                    deviceInfo.AppendLine($"   服務: {serviceDesc}");
                    bool serviceAccessDenied = false;
                    StringBuilder tempBuffer = new StringBuilder();
                    
                    try
                    {
                        var characteristics = await service.GetCharacteristicsAsync();
                        if (characteristics != null && characteristics.Any())
                        {
                            foreach (var characteristic in characteristics)
                            {
                                try
                                {
                                    var charDesc = GetCharacteristicDescription(characteristic.Id.ToString());
                                    tempBuffer.AppendLine($"     特徵: {charDesc}");
                                    tempBuffer.AppendLine($"       可讀: {characteristic.CanRead}");
                                    tempBuffer.AppendLine($"       可寫: {characteristic.CanWrite}");
                                    tempBuffer.AppendLine($"       可通知: {characteristic.CanUpdate}");
                                }
                                catch
                                {
                                    serviceAccessDenied = true;
                                    break;
                                }
                            }
                        }
                        
                        if (!serviceAccessDenied)
                        {
                            deviceInfo.Append(tempBuffer.ToString());
                        }
                    }
                    catch (Exception ex) when (ex.Message.Contains("Device is disconnected"))
                    {
                        deviceInfo.AppendLine("設備已斷開連接");
                        Log(deviceInfo.ToString());
                        return;
                    }
                    catch (Exception ex) when (
                        ex.Message.Contains("AccessDenied") || 
                        ex.Message.Contains("GetCharacteristicsNativeAsync"))
                    {
                        deviceInfo.AppendLine("     備註：");
                        deviceInfo.AppendLine("     - iOS 設備出於安全考慮，會限制對某些藍牙服務的訪問");
                        deviceInfo.AppendLine("     - 此服務可能需要特殊的權限或配對才能訪問");
                        deviceInfo.AppendLine("     - 這種限制是 iOS 的安全機制的一部分，用於保護設備安全");
                        continue;
                    }
                    catch (Exception)
                    {
                        deviceInfo.AppendLine("     備註：");
                        deviceInfo.AppendLine("     - 無法讀取此服務的特徵");
                        continue;
                    }

                    if (serviceAccessDenied)
                    {
                        deviceInfo.AppendLine("     備註：");
                        deviceInfo.AppendLine("     - iOS 設備出於安全考慮，會限制對某些藍牙服務的訪問");
                        deviceInfo.AppendLine("     - 此服務可能需要特殊的權限或配對才能訪問");
                        deviceInfo.AppendLine("     - 這種限制是 iOS 的安全機制的一部分，用於保護設備安全");
                    }
                }
                
                Log(deviceInfo.ToString());
            }
        }
        catch (Exception ex) when (ex.Message.Contains("Device is disconnected"))
        {
            deviceInfo.AppendLine("設備已斷開連接");
            Log(deviceInfo.ToString());
        }
        catch (Exception)
        {
            Log(deviceInfo.ToString());
        }
    }

    private void OnDeviceDiscovered(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs args)
    {
        if (_disposed) return;

        var device = args.Device;
        if (device == null) return;

        // 如果設備名稱以 Bluetooth 開頭，則忽略
        if (!string.IsNullOrEmpty(device.Name) && 
            device.Name.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var deviceId = device.Id.ToString();
        
        // 檢查是否是目標設備
        var shortMacAddress = deviceId.Split('-').Last();
        if (shortMacAddress.Equals(_targetDeviceAddress.Replace(":", ""), StringComparison.OrdinalIgnoreCase))
        {
            if (!_devicesToConnect.Contains(device))
            {
                _devicesToConnect.Add(device);
            }
        }

        var displayName = GetDeviceDisplayName(device);
        var isNewDevice = !_discoveredDevices.ContainsKey(deviceId);

        if (isNewDevice)
        {
            var deviceInfo = new StringBuilder();
            deviceInfo.AppendLine("發現新設備 (BLE):");
            deviceInfo.AppendLine($"   設備名稱: {displayName}");
            deviceInfo.AppendLine($"   設備ID: {deviceId}");
            var formattedMacAddress = deviceId.Split('-').Last().Insert(10, ":").Insert(8, ":").Insert(6, ":").Insert(4, ":").Insert(2, ":");
            deviceInfo.AppendLine($"   廣播地址: {formattedMacAddress.ToUpper()}");
            var connectStatus = device.Rssi > -70 ? "良好" : "較差";
            deviceInfo.AppendLine($"   RSSI: {device.Rssi} dBm ({connectStatus})");
            deviceInfo.AppendLine($"   連接狀態: {(device.State == Plugin.BLE.Abstractions.DeviceState.Connected ? "已連接" : "未連接")}");

            // 如果設備已連接，獲取服務信息
            if (device.State == Plugin.BLE.Abstractions.DeviceState.Connected)
            {
                Task.Run(async () => await GetServicesInfo(device, deviceInfo));
            }

            // 保存設備信息
            _discoveredDevices[deviceId] = new DeviceInfo
            {
                Name = displayName,
                Address = deviceId,
                Rssi = (short)device.Rssi,
                ConnectStatus = connectStatus
            };

            // 檢查並顯示廣播數據
            if (device.AdvertisementRecords != null)
            {
                foreach (var record in device.AdvertisementRecords)
                {
                    var typeDesc = record.Type switch
                    {
                        AdvertisementRecordType.Flags => $"類型: Flags: {ParseAdvertisementFlags(record.Data)}",
                        AdvertisementRecordType.TxPowerLevel => $"類型: 發射功率: {(sbyte)record.Data[0]} dBm",
                        AdvertisementRecordType.ManufacturerSpecificData => $"類型: 製造商數據: {ParseManufacturerData(record.Data)}",
                        AdvertisementRecordType.CompleteLocalName => $"類型: CompleteLocalName, 數據: {BitConverter.ToString(record.Data)}",
                        AdvertisementRecordType.UuidsComplete128Bit => $"類型: 完整 128 位服務: {BitConverter.ToString(record.Data)}",
                        AdvertisementRecordType.UuidsIncomplete128Bit => $"類型: 不完整 128 位服務: {BitConverter.ToString(record.Data)}",
                        AdvertisementRecordType.UuidsComplete16Bit => $"類型: UuidsComplete16Bit, 數據: {BitConverter.ToString(record.Data)}",
                        _ => $"類型: {record.Type}, 數據: {BitConverter.ToString(record.Data)}"
                    };
                    deviceInfo.AppendLine($"     {typeDesc}");
                }
            }

            Log(deviceInfo.ToString().TrimEnd());
        }
        else
        {
            // 更新 RSSI
            if (_discoveredDevices[deviceId].Rssi != (short)device.Rssi)
            {
                _discoveredDevices[deviceId].Rssi = (short)device.Rssi;
                _discoveredDevices[deviceId].ConnectStatus = device.Rssi > -70 ? "良好" : "較差";
            }

            // 檢查製造商數據是否有更新
            if (device.AdvertisementRecords != null)
            {
                var mfgRecord = device.AdvertisementRecords.FirstOrDefault(r => 
                    r.Type == AdvertisementRecordType.ManufacturerSpecificData);
                if (mfgRecord != null)
                {
                    var mfgDataStr = BitConverter.ToString(mfgRecord.Data);
                    if (_lastManufacturerData.TryGetValue(deviceId, out var lastData) && lastData != mfgDataStr)
                    {
                        var updateInfo = new StringBuilder();
                        updateInfo.AppendLine("製造商數據已更新:");
                        updateInfo.AppendLine($"   設備: {device.Name ?? "未知"} ({deviceId})");
                        updateInfo.AppendLine($"   舊數據: {lastData}");
                        updateInfo.AppendLine($"   新數據: {mfgDataStr}");
                        Log(updateInfo.ToString().TrimEnd());
                        _lastManufacturerData[deviceId] = mfgDataStr;
                    }
                }
            }
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(message);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _adapter?.StopScanningForDevicesAsync();
            _disposed = true;
            _discoveredDevices.Clear();
            _lastManufacturerData.Clear();
            _deviceAliases.Clear();
        }
    }
}