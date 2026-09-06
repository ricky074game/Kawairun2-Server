namespace KawaiRun2Launcher.Controller;

internal interface IOskHost
{
    bool IsOpen { get; }
    void Navigate(int dx, int dy);
    void Activate();
    void Backspace();
    void Submit();
    void Tab(bool backward);
    void CycleShift();
    void Toggle();
}

internal static class Hysteresis
{
    public static bool Update(bool current, float value, float pressThreshold)
    {
        float releaseThreshold = MathF.Max(0f, pressThreshold - 0.05f);
        if (!current && value >= pressThreshold)
        {
            return true;
        }
        if (current && value < releaseThreshold)
        {
            return false;
        }
        return current;
    }
}

internal sealed class RefCountedKeySet
{
    private readonly InputInjector _injector;
    private readonly Dictionary<string, int> _counts = new();

    private readonly Dictionary<(string SourceId, string Token), bool> _sourceTokenActive = new();

    public RefCountedKeySet(InputInjector injector)
    {
        _injector = injector;
    }

    public void SetSource(string sourceId, string? token, bool active)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        (string, string) key = (sourceId, token);
        bool wasActive = _sourceTokenActive.TryGetValue(key, out bool prev) && prev;

        if (active && !wasActive)
        {
            Increment(token);
        }
        else if (!active && wasActive)
        {
            Decrement(token);
        }

        _sourceTokenActive[key] = active;
    }

    private void Increment(string token)
    {
        int count = _counts.GetValueOrDefault(token) + 1;
        _counts[token] = count;
        if (count == 1)
        {
            _injector.KeyDown(token);
        }
    }

    private void Decrement(string token)
    {
        int count = Math.Max(0, _counts.GetValueOrDefault(token) - 1);
        _counts[token] = count;
        if (count == 0)
        {
            _injector.KeyUp(token);
        }
    }

    public void ReleaseAll()
    {
        foreach (KeyValuePair<string, int> kv in _counts.Where(kv => kv.Value > 0).ToList())
        {
            _injector.KeyUp(kv.Key, force: true);
        }
        _counts.Clear();
        _sourceTokenActive.Clear();
    }
}

internal sealed class ComboState
{
    public bool StartPendingTap;
    public DateTime StartPressUtc;
    public bool BackPendingTap;
    public DateTime BackPressUtc;
    public bool Engaged;
    public DateTime BothDownUtc;
    public bool Fired;
    public bool PrevStart;
    public bool PrevBack;
}

internal sealed class PadRuntime
{
    public RefCountedKeySet Keys = null!;
    public readonly Dictionary<string, bool> Hysteresis = new();
    public readonly ComboState Combo = new();
    public bool WasConnected;

    public bool PrevSouth, PrevEast, PrevWest, PrevNorth, PrevLb, PrevRb, PrevL3, PrevR3;

    public bool OskPrevUp, OskPrevDown, OskPrevLeft, OskPrevRight;
    public bool OskPrevSouth, OskPrevEast, OskPrevWest, OskPrevNorth, OskPrevLb, OskPrevRb, OskPrevStart;

    public bool OskWasOpen;

    public bool MouseButtonDown;
    public bool CursorSuspendedForRealMouse;
    public bool ControllerCursorSignaled;
    public bool HasLastSetCursorPos;
    public Native.POINT LastSetCursorPos;

    public bool HasLogicalCursorPos;
    public float LogicalCursorX;
    public float LogicalCursorY;
}

internal sealed class Mapper
{
    private const float TriggerThreshold = Native.XINPUT_GAMEPAD_TRIGGER_THRESHOLD / 255f;
    private const double ComboRecognitionWindowMs = 150.0;
    private const double ComboHoldMs = 1000.0;

    private readonly InputInjector _injector;
    private readonly Func<ControllerRootConfig> _configProvider;
    private readonly PadRuntime _p1 = new();
    private readonly PadRuntime _p2 = new();

    private readonly object _lock = new();
    private volatile bool _suspended;

    private volatile bool _releaseRequested;
    private bool _wasForeground;

    public event Action? OpenConfigRequested;

