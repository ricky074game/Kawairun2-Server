namespace KawaiRun2Launcher.Controller;

internal enum PadSource
{
    None,
    XInput,
    Bridge
}

internal sealed class PadState
{
    public bool Connected;
    public PadSource Source = PadSource.None;

    public string DeviceId = string.Empty;
    public string DisplayName = string.Empty;

    public bool South;
    public bool East;
    public bool West;
    public bool North;

    public bool LB;
    public bool RB;

    public float LT;

    public float RT;

    public bool Start;
    public bool Back;

    public bool L3;
    public bool R3;

    public bool DpadUp;
    public bool DpadDown;
    public bool DpadLeft;
    public bool DpadRight;

    public float LX;
    public float LY;
    public float RX;
    public float RY;

    public PadState Clone()
    {
        return (PadState)MemberwiseClone();
    }

    public static PadState Neutral() => new();
}
