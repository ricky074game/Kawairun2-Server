namespace KawaiRun2Launcher.Controller;

internal sealed class BridgeProfile
{
    public int South = 0, East = 1, West = 2, North = 3, Lb = 4, Rb = 5, Back = 8, Start = 9, L3 = 10, R3 = 11;

    public int? LtButton = 6;
    public int? RtButton = 7;

    public int? LtAxis;
    public int? RtAxis;

    public int Lx = 0, Ly = 1, Rx = 2, Ry = 3;
    public int LxSign = 1, LySign = 1, RxSign = 1, RySign = 1;
}

internal static class BridgeProfiles
{
    private static readonly Dictionary<string, BridgeProfile> ByVidPid = BuildTable();

    public static readonly BridgeProfile Generic = new();

    public static BridgeProfile Resolve(string vid, string pid)
    {
        string key = $"{vid}:{pid}".ToUpperInvariant();
        return ByVidPid.TryGetValue(key, out BridgeProfile? profile) ? profile : Generic;
    }

    private static Dictionary<string, BridgeProfile> BuildTable()
    {

        BridgeProfile nintendo = new()
        {
            South = 0, East = 1, West = 2, North = 3,
            Lb = 4, Rb = 5, LtButton = 6, RtButton = 7, LtAxis = null, RtAxis = null,
            Back = 8, Start = 9, L3 = 10, R3 = 11,
            Lx = 0, Ly = 1, Rx = 2, Ry = 3
        };

        BridgeProfile sony = new()
        {
            South = 1, East = 2, West = 0, North = 3,
            Lb = 4, Rb = 5, LtButton = 6, RtButton = 7, LtAxis = 3, RtAxis = 4,
            Back = 8, Start = 9, L3 = 10, R3 = 11,
            Lx = 0, Ly = 1, Rx = 2, Ry = 5
        };

        return new Dictionary<string, BridgeProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["057E:2009"] = nintendo,
            ["057E:200E"] = nintendo,
            ["054C:05C2"] = sony,
            ["054C:09CC"] = sony,
            ["054C:0CE6"] = sony
        };
    }
}

internal sealed class RawBridgeFrame
{
    public int Index;
    public string Id = string.Empty;
    public string Vid = string.Empty;
    public string Pid = string.Empty;
    public float[] Axes = Array.Empty<float>();

    public bool[] Buttons = Array.Empty<bool>();

    public DateTime LastSeenUtc;
}

internal static class MappingResolver
{
    public static PadState Resolve(RawBridgeFrame frame, PadMapConfig? userMap)
    {
        BridgeProfile p = BridgeProfiles.Resolve(frame.Vid, frame.Pid);
        int dpadBase = frame.Buttons.Length - 4;

        var pad = new PadState
        {
            Connected = true,
            Source = PadSource.Bridge,
            DeviceId = $"{frame.Vid}:{frame.Pid}",
            DisplayName = frame.Id,

            South = ReadButton(frame, userMap, "south", p.South),
            East = ReadButton(frame, userMap, "east", p.East),
            West = ReadButton(frame, userMap, "west", p.West),
            North = ReadButton(frame, userMap, "north", p.North),
            LB = ReadButton(frame, userMap, "lb", p.Lb),
            RB = ReadButton(frame, userMap, "rb", p.Rb),
            Back = ReadButton(frame, userMap, "back", p.Back),
            Start = ReadButton(frame, userMap, "start", p.Start),
            L3 = ReadButton(frame, userMap, "l3", p.L3),
            R3 = ReadButton(frame, userMap, "r3", p.R3),

            LT = ReadTrigger(frame, userMap, "lt", p.LtButton, p.LtAxis),
            RT = ReadTrigger(frame, userMap, "rt", p.RtButton, p.RtAxis),

            DpadUp = ReadDpad(frame, userMap, "up", dpadBase + 0),
            DpadRight = ReadDpad(frame, userMap, "right", dpadBase + 1),
            DpadDown = ReadDpad(frame, userMap, "down", dpadBase + 2),
            DpadLeft = ReadDpad(frame, userMap, "left", dpadBase + 3),

            LX = ReadAxis(frame, userMap, "lx", p.Lx, p.LxSign),
            LY = ReadAxis(frame, userMap, "ly", p.Ly, p.LySign),
            RX = ReadAxis(frame, userMap, "rx", p.Rx, p.RxSign),
            RY = ReadAxis(frame, userMap, "ry", p.Ry, p.RySign)
        };

        return pad;
    }

    private static bool ReadButton(RawBridgeFrame frame, PadMapConfig? map, string name, int defaultIndex)
    {
        if (map is not null && map.Buttons.TryGetValue(name, out PadMapButtonEntry? entry))
        {
            if (entry.Index is int bi && bi >= 0 && bi < frame.Buttons.Length)
            {
                return frame.Buttons[bi];
            }
            if (entry.AxisIndex is int ai && ai >= 0 && ai < frame.Axes.Length)
            {
                return frame.Axes[ai] * entry.Sign > 0.5f;
            }
        }
        return defaultIndex >= 0 && defaultIndex < frame.Buttons.Length && frame.Buttons[defaultIndex];
    }

    private static float ReadTrigger(RawBridgeFrame frame, PadMapConfig? map, string name, int? defaultButton, int? defaultAxis)
    {
        if (map is not null && map.Buttons.TryGetValue(name, out PadMapButtonEntry? entry))
        {
            if (entry.AxisIndex is int ai && ai >= 0 && ai < frame.Axes.Length)
            {
                return Math.Clamp((frame.Axes[ai] * entry.Sign + 1f) / 2f, 0f, 1f);
            }
            if (entry.Index is int bi && bi >= 0 && bi < frame.Buttons.Length)
            {
                return frame.Buttons[bi] ? 1f : 0f;
            }
        }
        if (defaultAxis is int dai && dai >= 0 && dai < frame.Axes.Length)
        {
            return Math.Clamp((frame.Axes[dai] + 1f) / 2f, 0f, 1f);
        }
        if (defaultButton is int dbi && dbi >= 0 && dbi < frame.Buttons.Length)
        {
            return frame.Buttons[dbi] ? 1f : 0f;
        }
        return 0f;
    }

    private static bool ReadDpad(RawBridgeFrame frame, PadMapConfig? map, string name, int defaultBitIndex)
    {
        if (map is not null && map.Dpad.TryGetValue(name, out PadMapDpadEntry? entry))
        {
            if (entry.ButtonIndex is int bi && bi >= 0 && bi < frame.Buttons.Length)
            {
                return frame.Buttons[bi];
            }
            if (entry.AxisIndex is int ai && ai >= 0 && ai < frame.Axes.Length && entry.MatchValue is double mv)
            {
                double rest = entry.RestValue ?? 0.0;
                return Math.Abs(frame.Axes[ai] - mv) < 0.18 && Math.Abs(frame.Axes[ai] - rest) > 0.10;
            }
        }
        return defaultBitIndex >= 0 && defaultBitIndex < frame.Buttons.Length && frame.Buttons[defaultBitIndex];
    }

    private static float ReadAxis(RawBridgeFrame frame, PadMapConfig? map, string name, int defaultIndex, int defaultSign)
    {
        int index = defaultIndex;
        int sign = defaultSign;
        if (map is not null && map.Axes.TryGetValue(name, out PadMapAxisEntry? entry))
        {
            index = entry.Index;
            sign = entry.Sign;
        }
        if (index < 0 || index >= frame.Axes.Length)
        {
            return 0f;
        }
        return Math.Clamp(frame.Axes[index] * sign, -1f, 1f);
    }
}