    public IOskHost? OskHost { get; set; }

    public Mapper(InputInjector injector, Func<ControllerRootConfig> configProvider)
    {
        _injector = injector;
        _configProvider = configProvider;
        _p1.Keys = new RefCountedKeySet(injector);
        _p2.Keys = new RefCountedKeySet(injector);
    }

    public void SetSuspended(bool suspended)
    {
        lock (_lock)
        {
            if (suspended == _suspended)
            {
                return;
            }
            _suspended = suspended;
            if (suspended)
            {
                _releaseRequested = true;
            }
        }
    }

    public void ReleaseAll(int padIndex)
    {
        lock (_lock)
        {
            PadRuntime rt = padIndex == 1 ? _p1 : _p2;
            rt.Keys.ReleaseAll();
            if (padIndex == 1 && rt.MouseButtonDown)
            {
                _injector.MouseUp(force: true);
                rt.MouseButtonDown = false;
            }
        }
    }

    public void Tick(PadState pad1, PadState pad2, double dtSeconds)
    {
        lock (_lock)
        {
            if (_releaseRequested)
            {

                _releaseRequested = false;
                ReleaseAll(1);
                ReleaseAll(2);
            }

            ControllerRootConfig config = _configProvider();
            if (!config.Enabled || _suspended)
            {
                return;
            }

            bool postMessage = string.Equals(config.InjectionMode, "postmessage", StringComparison.OrdinalIgnoreCase);

            bool foreground = postMessage ? _injector.ProjectorHwnd != IntPtr.Zero : _injector.IsProjectorForeground();
            if (!foreground)
            {
                if (_wasForeground)
                {
                    ReleaseAll(1);
                    ReleaseAll(2);
                }
                _wasForeground = false;
                return;
            }
            _wasForeground = true;

            ProcessPad1(pad1, config, dtSeconds);

            if (config.P2Enabled)
            {
                ProcessPad2(pad2, config);
            }
            else
            {
                ReleaseAll(2);
            }
        }
    }

