namespace KawaiRun2Launcher.Controller;

internal sealed class XInputSource
{
    private const int SlotCount = 4;
    private static readonly TimeSpan DisconnectedProbeInterval = TimeSpan.FromSeconds(1);

    private readonly bool[] _lastKnownConnected = new bool[SlotCount];
    private readonly DateTime[] _nextProbeUtc = new DateTime[SlotCount];

    private readonly bool[] _everNonNeutral = new bool[SlotCount];

    public XInputSource()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            _nextProbeUtc[i] = DateTime.MinValue;
        }
    }

    public PadState[] PollAll()
    {
        PadState[] result = new PadState[SlotCount];
        DateTime now = DateTime.UtcNow;

        for (int slot = 0; slot < SlotCount; slot++)
        {

            if (!_lastKnownConnected[slot] && now < _nextProbeUtc[slot])
            {
                result[slot] = new PadState { Connected = false, Source = PadSource.XInput };
                continue;
            }

            int hr = Native.XInputGetState((uint)slot, out Native.XINPUT_STATE state);
            if (hr != Native.ERROR_SUCCESS)
            {
                _lastKnownConnected[slot] = false;
                _nextProbeUtc[slot] = now + DisconnectedProbeInterval;
                _everNonNeutral[slot] = false;
                result[slot] = new PadState { Connected = false, Source = PadSource.XInput };
                continue;
            }

            _lastKnownConnected[slot] = true;
            PadState pad = Normalize(slot, state.Gamepad);
            if (HasNonNeutralInput(pad))
            {
                _everNonNeutral[slot] = true;
            }
            result[slot] = pad;
        }

        return result;
    }

    public bool HasEverBeenNonNeutral(int slot) => slot is >= 0 and < SlotCount && _everNonNeutral[slot];

    public bool IsConnected(int slot) => slot is >= 0 and < SlotCount && _lastKnownConnected[slot];

    private static bool HasNonNeutralInput(PadState p)
    {
        const float axisRest = 0.05f;
        return p.South || p.East || p.West || p.North || p.LB || p.RB || p.Start || p.Back ||
               p.L3 || p.R3 || p.DpadUp || p.DpadDown || p.DpadLeft || p.DpadRight ||
               p.LT > axisRest || p.RT > axisRest ||
               MathF.Abs(p.LX) > axisRest || MathF.Abs(p.LY) > axisRest ||
               MathF.Abs(p.RX) > axisRest || MathF.Abs(p.RY) > axisRest;
    }

    private static PadState Normalize(int slot, Native.XINPUT_GAMEPAD gp)
    {
        ushort b = gp.wButtons;
        var pad = new PadState
        {
            Connected = true,
            Source = PadSource.XInput,

            DeviceId = $"XINPUT:{slot}",
            DisplayName = $"XInput controller (slot {slot + 1})",

            South = (b & Native.XINPUT_GAMEPAD_A) != 0,
            East = (b & Native.XINPUT_GAMEPAD_B) != 0,
            West = (b & Native.XINPUT_GAMEPAD_X) != 0,
            North = (b & Native.XINPUT_GAMEPAD_Y) != 0,
            LB = (b & Native.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0,
            RB = (b & Native.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0,
            LT = gp.bLeftTrigger / 255f,
            RT = gp.bRightTrigger / 255f,
            Start = (b & Native.XINPUT_GAMEPAD_START) != 0,
            Back = (b & Native.XINPUT_GAMEPAD_BACK) != 0,
            L3 = (b & Native.XINPUT_GAMEPAD_LEFT_THUMB) != 0,
            R3 = (b & Native.XINPUT_GAMEPAD_RIGHT_THUMB) != 0,
            DpadUp = (b & Native.XINPUT_GAMEPAD_DPAD_UP) != 0,
            DpadDown = (b & Native.XINPUT_GAMEPAD_DPAD_DOWN) != 0,
            DpadLeft = (b & Native.XINPUT_GAMEPAD_DPAD_LEFT) != 0,
            DpadRight = (b & Native.XINPUT_GAMEPAD_DPAD_RIGHT) != 0,

            LX = NormalizeAxis(gp.sThumbLX),

            LY = -NormalizeAxis(gp.sThumbLY),
            RX = NormalizeAxis(gp.sThumbRX),
            RY = -NormalizeAxis(gp.sThumbRY)
        };
        return pad;
    }

    private static float NormalizeAxis(short raw)
    {

        float v = raw / 32767f;
        return Math.Clamp(v, -1f, 1f);
    }
}
