using System.Collections.ObjectModel;
using System.Buffers.Binary;

namespace LineResistanceHost.Models;

public sealed class Oa1Frame
{
    private static readonly byte[] Header = [0xDF, 0x02, 0x07, 0x01];
    private static readonly string[] PinOrder = ["DP", "DM", "C1", "C2", "S1", "S2"];
    private const int WitrnK2DPlusOffset = 30;
    private const int WitrnK2DMinusOffset = 34;
    private const int WitrnK2CurrentOffset = 50;
    private static readonly (double TotalMilliOhms, double Alpha)[] WitrnK2Calibration =
    [
        (23.13, 0.558),
        (65.75, 0.532),
        (92.84, 0.456),
        (119.41, 0.397),
        (142.46, 0.473)
    ];

    public required Oa1DeviceKind SourceKind { get; init; }
    public required string Cable { get; init; }
    public required IReadOnlyDictionary<string, bool> Pins { get; init; }
    public required bool PinsApplicable { get; init; }
    public required double? VbusMilliOhms { get; init; }
    public required double? GbusMilliOhms { get; init; }
    public required double? TotalMilliOhmsOverride { get; init; }
    public required byte Checksum { get; init; }
    public required bool ChecksumApplicable { get; init; }
    public required bool ChecksumValid { get; init; }
    public required bool TailValid { get; init; }
    public required float? DPlusVolts { get; init; }
    public required float? DMinusVolts { get; init; }
    public required float? CurrentAmps { get; init; }
    public required double? EstimatedAlpha { get; init; }
    public required double? EstimatedV0Volts { get; init; }
    public required bool SplitValuesEstimated { get; init; }
    public required bool SplitEstimateExtrapolated { get; init; }
    public required byte[] Raw { get; init; }

    public double? TotalMilliOhms => TotalMilliOhmsOverride ?? (VbusMilliOhms.HasValue && GbusMilliOhms.HasValue
        ? VbusMilliOhms.Value + GbusMilliOhms.Value
        : null);
    public string RawHex => string.Join(" ", Raw.Select(value => value.ToString("X2")));

    public static Oa1Frame Empty { get; } = new()
    {
        SourceKind = Oa1DeviceKind.Oa1,
        Cable = "N/A",
        Pins = new ReadOnlyDictionary<string, bool>(PinOrder.ToDictionary(pin => pin, _ => false)),
        PinsApplicable = false,
        VbusMilliOhms = null,
        GbusMilliOhms = null,
        TotalMilliOhmsOverride = null,
        Checksum = 0,
        ChecksumApplicable = false,
        ChecksumValid = false,
        TailValid = false,
        DPlusVolts = null,
        DMinusVolts = null,
        CurrentAmps = null,
        EstimatedAlpha = null,
        EstimatedV0Volts = null,
        SplitValuesEstimated = false,
        SplitEstimateExtrapolated = false,
        Raw = []
    };

    public static bool TryParse(Oa1DeviceKind kind, ReadOnlySpan<byte> report, out Oa1Frame? frame)
    {
        return kind == Oa1DeviceKind.WitrnK2
            ? TryParseWitrnK2(report, out frame)
            : TryParseOa1(report, out frame);
    }

    public static bool TryParseOa1(ReadOnlySpan<byte> report, out Oa1Frame? frame)
    {
        frame = null;

        var payload = LocatePayload(report);
        if (payload.Length < 12)
        {
            return false;
        }

        var raw = payload[..12].ToArray();
        var cable = raw[4] switch
        {
            1 => "CC",
            2 => "AC",
            _ => "ERR"
        };

        var pinFlags = raw[5];
        var pins = new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>
        {
            ["DP"] = (pinFlags & 0x01) != 0,
            ["DM"] = (pinFlags & 0x02) != 0,
            ["C1"] = (pinFlags & 0x04) != 0,
            ["C2"] = (pinFlags & 0x08) != 0,
            ["S1"] = (pinFlags & 0x10) != 0,
            ["S2"] = (pinFlags & 0x20) != 0
        });

        var checksumTotal = 0;
        for (var index = 0; index <= 10; index++)
        {
            checksumTotal += raw[index];
        }

        frame = new Oa1Frame
        {
            SourceKind = Oa1DeviceKind.Oa1,
            Cable = cable,
            Pins = pins,
            PinsApplicable = true,
            VbusMilliOhms = (ushort)(raw[6] | raw[7] << 8),
            GbusMilliOhms = (ushort)(raw[8] | raw[9] << 8),
            TotalMilliOhmsOverride = null,
            Checksum = raw[10],
            ChecksumApplicable = true,
            ChecksumValid = checksumTotal == 512,
            TailValid = raw[11] == 0xFF,
            DPlusVolts = null,
            DMinusVolts = null,
            CurrentAmps = null,
            EstimatedAlpha = null,
            EstimatedV0Volts = null,
            SplitValuesEstimated = false,
            SplitEstimateExtrapolated = false,
            Raw = raw
        };