    private void ProcessPad1(PadState pad, ControllerRootConfig config, double dtSeconds)
    {
        PadConfig cfg = config.Pads["1"];
        PadRuntime rt = _p1;

        if (!pad.Connected)
        {
            if (rt.WasConnected)
            {
                ReleaseAll(1);
            }
            rt.WasConnected = false;
            return;
        }
        rt.WasConnected = true;

        bool postMessage = string.Equals(config.InjectionMode, "postmessage", StringComparison.OrdinalIgnoreCase);

        bool oskOpenNow = OskHost is { IsOpen: true };
        if (oskOpenNow != rt.OskWasOpen)
        {

            rt.PrevSouth = pad.South;
            rt.PrevEast = pad.East;
            rt.PrevWest = pad.West;
            rt.PrevNorth = pad.North;
            rt.PrevLb = pad.LB;
            rt.PrevRb = pad.RB;
            rt.PrevL3 = pad.L3;
            rt.PrevR3 = pad.R3;
            rt.OskPrevUp = false;
            rt.OskPrevDown = false;
            rt.OskPrevLeft = false;
            rt.OskPrevRight = false;
            rt.OskPrevSouth = pad.South;
            rt.OskPrevEast = pad.East;
            rt.OskPrevWest = pad.West;
            rt.OskPrevNorth = pad.North;
            rt.OskPrevLb = pad.LB;
            rt.OskPrevRb = pad.RB;
            rt.OskPrevStart = pad.Start;

            if (oskOpenNow)
            {
                ReleaseAll(1);
            }
            rt.OskWasOpen = oskOpenNow;
        }

        if (OskHost is { IsOpen: true } host)
        {

            ProcessOsk(pad, rt, cfg, host);
            return;
        }

        bool swap = cfg.SwapAB;
        bool south = swap ? pad.East : pad.South;
        bool east = swap ? pad.South : pad.East;

        bool moveViaStick = cfg.MovementSource != "dpad";
        bool moveViaDpad = cfg.MovementSource != "stick";
        ProcessStickMovement("p1", rt, cfg.Bindings.LeftStick.FirstOrDefault(), pad.LX, pad.LY, (float)cfg.MovementDeadzone, moveViaStick);
        ProcessDpadMovement("p1", rt.Keys, cfg.Bindings.Dpad.FirstOrDefault(), pad, moveViaDpad);

        if (postMessage)
        {
            EnsureLogicalCursor(rt);
        }

        ProcessButtonBinding("p1_south", rt.Keys, cfg.Bindings.South, south, rt.PrevSouth, rt, postMessage);
        ProcessButtonBinding("p1_east", rt.Keys, cfg.Bindings.East, east, rt.PrevEast, rt, postMessage);
        ProcessButtonBinding("p1_west", rt.Keys, cfg.Bindings.West, pad.West, rt.PrevWest, null, postMessage);
        ProcessButtonBinding("p1_north", rt.Keys, cfg.Bindings.North, pad.North, rt.PrevNorth, null, postMessage);
        ProcessButtonBinding("p1_lb", rt.Keys, cfg.Bindings.Lb, pad.LB, rt.PrevLb, null, postMessage);
        ProcessButtonBinding("p1_rb", rt.Keys, cfg.Bindings.Rb, pad.RB, rt.PrevRb, null, postMessage);

        ProcessTriggerAsButton("p1_lt", rt, cfg.Bindings.Lt, pad.LT);
        if (!cfg.Bindings.Rt.Contains("PRECISION_MODIFIER"))
        {

            ProcessTriggerAsButton("p1_rt", rt, cfg.Bindings.Rt, pad.RT);
        }

        ProcessStartBackCombo(rt, pad, cfg.Bindings.Start, cfg.Bindings.Back);

        if (pad.L3 && !rt.PrevL3 && cfg.Bindings.L3.Contains("CURSOR_RECENTER"))
        {
            _injector.RecenterCursor();
            rt.HasLastSetCursorPos = false;
            rt.HasLogicalCursorPos = false;
        }
        if (pad.R3 && !rt.PrevR3 && cfg.Bindings.R3.Contains("TOGGLE_OSK") && config.OskEnabled)
        {
            OskHost?.Toggle();
        }

        ProcessCursor(pad, cfg, dtSeconds, rt, postMessage);

        rt.PrevSouth = south;
        rt.PrevEast = east;
        rt.PrevWest = pad.West;
        rt.PrevNorth = pad.North;
        rt.PrevLb = pad.LB;
        rt.PrevRb = pad.RB;
        rt.PrevL3 = pad.L3;
        rt.PrevR3 = pad.R3;
    }

    private void ProcessPad2(PadState pad, ControllerRootConfig config)
    {
        PadConfig cfg = config.Pads["2"];
        PadRuntime rt = _p2;

        if (!pad.Connected)
        {
            if (rt.WasConnected)
            {
                ReleaseAll(2);
            }
            rt.WasConnected = false;
            return;
        }
        rt.WasConnected = true;

        bool swap = cfg.SwapAB;
        bool south = swap ? pad.East : pad.South;
        bool east = swap ? pad.South : pad.East;

        bool moveViaStick = cfg.MovementSource != "dpad";
        bool moveViaDpad = cfg.MovementSource != "stick";
        ProcessStickMovement("p2", rt, cfg.Bindings.LeftStick.FirstOrDefault(), pad.LX, pad.LY, (float)cfg.MovementDeadzone, moveViaStick);
        ProcessDpadMovement("p2", rt.Keys, cfg.Bindings.Dpad.FirstOrDefault(), pad, moveViaDpad);

        ProcessButtonBinding("p2_south", rt.Keys, cfg.Bindings.South, south, rt.PrevSouth, null, false);
        ProcessButtonBinding("p2_east", rt.Keys, cfg.Bindings.East, east, rt.PrevEast, null, false);
        ProcessButtonBinding("p2_west", rt.Keys, cfg.Bindings.West, pad.West, rt.PrevWest, null, false);
        ProcessButtonBinding("p2_north", rt.Keys, cfg.Bindings.North, pad.North, rt.PrevNorth, null, false);

        ProcessButtonBinding("p2_lb", rt.Keys, cfg.Bindings.Lb, pad.LB, rt.PrevLb, null, false);
        ProcessButtonBinding("p2_rb", rt.Keys, cfg.Bindings.Rb, pad.RB, rt.PrevRb, null, false);

        rt.PrevSouth = south;
        rt.PrevEast = east;
        rt.PrevWest = pad.West;
        rt.PrevNorth = pad.North;
        rt.PrevLb = pad.LB;
        rt.PrevRb = pad.RB;
    }

