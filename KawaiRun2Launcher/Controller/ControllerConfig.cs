using System.Text.Json;
using System.Text.Json.Serialization;

namespace KawaiRun2Launcher.Controller;

internal static class BindingTokens
{
    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "ARROW_UP", "ARROW_DOWN", "ARROW_LEFT", "ARROW_RIGHT",
        "W", "A", "S", "D",
        "SPACE", "ENTER", "M", "P", "Z", "X", "C",
        "CURSOR_CLICK", "CURSOR_RECENTER", "TOGGLE_OSK",
        "CURSOR_MOVE", "MOVE_ARROWS", "MOVE_WASD", "PRECISION_MODIFIER"
    };

    public static bool IsValid(string? token) => token is null || Allowed.Contains(token);
}

internal sealed class JsonBindingValueConverter : JsonConverter<List<string>>
{
    public override bool HandleNull => true;

    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new List<string>();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? s = reader.GetString();
            return string.IsNullOrEmpty(s) ? new List<string>() : new List<string> { s };
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            List<string> list = new();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    string? s = reader.GetString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        list.Add(s);
                    }
                }
            }
            return list;
        }

        reader.Skip();
        return new List<string>();
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        if (value.Count == 0)
        {
            writer.WriteNullValue();
        }
        else if (value.Count == 1)
        {
            writer.WriteStringValue(value[0]);
        }
        else
        {
            writer.WriteStartArray();
            foreach (string token in value)
            {
                writer.WriteStringValue(token);
            }
            writer.WriteEndArray();
        }
    }
}

