using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace BluetoothServices;

public class InTheHandScanner : IDisposable
{
    private readonly string _targetDeviceAddress;
    private bool _disposed;
    private BluetoothClient? _client;

    public event Action<string>? LogMessage;

    public InTheHandScanner(string targetAddress)
    {
        _targetDeviceAddress = targetAddress;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(message);
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
            var client = new BluetoothClient();
            Log("開始搜索...");

            await Task.Run(() =>
            {
                var devices = client.DiscoverDevices();
                foreach (var device in devices)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Log("掃描被取消");
                        break;
                    }

                    var deviceInfo = new StringBuilder();
                    deviceInfo.AppendLine("發現新設備 (BR/EDR):");
                    deviceInfo.AppendLine($"  設備名稱: {device.DeviceName}");
                    var addressBytes = device.DeviceAddress.ToByteArray();
                    Array.Reverse(addressBytes);  // 反轉位元組順序
                    var address = BitConverter.ToString(addressBytes.Skip(2).Take(6).ToArray()).Replace("-", ":");  // 跳過前2個位元組，取接下來的6個位元組
                    deviceInfo.AppendLine($"  廣播地址: {address}");
                    deviceInfo.AppendLine($"  地址類型: Public - BR/EDR 只使用 Public 地址（固定的 MAC 地址）");
                    deviceInfo.AppendLine($"  藍牙類型: BR/EDR");
                    deviceInfo.AppendLine($"  已配對: {device.Authenticated}");
                    deviceInfo.AppendLine($"  已連接: {device.Connected}");
                    
                    // 獲取設備的服務
                    try
                    {
                        var services = device.InstalledServices;
                        if (services != null && services.Any())
                        {
                            deviceInfo.AppendLine("  已安裝的服務:");
                            foreach (var service in services)
                            {
                                deviceInfo.AppendLine($"    {service}");
                            }
                        }
                        else
                        {
                            deviceInfo.AppendLine("  沒有找到已安裝的服務");
                        }
                    }
                    catch (Exception ex)
                    {
                        deviceInfo.AppendLine($"讀取服務時發生錯誤: {ex.Message}");
                    }

                    Log(deviceInfo.ToString().TrimEnd());
                }

                Log("掃描完成");
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Log("掃描被取消");
            throw;
        }
        catch (Exception ex)
        {
            Log($"掃描時發生錯誤: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _disposed = true;
            _client?.Dispose();
            _client = null;
        }
        catch (Exception ex)
        {
            Log($"釋放資源時發生錯誤: {ex.Message}");
        }
    }
}