    private static (string Up, string Down, string Left, string Right)? MovementTokens(string? moveToken) => moveToken switch
    {
        "MOVE_ARROWS" => ("ARROW_UP", "ARROW_DOWN", "ARROW_LEFT", "ARROW_RIGHT"),
        "MOVE_WASD" => ("W", "S", "A", "D"),
        _ => null
    };

    private static void ProcessStickMovement(string prefix, PadRuntime rt, string? moveToken, float lx, float ly, float deadzone, bool enabled)
    {
        (string Up, string Down, string Left, string Right)? dirs = MovementTokens(moveToken);
        string upSrc = $"{prefix}_stick_up", downSrc = $"{prefix}_stick_down", leftSrc = $"{prefix}_stick_left", rightSrc = $"{prefix}_stick_right";

        if (dirs is null || !enabled)
        {
            rt.Keys.SetSource(upSrc, null, false);
            rt.Keys.SetSource(downSrc, null, false);
            rt.Keys.SetSource(leftSrc, null, false);
            rt.Keys.SetSource(rightSrc, null, false);
            return;
        }

        bool up = Hysteresis.Update(rt.Hysteresis.GetValueOrDefault(upSrc), -ly, deadzone);
        bool down = Hysteresis.Update(rt.Hysteresis.GetValueOrDefault(downSrc), ly, deadzone);
        bool left = Hysteresis.Update(rt.Hysteresis.GetValueOrDefault(leftSrc), -lx, deadzone);
        bool right = Hysteresis.Update(rt.Hysteresis.GetValueOrDefault(rightSrc), lx, deadzone);
        rt.Hysteresis[upSrc] = up;
        rt.Hysteresis[downSrc] = down;
        rt.Hysteresis[leftSrc] = left;
        rt.Hysteresis[rightSrc] = right;

        rt.Keys.SetSource(upSrc, dirs.Value.Up, up);
        rt.Keys.SetSource(downSrc, dirs.Value.Down, down);
        rt.Keys.SetSource(leftSrc, dirs.Value.Left, left);
        rt.Keys.SetSource(rightSrc, dirs.Value.Right, right);
    }

    private static void ProcessDpadMovement(string prefix, RefCountedKeySet keys, string? moveToken, PadState pad, bool enabled)
    {
        (string Up, string Down, string Left, string Right)? dirs = MovementTokens(moveToken);
        string upSrc = $"{prefix}_dpad_up", downSrc = $"{prefix}_dpad_down", leftSrc = $"{prefix}_dpad_left", rightSrc = $"{prefix}_dpad_right";

        if (dirs is null || !enabled)
        {
            keys.SetSource(upSrc, null, false);
            keys.SetSource(downSrc, null, false);
            keys.SetSource(leftSrc, null, false);
            keys.SetSource(rightSrc, null, false);
            return;
        }

        keys.SetSource(upSrc, dirs.Value.Up, pad.DpadUp);
        keys.SetSource(downSrc, dirs.Value.Down, pad.DpadDown);
        keys.SetSource(leftSrc, dirs.Value.Left, pad.DpadLeft);
        keys.SetSource(rightSrc, dirs.Value.Right, pad.DpadRight);
    }