        return true;
    }

    public static bool TryParseWitrnK2(ReadOnlySpan<byte> report, out Oa1Frame? frame)
    {
        frame = null;

        var payload = LocateWitrnK2Payload(report);
        if (payload.Length < 57)
        {
            return false;
        }

        var dPlus = ReadSingleLittleEndian(payload.Slice(WitrnK2DPlusOffset, 4));
        var dMinus = ReadSingleLittleEndian(payload.Slice(WitrnK2DMinusOffset, 4));
        var currentAmps = ReadSingleLittleEndian(payload.Slice(WitrnK2CurrentOffset, 4));
        if (!float.IsFinite(dPlus) || !float.IsFinite(dMinus) || !float.IsFinite(currentAmps) || currentAmps <= 0)
        {
            return false;
        }

        var totalMilliOhms = Math.Round((dPlus - dMinus) / currentAmps * 1000f, 1, MidpointRounding.AwayFromZero);
        if (totalMilliOhms < 0)
        {
            return false;
        }

        var (alpha, extrapolated) = EstimateWitrnK2GbusAlpha(totalMilliOhms);
        var gbusMilliOhms = Math.Round(totalMilliOhms * alpha, 1, MidpointRounding.AwayFromZero);
        var vbusMilliOhms = Math.Round(totalMilliOhms - gbusMilliOhms, 1, MidpointRounding.AwayFromZero);
        var estimatedV0 = dMinus + currentAmps * gbusMilliOhms / 1000d;

        frame = new Oa1Frame
        {
            SourceKind = Oa1DeviceKind.WitrnK2,
            Cable = "N/A",
            Pins = new ReadOnlyDictionary<string, bool>(PinOrder.ToDictionary(pin => pin, _ => false)),
            PinsApplicable = false,
            VbusMilliOhms = vbusMilliOhms,
            GbusMilliOhms = gbusMilliOhms,
            TotalMilliOhmsOverride = totalMilliOhms,
            Checksum = 0,
            ChecksumApplicable = false,
            ChecksumValid = false,
            TailValid = false,
            DPlusVolts = dPlus,
            DMinusVolts = dMinus,
            CurrentAmps = currentAmps,
            EstimatedAlpha = alpha,
            EstimatedV0Volts = estimatedV0,
            SplitValuesEstimated = true,
            SplitEstimateExtrapolated = extrapolated,
            Raw = payload[..Math.Min(payload.Length, 57)].ToArray()
        };

        return true;
    }

    public static Oa1Frame CreateSample()
    {
        byte[] raw = [0xDF, 0x02, 0x07, 0x01, 0x01, 0x3F, 0x58, 0x00, 0x4A, 0x00, 0x25, 0xFF];
        _ = TryParseOa1(raw, out var frame);
        return frame ?? Empty;
    }

    private static ReadOnlySpan<byte> LocatePayload(ReadOnlySpan<byte> report)
    {
        if (HasHeader(report))
        {
            return report;
        }

        if (report.Length > 1 && HasHeader(report[1..]))
        {
            return report[1..];
        }

        return [];
    }

    private static ReadOnlySpan<byte> LocateWitrnK2Payload(ReadOnlySpan<byte> report)
    {
        if (report.Length > 0 && report[0] == 0xFF)
        {
            return report;
        }

        if (report.Length > 1 && report[1] == 0xFF)
        {
            return report[1..];
        }

        return [];
    }

    private static bool HasHeader(ReadOnlySpan<byte> value)
    {
        return value.Length >= Header.Length && value[..Header.Length].SequenceEqual(Header);
    }

    private static float ReadSingleLittleEndian(ReadOnlySpan<byte> value)
    {
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value));
    }

    private static (double Alpha, bool Extrapolated) EstimateWitrnK2GbusAlpha(double totalMilliOhms)
    {
        if (totalMilliOhms <= WitrnK2Calibration[0].TotalMilliOhms)
        {
                return (ClampAlpha(InterpolateAlpha(totalMilliOhms, WitrnK2Calibration[0], WitrnK2Calibration[1])), true);
        }

        for (var index = 1; index < WitrnK2Calibration.Length; index++)
        {
            var previous = WitrnK2Calibration[index - 1];
            var current = WitrnK2Calibration[index];
            if (totalMilliOhms <= current.TotalMilliOhms)
            {
                return (ClampAlpha(InterpolateAlpha(totalMilliOhms, previous, current)), false);
            }
        }

        return (ClampAlpha(InterpolateAlpha(
            totalMilliOhms,
            WitrnK2Calibration[^2],
            WitrnK2Calibration[^1])), true);
    }

    private static double InterpolateAlpha(
        double totalMilliOhms,
        (double TotalMilliOhms, double Alpha) low,
        (double TotalMilliOhms, double Alpha) high)
    {
        var amount = (totalMilliOhms - low.TotalMilliOhms) / (high.TotalMilliOhms - low.TotalMilliOhms);
        return low.Alpha + (high.Alpha - low.Alpha) * amount;
    }

    private static double ClampAlpha(double alpha)
    {
        return Math.Clamp(alpha, 0, 1);
    }
}

