namespace KawaiRun2Launcher.Controller;

internal enum DeviceStatus
{
    Available,

    Assigned,

    PendingIdentity,

    Duplicate,

    Unassignable
}

internal sealed class TrackedDevice
{
    public required string DeviceId;
    public required string DisplayName;
    public required PadSource Source;
    public required PadState State;
    public DeviceStatus Status;
    public int AssignedPad;
}

internal sealed class PadManager
{
    private const int RequiredMatchTicks = 8;
    private const float AxisMatchTolerance = 0.05f;

    private readonly XInputSource _xInput = new();
    private readonly BridgeSource? _bridge;
    private readonly object _publishLock = new();

    private PadState _pad1 = new();
    private PadState _pad2 = new();
    private List<TrackedDevice> _devices = new();

    private readonly Dictionary<string, int> _dedupMatchTicks = new();
    private readonly Dictionary<string, int> _dedupMatchedSlot = new();

    private readonly HashSet<string> _confirmedDuplicates = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, PadAssignmentConfig> _liveAssignments = new(StringComparer.OrdinalIgnoreCase);
    private bool _assignmentsSeeded;

    public Action<Action<ControllerRootConfig>>? Persist { get; set; }

    public event Action<int, string, string>? PadConnected;
    public event Action<int>? PadDisconnected;
    public event Action? StateChanged;

    public PadManager(string? bridgeTempDir)
    {
        if (OperatingSystem.IsWindows() && bridgeTempDir is not null)
        {
            _bridge = new BridgeSource(bridgeTempDir);
        }
    }

    public bool BridgeUnavailable => _bridge?.BridgeUnavailable ?? false;

    public void Start()
    {
        _bridge?.Start();
    }

    public void Stop()
    {
        _bridge?.Dispose();
    }

