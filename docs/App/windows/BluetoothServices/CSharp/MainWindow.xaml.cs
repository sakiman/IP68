using System;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BluetoothServices;

public partial class MainWindow : Window
{
    private WindowsBluetoothScanner? _windowsScanner;
    private PluginBleScanner? _pluginBleScanner;
    private InTheHandScanner? _inTheHandScanner;
    private readonly List<CancellationTokenSource> _cancellationTokens = new();
    private const string TARGET_DEVICE_ADDRESS = "18:7E:B9:6A:B8:5D";
    private bool _scannersInitialized = false;

    public MainWindow()
    {
        InitializeComponent();
        InitializeUI();
        
        // 使用 Dispatcher.BeginInvoke 確保在 UI 顯示後再初始化掃描器
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            InitializeScanners();
        }));
    }

    private void InitializeUI()
    {
        Title = "藍牙測試";
        btnStartTest.IsEnabled = true;
        progressBarWindows.IsIndeterminate = false;
        progressBarPluginBle.IsIndeterminate = false;
        progressBarInTheHand.IsIndeterminate = false;

        InitializeFlagDescriptions();
    }

    private void InitializeScanners()
    {
        try
        {
            _windowsScanner = new WindowsBluetoothScanner(TARGET_DEVICE_ADDRESS);
            _pluginBleScanner = new PluginBleScanner(TARGET_DEVICE_ADDRESS);
            _inTheHandScanner = new InTheHandScanner(TARGET_DEVICE_ADDRESS);

            // 設置日誌處理
            if (_windowsScanner != null)
                _windowsScanner.LogMessage += message => SafeLogMessage(txtWindowsLog, message);
            if (_pluginBleScanner != null)
                _pluginBleScanner.LogMessage += message => SafeLogMessage(txtPluginBleLog, message);
            if (_inTheHandScanner != null)
                _inTheHandScanner.LogMessage += message => SafeLogMessage(txtInTheHandLog, message);

            _scannersInitialized = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化掃描器時發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnStartTest_Click(object sender, RoutedEventArgs e)
    {
        if (!_scannersInitialized || _windowsScanner == null || _pluginBleScanner == null || _inTheHandScanner == null)
        {
            MessageBox.Show("掃描器尚未初始化完成，請稍後再試。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            btnStartTest.IsEnabled = false;
            progressBarPluginBle.IsIndeterminate = true;
            progressBarInTheHand.IsIndeterminate = true;
            progressBarWindows.IsIndeterminate = true;

            // 清除之前的日誌
            ClearAllLogs();

            // 停止所有掃描器
            StopAllScanners();

            // 創建取消令牌
            var windowsCts = new CancellationTokenSource();
            var pluginBleCts = new CancellationTokenSource();
            var inTheHandCts = new CancellationTokenSource();

            _cancellationTokens.Add(windowsCts);
            _cancellationTokens.Add(pluginBleCts);
            _cancellationTokens.Add(inTheHandCts);

            try
            {
                // 1. 先執行 Plugin.BLE 掃描器
                SafeLogMessage(txtPluginBleLog, "開始 Plugin.BLE 掃描...");
                await _pluginBleScanner.ScanAsync(pluginBleCts.Token);
                progressBarPluginBle.IsIndeterminate = false;

                // 使用 Plugin.BLE 嘗試連接目標設備
                if (_pluginBleScanner != null)
                {
                    SafeLogMessage(txtPluginBleLog, "開始連接目標設備...");
                    await _pluginBleScanner.ConnectToTargetDevicesAsync();
                }

                await Task.Delay(1000); // 等待一秒再啟動下一個掃描器

                // 2. 然後執行 InTheHand 掃描器
                SafeLogMessage(txtInTheHandLog, "開始 InTheHand 掃描...");
                await _inTheHandScanner.ScanAsync(inTheHandCts.Token);
                progressBarInTheHand.IsIndeterminate = false;

                await Task.Delay(1000); // 等待一秒再啟動下一個掃描器

                // 3. 最後執行 Windows Bluetooth 掃描器
                SafeLogMessage(txtWindowsLog, "開始 Windows Bluetooth 掃描...");
                await _windowsScanner.ScanAsync(windowsCts.Token);
                progressBarWindows.IsIndeterminate = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"掃描過程中發生錯誤: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStartTest.IsEnabled = true;
                progressBarPluginBle.IsIndeterminate = false;
                progressBarInTheHand.IsIndeterminate = false;
                progressBarWindows.IsIndeterminate = false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"測試過程中發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SafeLogMessage(System.Windows.Controls.TextBox textBox, string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => SafeLogMessage(textBox, message)));
            return;
        }

        // 移除開頭的換行符
        if (message.StartsWith("\n"))
        {
            message = message.Substring(1);
        }

        // 如果文本框不是空的，添加換行符
        if (textBox.Text.Length > 0)
        {
            textBox.AppendText(Environment.NewLine);
            
            // 如果是新設備信息或開始/結束掃描的消息，額外添加一個空行
            if (message.StartsWith("發現新設備") || message.StartsWith("開始") || 
                message.Contains("掃描結束") || message.Contains("掃描完成"))
            {
                textBox.AppendText(Environment.NewLine);
            }
        }

        // 只在特定消息前添加時間戳
        if (message.StartsWith("發現新設備") || message.StartsWith("開始") || 
            message.Contains("掃描結束") || message.Contains("掃描完成"))
        {
            message = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        }

        textBox.AppendText(message);
        textBox.ScrollToEnd();
    }

    private void ClearAllLogs()
    {
        txtWindowsLog.Clear();
        txtPluginBleLog.Clear();
        txtInTheHandLog.Clear();
    }

    private void StopAllScanners()
    {
        foreach (var cts in _cancellationTokens)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
        _cancellationTokens.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAllScanners();
        base.OnClosed(e);
    }

    private void InitializeFlagDescriptions()
    {
        var flags = new List<FlagDescription>
        {
            new FlagDescription 
            { 
                Bit = "Bit 0", 
                Value = "0", 
                Meaning = "不使用",
                ValueDescription = "保留位元",
                Usage = "未使用",
                Example = "保留供未來使用"
            },
            new FlagDescription 
            { 
                Bit = "Bit 1", 
                Value = "1", 
                Meaning = "LE Limited Discoverable Mode",
                ValueDescription = "表示設備處於「有限發現模式」，只能在短時間內對其他設備可見。",
                Usage = "通常用於臨時或特定情況下讓設備可見，例如配對模式或設備啟動後的初始時間段。",
                Example = "BLE 鍵身追蹤器在初次啟動時進入 Limited Discoverable Mode，允許用戶的手機找到設備並進行初次配對。"
            },
            new FlagDescription 
            { 
                Bit = "Bit 2", 
                Value = "1", 
                Meaning = "LE General Discoverable Mode",
                ValueDescription = "表示設備處於「一般發現模式」，對所有 BLE 設備可見。",
                Usage = "設備長期廣播，允許其他設備在範圍內找到。",
                Example = "BLE 智能燈泡一直廣播其存在，以便用戶隨時能通過手機應用程序找到並連接。"
            },
            new FlagDescription 
            { 
                Bit = "Bit 3", 
                Value = "0", 
                Meaning = "不使用",
                ValueDescription = "保留位元",
                Usage = "未使用",
                Example = "保留供未來使用"
            },
            new FlagDescription 
            { 
                Bit = "Bit 4", 
                Value = "1", 
                Meaning = "BR/EDR Not Supported",
                ValueDescription = "表示該設備「不支持傳統藍牙（BR/EDR）」模式，僅支持 BLE。",
                Usage = "確保該設備不會被誤認為支持 BR/EDR 的設備。",
                Example = "BLE 心率監測器（如 Polar H10）僅支持低功耗藍牙，廣播時會將此位設為 1。"
            },
            new FlagDescription 
            { 
                Bit = "Bit 5", 
                Value = "0", 
                Meaning = "BR/EDR Controller 不使用",
                ValueDescription = "設備的控制器不支援或未啟用傳統藍牙功能",
                Usage = "指示硬體層面不支援傳統藍牙",
                Example = "純 BLE 設備的控制器"
            },
            new FlagDescription 
            { 
                Bit = "Bit 6", 
                Value = "0", 
                Meaning = "BR/EDR Host 不使用",
                ValueDescription = "設備的主機端不支援或未啟用傳統藍牙功能",
                Usage = "指示軟體層面不支援傳統藍牙",
                Example = "純 BLE 設備的主機端"
            },
            new FlagDescription 
            { 
                Bit = "Bit 7", 
                Value = "0", 
                Meaning = "保留位",
                ValueDescription = "保留位元",
                Usage = "未使用",
                Example = "保留供未來使用"
            }
        };

        listFlags.ItemsSource = flags;
    }
}