    private void ProcessButtonBinding(string sourceId, RefCountedKeySet keys, List<string> tokens, bool pressed, bool prevPressed, PadRuntime? clickTarget, bool postMessage)
    {
        bool justPressed = pressed && !prevPressed;
        bool justReleased = !pressed && prevPressed;

        foreach (string token in tokens)
        {
            switch (token)
            {
                case "CURSOR_CLICK":
                    if (clickTarget is not null)
                    {
                        if (justPressed)
                        {
                            if (postMessage)
                            {

                                _injector.PostMouseButton(true, (int)MathF.Round(clickTarget.LogicalCursorX), (int)MathF.Round(clickTarget.LogicalCursorY));
                            }
                            else
                            {
                                _injector.MouseDown();
                            }
                            clickTarget.MouseButtonDown = true;
                        }
                        else if (justReleased)
                        {
                            if (postMessage)
                            {
                                _injector.PostMouseButton(false, (int)MathF.Round(clickTarget.LogicalCursorX), (int)MathF.Round(clickTarget.LogicalCursorY));
                            }
                            else
                            {
                                _injector.MouseUp();
                            }
                            clickTarget.MouseButtonDown = false;
                        }
                    }
                    continue;
                case "CURSOR_RECENTER":
                case "TOGGLE_OSK":
                case "CURSOR_MOVE":
                case "MOVE_ARROWS":
                case "MOVE_WASD":
                case "PRECISION_MODIFIER":

                    continue;
            }

            if (!VkTable.Map.TryGetValue(token, out VkDef def))
            {
                continue;
            }

            if (def.Held)
            {
                keys.SetSource(sourceId, token, pressed);
            }
            else if (justPressed)
            {
                _injector.TapKey(token);
            }
        }
    }

    private void ProcessTriggerAsButton(string sourceId, PadRuntime rt, List<string> tokens, float analogValue)
    {
        bool prev = rt.Hysteresis.GetValueOrDefault(sourceId);
        bool state = Hysteresis.Update(prev, analogValue, TriggerThreshold);
        rt.Hysteresis[sourceId] = state;
        ProcessButtonBinding(sourceId, rt.Keys, tokens, state, prev, null, false);
    }

    private void ProcessStartBackCombo(PadRuntime rt, PadState pad, List<string> startTokens, List<string> backTokens)
    {
        ComboState c = rt.Combo;
        bool startDown = pad.Start;
        bool backDown = pad.Back;
        DateTime now = DateTime.UtcNow;

        if (startDown && !c.PrevStart)
        {
            c.StartPendingTap = true;
            c.StartPressUtc = now;
        }
        if (backDown && !c.PrevBack)
        {
            c.BackPendingTap = true;
            c.BackPressUtc = now;
        }

        if (startDown && backDown && !c.Engaged && !c.Fired)
        {

            c.StartPendingTap = false;
            c.BackPendingTap = false;
            c.Engaged = true;
            c.BothDownUtc = now;
        }

        if (c.Engaged)
        {
            if (!startDown || !backDown)
            {

                c.Engaged = false;
            }
            else if (!c.Fired && (now - c.BothDownUtc).TotalMilliseconds >= ComboHoldMs)
            {
                c.Fired = true;
                OpenConfigRequested?.Invoke();
            }
        }

        if (!startDown && !backDown)
        {
            c.Fired = false;
        }

        if (c.StartPendingTap && !c.Engaged && !backDown && (now - c.StartPressUtc).TotalMilliseconds >= ComboRecognitionWindowMs)
        {
            c.StartPendingTap = false;
            DispatchTap(startTokens);
        }
        if (c.BackPendingTap && !c.Engaged && !startDown && (now - c.BackPressUtc).TotalMilliseconds >= ComboRecognitionWindowMs)
        {
            c.BackPendingTap = false;
            DispatchTap(backTokens);
        }

        c.PrevStart = startDown;
        c.PrevBack = backDown;
    }

    private void DispatchTap(List<string> tokens)
    {
        foreach (string token in tokens)
        {
            if (VkTable.Map.TryGetValue(token, out VkDef def) && !def.Held)
            {
                _injector.TapKey(token);
            }
        }
    }

    private static float PrecisionRtValue(PadConfig cfg, PadState pad) =>
        cfg.Bindings.Rt.Contains("PRECISION_MODIFIER") ? pad.RT : 0f;