    public void Update(ControllerRootConfig config)
    {
        if (!_assignmentsSeeded)
        {

            foreach (KeyValuePair<string, PadAssignmentConfig> kv in config.Assignments)
            {
                _liveAssignments[kv.Key] = kv.Value;
            }
            _assignmentsSeeded = true;
        }

        PadState[] xPads = _xInput.PollAll();
        List<RawBridgeFrame> bridgeFrames = _bridge?.GetFrames() ?? new List<RawBridgeFrame>();

        if (_confirmedDuplicates.Count > 0)
        {

            HashSet<string> seenIds = new(bridgeFrames.Select(f => $"{f.Vid}:{f.Pid}".ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
            foreach (string staleId in _confirmedDuplicates.Where(id => !seenIds.Contains(id)).ToList())
            {
                _confirmedDuplicates.Remove(staleId);
                _dedupMatchTicks.Remove(staleId);
                _dedupMatchedSlot.Remove(staleId);
            }
        }

        List<TrackedDevice> devices = new();

        for (int slot = 0; slot < xPads.Length; slot++)
        {
            if (!xPads[slot].Connected)
            {
                continue;
            }
            devices.Add(new TrackedDevice
            {
                DeviceId = $"XINPUT:{slot}",
                DisplayName = xPads[slot].DisplayName,
                Source = PadSource.XInput,
                State = xPads[slot],
                Status = DeviceStatus.Available
            });
        }

        bool anyXInputConnected = devices.Count > 0;

        foreach (RawBridgeFrame frame in bridgeFrames)
        {
            string deviceId = $"{frame.Vid}:{frame.Pid}".ToUpperInvariant();

            if (string.Equals(frame.Vid, "045E", StringComparison.OrdinalIgnoreCase) && anyXInputConnected)
            {
                devices.Add(MakeBridgeDevice(frame, deviceId, config, DeviceStatus.Duplicate));
                continue;
            }

            List<int> eligibleSlots = new();
            List<int> pendingSlots = new();
            for (int slot = 0; slot < xPads.Length; slot++)
            {
                if (!xPads[slot].Connected)
                {
                    continue;
                }
                if (_xInput.HasEverBeenNonNeutral(slot))
                {
                    eligibleSlots.Add(slot);
                }
                else
                {
                    pendingSlots.Add(slot);
                }
            }

            PadMapConfig? padMap = ResolvePadMapForDevice(config, deviceId);
            PadState resolved = MappingResolver.Resolve(frame, padMap);

            if (_confirmedDuplicates.Contains(deviceId))
            {

                int slotIdx = _dedupMatchedSlot.GetValueOrDefault(deviceId, -1);
                if (slotIdx >= 0 && slotIdx < xPads.Length && xPads[slotIdx].Connected)
                {
                    devices.Add(MakeBridgeDevice(frame, deviceId, config, DeviceStatus.Duplicate, resolved));
                    continue;
                }

                _confirmedDuplicates.Remove(deviceId);
                _dedupMatchTicks.Remove(deviceId);
                _dedupMatchedSlot.Remove(deviceId);
            }

            if (pendingSlots.Count > 0 && eligibleSlots.Count == 0)
            {

                devices.Add(MakeBridgeDevice(frame, deviceId, config, DeviceStatus.PendingIdentity, resolved));
                continue;
            }

            bool isDuplicate = false;
            foreach (int slot in eligibleSlots)
            {
                if (StatesCorrelate(resolved, xPads[slot]))
                {
                    _dedupMatchTicks[deviceId] = _dedupMatchTicks.GetValueOrDefault(deviceId) + 1;
                    _dedupMatchedSlot[deviceId] = slot;
                    if (_dedupMatchTicks[deviceId] >= RequiredMatchTicks)
                    {
                        isDuplicate = true;
                        _confirmedDuplicates.Add(deviceId);
                    }
                    break;
                }
            }
            if (!isDuplicate)
            {

                if (_dedupMatchedSlot.TryGetValue(deviceId, out int lastSlot) &&
                    (!eligibleSlots.Contains(lastSlot) || !StatesCorrelate(resolved, xPads[lastSlot])))
                {
                    _dedupMatchTicks[deviceId] = 0;
                }
            }

            devices.Add(MakeBridgeDevice(frame, deviceId, config, isDuplicate ? DeviceStatus.Duplicate : DeviceStatus.Available, resolved));
        }

        AssignPads(devices, config);

        lock (_publishLock)
        {
            bool pad1WasConnected = _pad1.Connected;
            bool pad2WasConnected = _pad2.Connected;

            TrackedDevice? slot1 = devices.FirstOrDefault(d => d.AssignedPad == 1);
            TrackedDevice? slot2 = devices.FirstOrDefault(d => d.AssignedPad == 2);

            _pad1 = slot1?.State ?? new PadState();
            _pad2 = slot2?.State ?? new PadState();
            _devices = devices;

            if (_pad1.Connected && !pad1WasConnected)
            {
                PadConnected?.Invoke(1, _pad1.DeviceId, _pad1.DisplayName);
            }
            else if (!_pad1.Connected && pad1WasConnected)
            {
                PadDisconnected?.Invoke(1);
            }

            if (_pad2.Connected && !pad2WasConnected)
            {
                PadConnected?.Invoke(2, _pad2.DeviceId, _pad2.DisplayName);
            }
            else if (!_pad2.Connected && pad2WasConnected)
            {
                PadDisconnected?.Invoke(2);
            }
        }

        StateChanged?.Invoke();
    }

    private TrackedDevice MakeBridgeDevice(RawBridgeFrame frame, string deviceId, ControllerRootConfig config,
        DeviceStatus status, PadState? resolved = null)
    {
        PadState state = resolved ?? MappingResolver.Resolve(frame, ResolvePadMapForDevice(config, deviceId));
        return new TrackedDevice
        {
            DeviceId = deviceId,
            DisplayName = frame.Id,
            Source = PadSource.Bridge,
            State = state,
            Status = status
        };
    }

    private PadMapConfig? ResolvePadMapForDevice(ControllerRootConfig config, string deviceId)
    {
        foreach (KeyValuePair<string, PadAssignmentConfig> kv in _liveAssignments)
        {
            if (string.Equals(kv.Value.DeviceHint, deviceId, StringComparison.OrdinalIgnoreCase) &&
                config.Pads.TryGetValue(kv.Key, out PadConfig? padConfig) &&
                padConfig.PadMap is { IsEmpty: false })
            {
                return padConfig.PadMap;
            }
        }
        return null;
    }

    private static bool StatesCorrelate(PadState a, PadState b)
    {
        if (a.South != b.South || a.East != b.East || a.West != b.West || a.North != b.North ||
            a.LB != b.LB || a.RB != b.RB || a.Start != b.Start || a.Back != b.Back ||
            a.L3 != b.L3 || a.R3 != b.R3 ||
            a.DpadUp != b.DpadUp || a.DpadDown != b.DpadDown || a.DpadLeft != b.DpadLeft || a.DpadRight != b.DpadRight)
        {
            return false;
        }
        return MathF.Abs(a.LX - b.LX) <= AxisMatchTolerance &&
               MathF.Abs(a.LY - b.LY) <= AxisMatchTolerance &&
               MathF.Abs(a.RX - b.RX) <= AxisMatchTolerance &&
               MathF.Abs(a.RY - b.RY) <= AxisMatchTolerance &&
               MathF.Abs(a.LT - b.LT) <= AxisMatchTolerance &&
               MathF.Abs(a.RT - b.RT) <= AxisMatchTolerance;
    }

    private void AssignPads(List<TrackedDevice> devices, ControllerRootConfig config)
    {

        List<TrackedDevice> eligible = devices.Where(d => d.Status == DeviceStatus.Available).ToList();

        bool[] slotTaken = { false, false, false };
        string?[] previousDeviceId = { null, GetAssignmentDeviceId("1"), GetAssignmentDeviceId("2") };

        for (int slot = 1; slot <= 2; slot++)
        {
            string? hint = previousDeviceId[slot];
            if (hint is null)
            {
                continue;
            }
            TrackedDevice? match = eligible.FirstOrDefault(d => string.Equals(d.DeviceId, hint, StringComparison.OrdinalIgnoreCase) && d.AssignedPad == 0);
            if (match is not null)
            {
                match.AssignedPad = slot;
                match.Status = DeviceStatus.Assigned;
                slotTaken[slot] = true;
            }
        }

        foreach (TrackedDevice device in eligible)
        {
            if (device.AssignedPad != 0)
            {
                continue;
            }
            for (int slot = 1; slot <= 2; slot++)
            {
                if (!slotTaken[slot])
                {
                    device.AssignedPad = slot;
                    device.Status = DeviceStatus.Assigned;
                    slotTaken[slot] = true;
                    UpdateAssignment(slot, device);
                    break;
                }
            }
        }

        for (int slot = 1; slot <= 2; slot++)
        {
            TrackedDevice? assigned = eligible.FirstOrDefault(d => d.AssignedPad == slot);
            if (assigned is not null)
            {
                UpdateAssignment(slot, assigned);
            }
        }

        foreach (TrackedDevice device in devices)
        {
            if (device.AssignedPad == 0 && device.Status == DeviceStatus.Available)
            {
                device.Status = DeviceStatus.Unassignable;
            }
        }
    }

    private string? GetAssignmentDeviceId(string padKey) =>
        _liveAssignments.TryGetValue(padKey, out PadAssignmentConfig? a) && !string.IsNullOrEmpty(a.DeviceHint) ? a.DeviceHint : null;

    private bool UpdateAssignment(int slot, TrackedDevice device)
    {
        string key = slot.ToString();
        bool changed = !_liveAssignments.TryGetValue(key, out PadAssignmentConfig? existing) ||
                       existing.DeviceHint != device.DeviceId || existing.DisplayName != device.DisplayName;
        if (changed)
        {
            PadAssignmentConfig updated = new()
            {
                DeviceHint = device.DeviceId,
                DisplayName = device.DisplayName,
                Source = device.Source == PadSource.XInput ? "xinput" : "bridge"
            };
            _liveAssignments[key] = updated;
            Persist?.Invoke(cfg => cfg.Assignments[key] = new PadAssignmentConfig
            {
                DeviceHint = updated.DeviceHint,
                DisplayName = updated.DisplayName,
                Source = updated.Source
            });
        }
        return changed;
    }

    public PadState GetPad(int index)
    {
        lock (_publishLock)
        {
            return (index == 1 ? _pad1 : _pad2).Clone();
        }
    }

    public RawBridgeFrame? GetRawBridgeFrame(string deviceId)
    {
        if (_bridge is null)
        {
            return null;
        }
        foreach (RawBridgeFrame frame in _bridge.GetFrames())
        {
            if (string.Equals($"{frame.Vid}:{frame.Pid}", deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return frame;
            }
        }
        return null;
    }

    public List<TrackedDevice> GetDeviceList()
    {
        lock (_publishLock)
        {
            return _devices;
        }
    }

    public static string DescribeStatus(TrackedDevice d) => d.Status switch
    {
        DeviceStatus.Assigned => d.Source == PadSource.XInput
            ? $"{d.DisplayName} (XInput slot {(d.DeviceId.StartsWith("XINPUT:") ? int.Parse(d.DeviceId[7..]) + 1 : 0)})"
            : $"{d.DisplayName} (bridge)",
        DeviceStatus.PendingIdentity => "Awaiting input to confirm identity",
        DeviceStatus.Duplicate => "Two devices detected as one pad",
        DeviceStatus.Unassignable => "Unassigned controller detected",
        _ => "Not connected"
    };

    public static string? DescribeBridgeStatus(bool bridgeUnavailable) =>
        bridgeUnavailable ? "Controller bridge unavailable (restart the launcher)" : null;
}
