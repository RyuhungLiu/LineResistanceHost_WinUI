namespace LineResistanceHost.Models;

public enum Oa1DeviceKind
{
    Oa1,
    PowerZK2
}

public sealed record Oa1DeviceInfo(string Path, string ProductName, ushort VendorId, ushort ProductId, Oa1DeviceKind Kind)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ProductName)
        ? $"VID_{VendorId:X4} PID_{ProductId:X4}"
        : $"VID_{VendorId:X4} PID_{ProductId:X4}";

    public string DeviceLabel => Kind == Oa1DeviceKind.PowerZK2 ? "POWER-Z K2" : "ATK-OA1";
}