internal sealed class PadAssignmentConfig
{
    public string DeviceHint { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

internal sealed class BindingsConfig
{
    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> LeftStick { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> Dpad { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> RightStick { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> South { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> East { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> West { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> North { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> Lb { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> Rb { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> Lt { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> Rt { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> Start { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> Back { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> L3 { get; set; } = new();

    [JsonConverter(typeof(JsonBindingValueConverter))]
    public List<string> R3 { get; set; } = new();

    public static BindingsConfig DefaultPad1() => new()
    {
        LeftStick = new List<string> { "MOVE_ARROWS" },
        Dpad = new List<string> { "MOVE_ARROWS" },
        RightStick = new List<string> { "CURSOR_MOVE" },
        South = new List<string> { "ARROW_UP", "CURSOR_CLICK" },
        East = new List<string> { "ARROW_DOWN", "SPACE" },
        West = new List<string> { "ENTER" },
        North = new List<string> { "M" },
        Lb = new List<string> { "Z" },
        Rb = new List<string> { "X" },
        Lt = new List<string> { "C" },
        Rt = new List<string> { "PRECISION_MODIFIER" },
        Start = new List<string> { "P" },
        Back = new List<string>(),
        L3 = new List<string> { "CURSOR_RECENTER" },
        R3 = new List<string> { "TOGGLE_OSK" }
    };

    public static BindingsConfig DefaultPad2() => new()
    {
        LeftStick = new List<string> { "MOVE_WASD" },
        Dpad = new List<string> { "MOVE_WASD" },
        RightStick = new List<string>(),
        South = new List<string> { "W" },
        East = new List<string> { "S", "SPACE" },
        West = new List<string> { "ENTER" },
        North = new List<string> { "M" },
        Lb = new List<string>(),
        Rb = new List<string>(),
        Lt = new List<string>(),
        Rt = new List<string>(),
        Start = new List<string>(),
        Back = new List<string>(),
        L3 = new List<string>(),
        R3 = new List<string>()
    };

    public void Sanitize(BindingsConfig fallbackDefaults)
    {
        LeftStick = SanitizeField(LeftStick, fallbackDefaults.LeftStick);
        Dpad = SanitizeField(Dpad, fallbackDefaults.Dpad);
        RightStick = SanitizeField(RightStick, fallbackDefaults.RightStick);
        South = SanitizeField(South, fallbackDefaults.South);
        East = SanitizeField(East, fallbackDefaults.East);
        West = SanitizeField(West, fallbackDefaults.West);
        North = SanitizeField(North, fallbackDefaults.North);
        Lb = SanitizeField(Lb, fallbackDefaults.Lb);
        Rb = SanitizeField(Rb, fallbackDefaults.Rb);
        Lt = SanitizeField(Lt, fallbackDefaults.Lt);
        Rt = SanitizeField(Rt, fallbackDefaults.Rt);
        Start = SanitizeField(Start, fallbackDefaults.Start);
        Back = SanitizeField(Back, fallbackDefaults.Back);
        L3 = SanitizeField(L3, fallbackDefaults.L3);
        R3 = SanitizeField(R3, fallbackDefaults.R3);
    }

    private static List<string> SanitizeField(List<string>? value, List<string>? fallback)
    {
        fallback ??= new List<string>();
        if (value is null || value.Count == 0)
        {
            return new List<string>();
        }
        foreach (string token in value)
        {
            if (!BindingTokens.IsValid(token))
            {
                return new List<string>(fallback);
            }
        }
        return value;
    }
}

internal sealed class PadMapButtonEntry
{
    public int? Index { get; set; }

    public int? AxisIndex { get; set; }
    public int Sign { get; set; } = 1;
}

internal sealed class PadMapDpadEntry
{
    public int? ButtonIndex { get; set; }
    public int? AxisIndex { get; set; }
    public double? MatchValue { get; set; }
    public double? RestValue { get; set; }
}

internal sealed class PadMapAxisEntry
{
    public int Index { get; set; }
    public int Sign { get; set; } = 1;
}

internal sealed class PadMapConfig
{
    public Dictionary<string, PadMapButtonEntry> Buttons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PadMapDpadEntry> Dpad { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PadMapAxisEntry> Axes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => Buttons.Count == 0 && Dpad.Count == 0 && Axes.Count == 0;
}

internal sealed class PadConfig
{
    public bool SwapAB { get; set; }
    public string ControllerTypeLabel { get; set; } = "auto";
    public bool InvertCursorY { get; set; }
    public string MovementSource { get; set; } = "both";
    public int CursorSpeed { get; set; } = 500;
    public double CursorDeadzone { get; set; } = 8689.0 / 32767.0;
    public double MovementDeadzone { get; set; } = 7849.0 / 32767.0;
    public double PrecisionMinScale { get; set; } = 0.3;
    public BindingsConfig Bindings { get; set; } = new();
    public PadMapConfig? PadMap { get; set; }

    public void Sanitize(bool isPad1)
    {
        BindingsConfig defaults = isPad1 ? BindingsConfig.DefaultPad1() : BindingsConfig.DefaultPad2();

        if (ControllerTypeLabel is not ("auto" or "xbox" or "switch" or "playstation" or "generic"))
        {
            ControllerTypeLabel = "auto";
        }
        if (MovementSource is not ("both" or "stick" or "dpad"))
        {
            MovementSource = "both";
        }
        CursorSpeed = Math.Clamp(CursorSpeed, 200, 1200);
        CursorDeadzone = Math.Clamp(CursorDeadzone, 0.10, 0.40);
        MovementDeadzone = Math.Clamp(MovementDeadzone, 0.10, 0.40);
        PrecisionMinScale = Math.Clamp(PrecisionMinScale, 0.1, 1.0);

        Bindings.Sanitize(defaults);

        if (PadMap is not null)
        {
            SanitizePadMap();
        }
    }

    private void SanitizePadMap()
    {
        if (PadMap is null)
        {
            return;
        }

        foreach (string key in PadMap.Buttons.Keys.ToList())
        {
            PadMapButtonEntry e = PadMap.Buttons[key];
            bool valid = (e.Index is >= 0 and < 64) ^ (e.AxisIndex is >= 0 and < 16);
            if (!valid || e.Sign is not (1 or -1))
            {
                PadMap.Buttons.Remove(key);
            }
        }
        foreach (string key in PadMap.Dpad.Keys.ToList())
        {
            PadMapDpadEntry e = PadMap.Dpad[key];
            bool hasBtn = e.ButtonIndex is >= 0 and < 64;
            bool hasAxis = e.AxisIndex is >= 0 and < 16;
            if (!(hasBtn ^ hasAxis))
            {
                PadMap.Dpad.Remove(key);
            }
        }
        foreach (string key in PadMap.Axes.Keys.ToList())
        {
            PadMapAxisEntry e = PadMap.Axes[key];
            if (e.Index is < 0 or >= 16 || e.Sign is not (1 or -1))
            {
                PadMap.Axes.Remove(key);
            }
        }
    }
}

internal sealed class ControllerRootConfig
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool P2Enabled { get; set; } = true;
    public bool OskEnabled { get; set; } = true;

    public string InjectionMode { get; set; } = "sendinput";

    public Dictionary<string, PadAssignmentConfig> Assignments { get; set; } = new();
    public Dictionary<string, PadConfig> Pads { get; set; } = new();

    public static ControllerRootConfig Default()
    {
        return new ControllerRootConfig
        {
            Pads = new Dictionary<string, PadConfig>
            {
                ["1"] = new PadConfig { Bindings = BindingsConfig.DefaultPad1() },
                ["2"] = new PadConfig { Bindings = BindingsConfig.DefaultPad2() }
            }
        };
    }

    public void Sanitize()
    {
        if (SchemaVersion < 1)
        {
            SchemaVersion = 1;
        }
        if (InjectionMode is not ("sendinput" or "postmessage"))
        {
            InjectionMode = "sendinput";
        }
        if (!Pads.ContainsKey("1"))
        {
            Pads["1"] = new PadConfig { Bindings = BindingsConfig.DefaultPad1() };
        }
        if (!Pads.ContainsKey("2"))
        {
            Pads["2"] = new PadConfig { Bindings = BindingsConfig.DefaultPad2() };
        }
        Pads["1"].Sanitize(isPad1: true);
        Pads["2"].Sanitize(isPad1: false);
    }
}

internal static class ControllerConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static string GetConfigPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KawaiRun2Launcher");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "controller.json");
    }

    public static ControllerRootConfig Load()
    {
        string path = GetConfigPath();
        ControllerRootConfig config;
        try
        {
            if (!File.Exists(path))
            {
                config = ControllerRootConfig.Default();
            }
            else
            {
                string text = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<ControllerRootConfig>(text, JsonOptions) ?? ControllerRootConfig.Default();
            }
        }
        catch
        {
            config = ControllerRootConfig.Default();
        }

        config.Sanitize();
        return config;
    }

    public static ControllerRootConfig Clone(ControllerRootConfig config)
    {
        string text = JsonSerializer.Serialize(config, JsonOptions);
        ControllerRootConfig copy = JsonSerializer.Deserialize<ControllerRootConfig>(text, JsonOptions) ?? ControllerRootConfig.Default();
        copy.Sanitize();
        return copy;
    }

    public static void Save(ControllerRootConfig config)
    {
        try
        {
            config.Sanitize();
            string path = GetConfigPath();
            string text = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(path, text);
        }
        catch
        {

        }
    }
}
