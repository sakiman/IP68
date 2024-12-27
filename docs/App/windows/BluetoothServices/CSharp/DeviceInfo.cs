namespace BluetoothServices;

public class DeviceInfo
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public short Rssi { get; set; }
    public string ConnectStatus { get; set; } = "";
}