    private void EnsureLogicalCursor(PadRuntime rt)
    {
        if (rt.HasLogicalCursorPos)
        {
            return;
        }
        Native.RECT? rect = _injector.GetProjectorClientRectScreen();
        if (rect is null || rect.Value.Width <= 0 || rect.Value.Height <= 0)
        {
            return;
        }
        rt.LogicalCursorX = rect.Value.Width / 2f;
        rt.LogicalCursorY = rect.Value.Height / 2f;
        rt.HasLogicalCursorPos = true;
    }

    private void ProcessCursor(PadState pad, PadConfig cfg, double dtSeconds, PadRuntime rt, bool postMessage)
    {
        if (!cfg.Bindings.RightStick.Contains("CURSOR_MOVE"))
        {
            return;
        }

        float rx = pad.RX;
        float ry = cfg.InvertCursorY ? -pad.RY : pad.RY;

        float magnitude = MathF.Sqrt(rx * rx + ry * ry);
        float deadzone = (float)cfg.CursorDeadzone;
        float normMag = magnitude < deadzone ? 0f : Math.Clamp((magnitude - deadzone) / (1f - deadzone), 0f, 1f);

        if (postMessage)
        {

            ProcessCursorPostMessage(pad, rx, ry, magnitude, normMag, cfg, dtSeconds, rt);
            return;
        }

        Native.POINT? actual = _injector.GetCursorScreenPos();
        if (actual is not null && rt.HasLastSetCursorPos)
        {
            int dx0 = actual.Value.X - rt.LastSetCursorPos.X;
            int dy0 = actual.Value.Y - rt.LastSetCursorPos.Y;
            if (dx0 * dx0 + dy0 * dy0 > 16)
            {
                rt.CursorSuspendedForRealMouse = true;
                if (rt.ControllerCursorSignaled)
                {
                    _injector.TapKey("F14");
                    rt.ControllerCursorSignaled = false;
                }
            }
        }
        if (rt.CursorSuspendedForRealMouse && normMag > 0f)
        {
            rt.CursorSuspendedForRealMouse = false;
        }

        if (normMag <= 0f || rt.CursorSuspendedForRealMouse)
        {
            return;
        }

        Native.RECT? rectN = _injector.GetProjectorClientRectScreen();
        if (rectN is null || rectN.Value.Width <= 0 || rectN.Value.Height <= 0)
        {
            return;
        }
        Native.RECT rect = rectN.Value;

        if (!rt.ControllerCursorSignaled)
        {
            _injector.TapKey("F13");
            rt.ControllerCursorSignaled = true;
        }

        float curved = MathF.Pow(normMag, 1.6f);
        float precisionScale = Lerp(1f, (float)cfg.PrecisionMinScale, PrecisionRtValue(cfg, pad));
        float speed = cfg.CursorSpeed * precisionScale;

        float dirX = rx / magnitude;
        float dirY = ry / magnitude;

        float scaleX = rect.Width / 750f;
        float scaleY = rect.Height / 400f;
        float dxPixels = dirX * curved * speed * (float)dtSeconds * scaleX;
        float dyPixels = dirY * curved * speed * (float)dtSeconds * scaleY;

        Native.POINT current = actual ?? new Native.POINT { X = rect.Left, Y = rect.Top };
        int newX = Math.Clamp(current.X + (int)MathF.Round(dxPixels), rect.Left, rect.Right);
        int newY = Math.Clamp(current.Y + (int)MathF.Round(dyPixels), rect.Top, rect.Bottom);

        _injector.SetCursorScreenPos(newX, newY);
        rt.LastSetCursorPos = new Native.POINT { X = newX, Y = newY };
        rt.HasLastSetCursorPos = true;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private void ProcessCursorPostMessage(PadState pad, float rx, float ry, float magnitude, float normMag, PadConfig cfg, double dtSeconds, PadRuntime rt)
    {
        Native.RECT? rectN = _injector.GetProjectorClientRectScreen();
        if (rectN is null || rectN.Value.Width <= 0 || rectN.Value.Height <= 0)
        {
            return;
        }
        Native.RECT rect = rectN.Value;

        EnsureLogicalCursor(rt);
        if (!rt.HasLogicalCursorPos)
        {
            return;
        }

        if (normMag <= 0f)
        {
            return;
        }

        float curved = MathF.Pow(normMag, 1.6f);
        float precisionScale = Lerp(1f, (float)cfg.PrecisionMinScale, PrecisionRtValue(cfg, pad));
        float speed = cfg.CursorSpeed * precisionScale;

        float dirX = rx / magnitude;
        float dirY = ry / magnitude;

        float scaleX = rect.Width / 750f;
        float scaleY = rect.Height / 400f;
        float dxPixels = dirX * curved * speed * (float)dtSeconds * scaleX;
        float dyPixels = dirY * curved * speed * (float)dtSeconds * scaleY;

        rt.LogicalCursorX = Math.Clamp(rt.LogicalCursorX + dxPixels, 0f, rect.Width);
        rt.LogicalCursorY = Math.Clamp(rt.LogicalCursorY + dyPixels, 0f, rect.Height);

        _injector.PostMouseMove((int)MathF.Round(rt.LogicalCursorX), (int)MathF.Round(rt.LogicalCursorY));
    }

    private void ProcessOsk(PadState pad, PadRuntime rt, PadConfig cfg, IOskHost host)
    {
        float dz = (float)cfg.MovementDeadzone;

        bool stickUp = Hysteresis.Update(rt.Hysteresis.GetValueOrDefault("osk_su"), -pad.LY, dz);
        bool stickDown = Hysteresis.Update(rt.Hysteresis.GetValueOrDefault("osk_sd"), pad.LY, dz);
        bool stickLeft = Hysteresis.Update(rt.Hysteresis.GetValueOrDefault("osk_sl"), -pad.LX, dz);
        bool stickRight = Hysteresis.Update(rt.Hysteresis.GetValueOrDefault("osk_sr"), pad.LX, dz);
        rt.Hysteresis["osk_su"] = stickUp;
        rt.Hysteresis["osk_sd"] = stickDown;
        rt.Hysteresis["osk_sl"] = stickLeft;
        rt.Hysteresis["osk_sr"] = stickRight;

        bool navUp = stickUp || pad.DpadUp;
        bool navDown = stickDown || pad.DpadDown;
        bool navLeft = stickLeft || pad.DpadLeft;
        bool navRight = stickRight || pad.DpadRight;

        if (navUp && !rt.OskPrevUp)
        {
            host.Navigate(0, -1);
        }
        if (navDown && !rt.OskPrevDown)
        {
            host.Navigate(0, 1);
        }
        if (navLeft && !rt.OskPrevLeft)
        {
            host.Navigate(-1, 0);
        }
        if (navRight && !rt.OskPrevRight)
        {
            host.Navigate(1, 0);
        }
        rt.OskPrevUp = navUp;
        rt.OskPrevDown = navDown;
        rt.OskPrevLeft = navLeft;
        rt.OskPrevRight = navRight;

        if (pad.South && !rt.OskPrevSouth)
        {
            host.Activate();
        }
        if (pad.East && !rt.OskPrevEast)
        {
            host.Backspace();
        }
        if (pad.West && !rt.OskPrevWest)
        {
            host.Submit();
        }
        if (pad.LB && !rt.OskPrevLb)
        {
            host.Tab(backward: true);
        }
        if (pad.RB && !rt.OskPrevRb)
        {
            host.Tab(backward: false);
        }
        if (pad.North && !rt.OskPrevNorth)
        {
            host.CycleShift();
        }
        if ((pad.R3 && !rt.PrevR3) || (pad.Start && !rt.OskPrevStart))
        {
            host.Toggle();
        }

        rt.OskPrevSouth = pad.South;
        rt.OskPrevEast = pad.East;
        rt.OskPrevWest = pad.West;
        rt.OskPrevNorth = pad.North;
        rt.OskPrevLb = pad.LB;
        rt.OskPrevRb = pad.RB;
        rt.OskPrevStart = pad.Start;
        rt.PrevR3 = pad.R3;
    }
}
