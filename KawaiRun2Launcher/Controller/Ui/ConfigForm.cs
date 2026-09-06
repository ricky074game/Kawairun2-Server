using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace KawaiRun2Launcher.Controller.Ui;

internal sealed class ConfigForm : Form
{
    private readonly ControllerService _service;
    private int _currentPad = 1;

    private readonly TabControl _tabs;
    private readonly PictureBox _picTop;
    private readonly PictureBox _picFront;
    private readonly Panel _diagramFront;
    private readonly Panel _diagramTop;
    private readonly Label _statusLabel;
    private readonly Label _captureStatusLabel;
    private readonly ComboBox _typeCombo;
    private readonly DataGridView _grid;

    private readonly CheckBox _swapAbChk;
    private readonly CheckBox _invertYChk;
    private readonly ComboBox _movementSourceCombo;
    private readonly TrackBar _cursorSpeedTb;
    private readonly Label _cursorSpeedVal;
    private readonly TrackBar _cursorDeadzoneTb;
    private readonly Label _cursorDeadzoneVal;
    private readonly TrackBar _movementDeadzoneTb;
    private readonly Label _movementDeadzoneVal;
    private readonly TrackBar _precisionTb;
    private readonly Label _precisionVal;

    private readonly CheckBox _globalEnabledChk;
    private readonly CheckBox _p2EnabledChk;
    private readonly CheckBox _oskEnabledChk;
    private readonly ComboBox _injectionModeCombo;

    private readonly TabPage _pad1Tab;
    private readonly TabPage _pad2Tab;
    private readonly Panel _optionsPanel;
    private readonly Label _pad2Note;
    private readonly Dictionary<string, MarkerDot> _markers = new();
    private readonly System.Windows.Forms.Timer _liveTimer;
    private readonly System.Windows.Forms.Timer _captureTimer;
    private readonly System.Windows.Forms.Timer _captureMessageTimer;

    private bool _suppressEvents;
    private bool _closingHandled;
    private CaptureSession? _capture;

    public ConfigForm(ControllerService service)
    {
        _service = service;

        Text = "Configure Controller";
        ClientSize = new Size(980, 660);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        Panel left = new() { Location = new Point(10, 10), Size = new Size(540, 600) };
        Controls.Add(left);

        _statusLabel = new Label { Location = new Point(0, 0), Size = new Size(540, 20), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        left.Controls.Add(_statusLabel);

        _captureStatusLabel = new Label
        {
            Location = new Point(0, 20),
            Size = new Size(540, 34),
            ForeColor = Color.DarkOrange,
            Font = new Font("Segoe UI", 8.5f)
        };
        left.Controls.Add(_captureStatusLabel);

        (_diagramTop, _picTop) = BuildDiagram("x360/xboxControllerTop.png", new Size(512, 210), new Point(14, 60));
        left.Controls.Add(_diagramTop);
        (_diagramFront, _picFront) = BuildDiagram("x360/xboxControllerFront.png", new Size(512, 352), new Point(14, 280));
        left.Controls.Add(_diagramFront);

        AddMarker(_diagramTop, "lt", 126, 54);
        AddMarker(_diagramTop, "rt", 386, 54);
        AddMarker(_diagramTop, "lb", 86, 132);
        AddMarker(_diagramTop, "rb", 426, 132);

        AddMarker(_diagramFront, "leftStick", 118, 94);
        AddMarker(_diagramFront, "rightStick", 320, 176);
        AddMarker(_diagramFront, "dpadUp", 184, 150);
        AddMarker(_diagramFront, "dpadDown", 184, 202);
        AddMarker(_diagramFront, "dpadLeft", 158, 176);
        AddMarker(_diagramFront, "dpadRight", 210, 176);
        AddMarker(_diagramFront, "south", 392, 132);
        AddMarker(_diagramFront, "east", 430, 96);
        AddMarker(_diagramFront, "west", 356, 96);
        AddMarker(_diagramFront, "north", 392, 58);
        AddMarker(_diagramFront, "start", 304, 96);
        AddMarker(_diagramFront, "back", 250, 96);

        Label swapNote = new()
        {
            Location = new Point(0, 640 - 8),
            Size = new Size(540, 16),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5f),
            Text = "Note: L3/R3 (stick clicks) share the stick markers above; record them from the bindings table."
        };
        left.Controls.Add(swapNote);

        _tabs = new TabControl { Location = new Point(560, 10), Size = new Size(410, 560) };
        Controls.Add(_tabs);

        TabPage pad1Tab = new("Pad 1");
        TabPage pad2Tab = new("Pad 2");
        TabPage globalTab = new("Global");
        _pad1Tab = pad1Tab;
        _pad2Tab = pad2Tab;
        _tabs.TabPages.Add(pad1Tab);
        _tabs.TabPages.Add(pad2Tab);
        _tabs.TabPages.Add(globalTab);
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            CancelCapture(announce: false);
            bool isPad2 = _tabs.SelectedTab == _pad2Tab;
            if (isPad2 || _tabs.SelectedTab == _pad1Tab)
            {
                _currentPad = isPad2 ? 2 : 1;
                TabPage target = isPad2 ? _pad2Tab : _pad1Tab;

                _optionsPanel.Parent = target;
                _optionsPanel.Location = new Point(0, 0);
                _grid.Parent = target;
                _grid.Location = new Point(0, 308);
                RefreshFromConfig();
            }
        };

        _typeCombo = new ComboBox { Location = new Point(10, 10), Size = new Size(180, 22), DropDownStyle = ComboBoxStyle.DropDownList };
        _typeCombo.Items.AddRange(new object[] { "auto", "xbox", "switch", "playstation", "generic" });
        _typeCombo.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) { UpdatePad(p => p.ControllerTypeLabel = (string)_typeCombo.SelectedItem!); RefreshGlyphLabels(); } };
        Label typeLbl = new() { Location = new Point(10, 36), Size = new Size(180, 16), Text = "Controller type label (cosmetic)", Font = new Font("Segoe UI", 7.5f) };

        _swapAbChk = new CheckBox { Location = new Point(200, 8), Size = new Size(200, 22), Text = "Swap A/B (South/East)" };
        _swapAbChk.CheckedChanged += (_, _) => { if (!_suppressEvents) UpdatePad(p => p.SwapAB = _swapAbChk.Checked); };

        _invertYChk = new CheckBox { Location = new Point(200, 32), Size = new Size(200, 22), Text = "Invert cursor Y (Pad 1)" };
        _invertYChk.CheckedChanged += (_, _) => { if (!_suppressEvents) UpdatePad(p => p.InvertCursorY = _invertYChk.Checked); };

        Label movLbl = new() { Location = new Point(10, 62), Size = new Size(120, 20), Text = "Movement source:" };
        _movementSourceCombo = new ComboBox { Location = new Point(130, 60), Size = new Size(100, 22), DropDownStyle = ComboBoxStyle.DropDownList };
        _movementSourceCombo.Items.AddRange(new object[] { "both", "stick", "dpad" });
        _movementSourceCombo.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) UpdatePad(p => p.MovementSource = (string)_movementSourceCombo.SelectedItem!); };

        (_cursorSpeedTb, _cursorSpeedVal) = MakeSlider(new Point(10, 92), "Cursor speed (px/s)", 200, 1200, v => UpdatePad(p => p.CursorSpeed = v));
        (_cursorDeadzoneTb, _cursorDeadzoneVal) = MakeSliderPercent(new Point(10, 140), "Cursor deadzone", 10, 40, v => UpdatePad(p => p.CursorDeadzone = v / 100.0));
        (_movementDeadzoneTb, _movementDeadzoneVal) = MakeSliderPercent(new Point(10, 188), "Movement deadzone", 10, 40, v => UpdatePad(p => p.MovementDeadzone = v / 100.0));
        (_precisionTb, _precisionVal) = MakeSliderPercent(new Point(10, 236), "RT precision min scale", 10, 100, v => UpdatePad(p => p.PrecisionMinScale = v / 100.0));

        _pad2Note = new Label
        {
            Location = new Point(0, 284), Size = new Size(390, 18), Font = new Font("Segoe UI", 7.5f), ForeColor = Color.DarkOrange,
            Text = "Pad 2 also moves Player 1 outside of local co-op."
        };
        _optionsPanel = new Panel { Location = new Point(0, 0), Size = new Size(390, 304) };
        _optionsPanel.Controls.AddRange(new Control[]
        {
            _typeCombo, typeLbl, _swapAbChk, _invertYChk, movLbl, _movementSourceCombo,
            _cursorSpeedTb.Parent!, _cursorDeadzoneTb.Parent!, _movementDeadzoneTb.Parent!, _precisionTb.Parent!, _pad2Note
        });
        pad1Tab.Controls.Add(_optionsPanel);
        _grid = BuildBindingsGrid();
        _grid.Location = new Point(0, 308);
        _grid.Size = new Size(390, 240);
        pad1Tab.Controls.Add(_grid);

        _globalEnabledChk = new CheckBox { Location = new Point(10, 10), Size = new Size(300, 22), Text = "Enable controller support" };
        _globalEnabledChk.CheckedChanged += (_, _) => { if (!_suppressEvents) _service.UpdateConfig(c => c.Enabled = _globalEnabledChk.Checked); };
        _p2EnabledChk = new CheckBox { Location = new Point(10, 34), Size = new Size(300, 22), Text = "Enable Pad 2 (WASD co-op)" };
        _p2EnabledChk.CheckedChanged += (_, _) => { if (!_suppressEvents) _service.UpdateConfig(c => c.P2Enabled = _p2EnabledChk.Checked); };
        _oskEnabledChk = new CheckBox { Location = new Point(10, 58), Size = new Size(300, 22), Text = "Enable on-screen keyboard (Pad 1, R3)" };
        _oskEnabledChk.CheckedChanged += (_, _) => { if (!_suppressEvents) _service.UpdateConfig(c => c.OskEnabled = _oskEnabledChk.Checked); };

        Label injLbl = new() { Location = new Point(10, 88), Size = new Size(120, 20), Text = "Injection mode:" };
        _injectionModeCombo = new ComboBox { Location = new Point(130, 86), Size = new Size(140, 22), DropDownStyle = ComboBoxStyle.DropDownList };
        _injectionModeCombo.Items.AddRange(new object[] { "sendinput", "postmessage" });
        _injectionModeCombo.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) _service.UpdateConfig(c => c.InjectionMode = (string)_injectionModeCombo.SelectedItem!); };

        globalTab.Controls.AddRange(new Control[] { _globalEnabledChk, _p2EnabledChk, _oskEnabledChk, injLbl, _injectionModeCombo });

        Button swapPadsBtn = new() { Location = new Point(560, 580), Size = new Size(140, 28), Text = "Swap Pad 1 / Pad 2" };
        swapPadsBtn.Click += (_, _) => SwapPads();
        new ToolTip().SetToolTip(swapPadsBtn, "Swaps which physical controller is Pad 1 vs Pad 2. Each pad's own behaviour (bindings, cursor/OSK on Pad 1, WASD co-op on Pad 2) stays put.");
        Button resetBtn = new() { Location = new Point(710, 580), Size = new Size(130, 28), Text = "Reset to defaults" };
        resetBtn.Click += (_, _) => ResetCurrentPadToDefaults();
        Button clearMapBtn = new() { Location = new Point(850, 580), Size = new Size(120, 28), Text = "Clear mapping" };
        clearMapBtn.Click += (_, _) => ClearRecordedMapping();
        Button closeBtn = new() { Location = new Point(850, 616), Size = new Size(120, 28), Text = "Close" };
        closeBtn.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { swapPadsBtn, resetBtn, clearMapBtn, closeBtn });

        _liveTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _liveTimer.Tick += (_, _) => RefreshLiveVisualizer();

        _captureTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _captureTimer.Tick += (_, _) => CaptureTick();

        _captureMessageTimer = new System.Windows.Forms.Timer { Interval = 1600 };
        _captureMessageTimer.Tick += (_, _) =>
        {
            if (IsDisposed)
            {
                return;
            }
            _captureMessageTimer.Stop();
            _captureStatusLabel.Text = string.Empty;
        };

        Load += (_, _) =>
        {
            _service.SuspendInjection(true);
            RefreshFromConfig();
            _liveTimer.Start();
        };
        FormClosing += (_, _) => HandleClosingCleanup();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && _capture is not null) { CancelCapture(announce: true); e.Handled = true; } };
    }

    private void HandleClosingCleanup()
    {
        if (_closingHandled)
        {
            return;
        }
        _closingHandled = true;
        CancelCapture(announce: false);
        _liveTimer.Stop();
        _captureTimer.Stop();
        _captureMessageTimer.Stop();
        _service.SuspendInjection(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            HandleClosingCleanup();
            _liveTimer.Dispose();
            _captureTimer.Dispose();
            _captureMessageTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private static (Panel container, PictureBox pic) BuildDiagram(string resourceName, Size size, Point location)
    {
        Panel container = new() { Location = location, Size = size };
        PictureBox pic = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage, Image = LoadEmbeddedImage(resourceName) };
        container.Controls.Add(pic);
        return (container, pic);
    }

    private static Image? LoadEmbeddedImage(string resourceName)
    {
        try
        {
            using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            return s is null ? null : Image.FromStream(s);
        }
        catch
        {
            return null;
        }
    }

    private void AddMarker(Panel parent, string key, int x, int y)
    {
        MarkerDot dot = new() { Location = new Point(x - 8, y - 8) };
        dot.Click += (_, _) => BeginCaptureForKey(key);
        var tip = new ToolTip();
        tip.SetToolTip(dot, key);
        parent.Controls.Add(dot);
        dot.BringToFront();
        _markers[key] = dot;
    }

    private (TrackBar, Label) MakeSlider(Point loc, string label, int min, int max, Action<int> onChange)
    {
        Panel row = new() { Location = loc, Size = new Size(390, 44) };
        Label lbl = new() { Location = new Point(0, 0), Size = new Size(220, 16), Text = label, Font = new Font("Segoe UI", 7.5f) };
        Label val = new() { Location = new Point(300, 0), Size = new Size(80, 16), Font = new Font("Segoe UI", 7.5f) };
        TrackBar tb = new() { Location = new Point(0, 16), Size = new Size(380, 26), Minimum = min, Maximum = max, TickStyle = TickStyle.None };
        tb.ValueChanged += (_, _) => { val.Text = tb.Value.ToString(); if (!_suppressEvents) onChange(tb.Value); };
        row.Controls.AddRange(new Control[] { lbl, val, tb });
        return (tb, val);
    }

    private (TrackBar, Label) MakeSliderPercent(Point loc, string label, int minPct, int maxPct, Action<int> onChange) =>
        MakeSlider(loc, label + " (%)", minPct, maxPct, onChange);

    private DataGridView BuildBindingsGrid()
    {
        DataGridView grid = new()
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            Font = new Font("Segoe UI", 8f)
        };

        DataGridViewTextBoxColumn elementCol = new() { HeaderText = "Element", Width = 110, ReadOnly = true };
        DataGridViewComboBoxColumn primaryCol = new() { HeaderText = "Primary", Width = 100, FlatStyle = FlatStyle.Flat };
        DataGridViewComboBoxColumn secondaryCol = new() { HeaderText = "Secondary", Width = 100, FlatStyle = FlatStyle.Flat };
        DataGridViewButtonColumn recordCol = new() { HeaderText = "", Width = 70, Text = "Record", UseColumnTextForButtonValue = true };

        foreach (string t in TokenChoices())
        {
            primaryCol.Items.Add(t);
            secondaryCol.Items.Add(t);
        }

        grid.Columns.AddRange(elementCol, primaryCol, secondaryCol, recordCol);

        foreach (ElementRow row in ElementRows)
        {
            int idx = grid.Rows.Add(row.Label, "(none)", "(none)", "Record");
            grid.Rows[idx].Tag = row;
            if (!row.HasAction)
            {
                grid.Rows[idx].Cells[1].ReadOnly = true;
                grid.Rows[idx].Cells[2].ReadOnly = true;
                grid.Rows[idx].Cells[1].Style.BackColor = Color.Gainsboro;
                grid.Rows[idx].Cells[2].Style.BackColor = Color.Gainsboro;
            }
            if (row.RawKind == "none")
            {
                grid.Rows[idx].Cells[3].ReadOnly = true;
                grid.Rows[idx].Cells[3].Style.BackColor = Color.Gainsboro;
            }
        }

        grid.CellValueChanged += (_, e) =>
        {
            if (_suppressEvents || e.RowIndex < 0 || e.ColumnIndex is not (1 or 2))
            {
                return;
            }
            CommitBindingRow((ElementRow)grid.Rows[e.RowIndex].Tag!, grid.Rows[e.RowIndex]);
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 3)
            {
                return;
            }
            ElementRow row = (ElementRow)grid.Rows[e.RowIndex].Tag!;
            if (row.RawKind != "none")
            {
                BeginCaptureForKey(row.Key);
            }
        };

        return grid;
    }

    private static IEnumerable<string> TokenChoices()
    {
        yield return "(none)";
        foreach (string t in BindingTokens.Allowed)
        {
            yield return t;
        }
    }

    private void CommitBindingRow(ElementRow row, DataGridViewRow gridRow)
    {
        string primary = gridRow.Cells[1].Value as string ?? "(none)";
        string secondary = gridRow.Cells[2].Value as string ?? "(none)";
        List<string> tokens = new();
        if (primary != "(none)")
        {
            tokens.Add(primary);
        }
        if (secondary != "(none)" && secondary != primary)
        {
            tokens.Add(secondary);
        }
        UpdatePad(p => SetBindingField(p.Bindings, row.Key, tokens));
    }

    private sealed record ElementRow(string Key, string Label, bool HasAction, string RawKind);

    private static readonly ElementRow[] ElementRows =
    {
        new("leftStick", "Left Stick", true, "stick"),
        new("rightStick", "Right Stick", true, "stick"),
        new("dpad", "D-Pad (output)", true, "none"),
        new("dpadUp", "D-Pad up (raw)", false, "dpadDir"),
        new("dpadDown", "D-Pad down (raw)", false, "dpadDir"),
        new("dpadLeft", "D-Pad left (raw)", false, "dpadDir"),
        new("dpadRight", "D-Pad right (raw)", false, "dpadDir"),
        new("south", "South", true, "button"),
        new("east", "East", true, "button"),
        new("west", "West", true, "button"),
        new("north", "North", true, "button"),
        new("lb", "Left Bumper", true, "button"),
        new("rb", "Right Bumper", true, "button"),
        new("lt", "Left Trigger", true, "button"),
        new("rt", "Right Trigger", true, "button"),
        new("start", "Start", true, "button"),
        new("back", "Back", true, "button"),
        new("l3", "L3 (stick click)", true, "button"),
        new("r3", "R3 (stick click)", true, "button")
    };

    private static List<string> GetBindingField(BindingsConfig b, string key) => key switch
    {
        "leftStick" => b.LeftStick,
        "dpad" => b.Dpad,
        "rightStick" => b.RightStick,
        "south" => b.South,
        "east" => b.East,
        "west" => b.West,
        "north" => b.North,
        "lb" => b.Lb,
        "rb" => b.Rb,
        "lt" => b.Lt,
        "rt" => b.Rt,
        "start" => b.Start,
        "back" => b.Back,
        "l3" => b.L3,
        "r3" => b.R3,
        _ => new List<string>()
    };

    private static void SetBindingField(BindingsConfig b, string key, List<string> value)
    {
        switch (key)
        {
            case "leftStick": b.LeftStick = value; break;
            case "dpad": b.Dpad = value; break;
            case "rightStick": b.RightStick = value; break;
            case "south": b.South = value; break;
            case "east": b.East = value; break;
            case "west": b.West = value; break;
            case "north": b.North = value; break;
            case "lb": b.Lb = value; break;
            case "rb": b.Rb = value; break;
            case "lt": b.Lt = value; break;
            case "rt": b.Rt = value; break;
            case "start": b.Start = value; break;
            case "back": b.Back = value; break;
            case "l3": b.L3 = value; break;
            case "r3": b.R3 = value; break;
        }
    }

    private PadConfig CurrentPadConfig() => _service.GetConfig().Pads[_currentPad.ToString()];

    private void UpdatePad(Action<PadConfig> mutate)
    {
        string key = _currentPad.ToString();
        _service.UpdateConfig(cfg => mutate(cfg.Pads[key]));
    }

    private void RefreshFromConfig()
    {
        _suppressEvents = true;
        try
        {
            ControllerRootConfig cfg = _service.GetConfig();
            PadConfig pad = cfg.Pads[_currentPad.ToString()];

            _globalEnabledChk.Checked = cfg.Enabled;
            _p2EnabledChk.Checked = cfg.P2Enabled;
            _oskEnabledChk.Checked = cfg.OskEnabled;
            _injectionModeCombo.SelectedItem = cfg.InjectionMode;

            _typeCombo.SelectedItem = pad.ControllerTypeLabel;
            _swapAbChk.Checked = pad.SwapAB;
            _invertYChk.Checked = pad.InvertCursorY;
            _invertYChk.Enabled = _currentPad == 1;
            _pad2Note.Visible = _currentPad == 2;
            _movementSourceCombo.SelectedItem = pad.MovementSource;
            _cursorSpeedTb.Enabled = _currentPad == 1;
            _cursorDeadzoneTb.Enabled = _currentPad == 1;
            _precisionTb.Enabled = _currentPad == 1;
            _cursorSpeedTb.Value = Math.Clamp(pad.CursorSpeed, _cursorSpeedTb.Minimum, _cursorSpeedTb.Maximum);
            _cursorSpeedVal.Text = pad.CursorSpeed.ToString();
            _cursorDeadzoneTb.Value = Math.Clamp((int)Math.Round(pad.CursorDeadzone * 100), _cursorDeadzoneTb.Minimum, _cursorDeadzoneTb.Maximum);
            _cursorDeadzoneVal.Text = _cursorDeadzoneTb.Value.ToString();
            _movementDeadzoneTb.Value = Math.Clamp((int)Math.Round(pad.MovementDeadzone * 100), _movementDeadzoneTb.Minimum, _movementDeadzoneTb.Maximum);
            _movementDeadzoneVal.Text = _movementDeadzoneTb.Value.ToString();
            _precisionTb.Value = Math.Clamp((int)Math.Round(pad.PrecisionMinScale * 100), _precisionTb.Minimum, _precisionTb.Maximum);
            _precisionVal.Text = _precisionTb.Value.ToString();

            RefreshGridFromConfig(pad.Bindings);
            RefreshGlyphLabels();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void RefreshGridFromConfig(BindingsConfig bindings)
    {
        _suppressEvents = true;
        try
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                ElementRow er = (ElementRow)row.Tag!;
                if (!er.HasAction)
                {
                    continue;
                }
                List<string> tokens = GetBindingField(bindings, er.Key);
                row.Cells[1].Value = tokens.Count > 0 ? tokens[0] : "(none)";
                row.Cells[2].Value = tokens.Count > 1 ? tokens[1] : "(none)";
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void RefreshGlyphLabels()
    {
        TrackedDevice? device = _service.PadManager.GetDeviceList().FirstOrDefault(d => d.AssignedPad == _currentPad);
        string vendor = device is null ? "" : device.Source == PadSource.XInput ? "045E" : device.DeviceId.Split(':').FirstOrDefault() ?? "";
        (string s, string e, string w, string n) = Glyphs(CurrentPadConfig().ControllerTypeLabel, vendor);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            ElementRow er = (ElementRow)row.Tag!;
            row.Cells[0].Value = er.Key switch
            {
                "south" => $"South ({s})",
                "east" => $"East ({e})",
                "west" => $"West ({w})",
                "north" => $"North ({n})",
                _ => er.Label
            };
        }
    }

    private static (string south, string east, string west, string north) Glyphs(string typeLabel, string vendorId)
    {
        string effective = typeLabel;
        if (effective == "auto")
        {
            effective = vendorId.ToUpperInvariant() switch
            {
                "057E" => "switch",
                "054C" => "playstation",
                "045E" => "xbox",
                _ => "generic"
            };
        }
        return effective switch
        {
            "xbox" => ("A", "B", "X", "Y"),
            "switch" => ("B", "A", "Y", "X"),
            "playstation" => ("Cross", "Circle", "Square", "Triangle"),
            _ => ("South", "East", "West", "North")
        };
    }

    private void RefreshLiveVisualizer()
    {
        if (!Visible)
        {
            return;
        }

        PadState pad = _service.PadManager.GetPad(_currentPad);
        TrackedDevice? device = _service.PadManager.GetDeviceList().FirstOrDefault(d => d.AssignedPad == _currentPad);
        _statusLabel.Text = $"Pad {_currentPad}: " + (device is not null ? PadManager.DescribeStatus(device) : "Not connected");

        SetOn("lt", pad.LT > 0.3f);
        SetOn("rt", pad.RT > 0.3f);
        SetOn("lb", pad.LB);
        SetOn("rb", pad.RB);
        SetOn("dpadUp", pad.DpadUp);
        SetOn("dpadDown", pad.DpadDown);
        SetOn("dpadLeft", pad.DpadLeft);
        SetOn("dpadRight", pad.DpadRight);
        SetOn("south", pad.South);
        SetOn("east", pad.East);
        SetOn("west", pad.West);
        SetOn("north", pad.North);
        SetOn("start", pad.Start);
        SetOn("back", pad.Back);
        SetOn("leftStick", Math.Abs(pad.LX) > 0.35f || Math.Abs(pad.LY) > 0.35f || pad.L3);
        SetOn("rightStick", Math.Abs(pad.RX) > 0.35f || Math.Abs(pad.RY) > 0.35f || pad.R3);

        OffsetMarker("leftStick", 118, 94, pad.LX, pad.LY);
        OffsetMarker("rightStick", 320, 176, pad.RX, pad.RY);
    }

    private void SetOn(string key, bool on)
    {
        if (_markers.TryGetValue(key, out MarkerDot? dot))
        {
            dot.On = on;
        }
    }

    private void OffsetMarker(string key, int homeX, int homeY, float ax, float ay)
    {
        if (!_markers.TryGetValue(key, out MarkerDot? dot))
        {
            return;
        }
        int x = homeX + (int)(Deadzone(ax) * 14);
        int y = homeY + (int)(Deadzone(ay) * 14);
        dot.Location = new Point(x - 8, y - 8);
    }

    private static float Deadzone(float v) => Math.Abs(v) > 0.18f ? v : 0f;

    private void SwapPads()
    {
        _service.UpdateConfig(cfg =>
        {
            (PadAssignmentConfig? a1, PadAssignmentConfig? a2) = (cfg.Assignments.GetValueOrDefault("1"), cfg.Assignments.GetValueOrDefault("2"));
            if (a1 is not null)
            {
                cfg.Assignments["2"] = a1;
            }
            else
            {
                cfg.Assignments.Remove("2");
            }
            if (a2 is not null)
            {
                cfg.Assignments["1"] = a2;
            }
            else
            {
                cfg.Assignments.Remove("1");
            }
        });
        RefreshFromConfig();
    }

    private void ResetCurrentPadToDefaults()
    {
        bool isPad1 = _currentPad == 1;
        _service.UpdateConfig(cfg =>
        {
            cfg.Pads[_currentPad.ToString()] = new PadConfig
            {
                Bindings = isPad1 ? BindingsConfig.DefaultPad1() : BindingsConfig.DefaultPad2()
            };
        });
        RefreshFromConfig();
    }

    private void ClearRecordedMapping()
    {
        _service.UpdateConfig(cfg => cfg.Pads[_currentPad.ToString()].PadMap = null);
        ShowCaptureMessage("Mapping cleared.", Color.Gray);
    }

    private sealed class CaptureStep
    {
        public required string Kind;
        public required string Target;
        public required string Prompt;
    }

    private sealed class CaptureSession
    {
        public required string ElementKey;
        public required string DeviceId;
        public required List<CaptureStep> Steps;
        public int Idx;
        public string Phase = "release";
        public int Stable;
        public float[]? RestAxes;
        public bool[]? RestButtons;
        public float[]? LastAxes;
        public bool[]? LastButtons;
        public int? FirstAxisIndex;
        public readonly PadMapConfig Pending = new();
    }

    private void BeginCaptureForKey(string key)
    {
        ElementRow? row = ElementRows.FirstOrDefault(r => r.Key == key);
        if (row is null || row.RawKind == "none")
        {
            return;
        }

        TrackedDevice? device = _service.PadManager.GetDeviceList().FirstOrDefault(d => d.AssignedPad == _currentPad);
        if (device is null || device.Source != PadSource.Bridge)
        {
            MessageBox.Show(this,
                "Recording a raw mapping is only available for non-Xbox controllers detected through " +
                "the bridge. XInput controllers (Xbox controllers, or anything already emulating one via " +
                "a compatibility layer) already use the correct standard layout and don't need this.",
                "Recording not available", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CancelCapture(announce: false);

        List<CaptureStep> steps = row.RawKind switch
        {
            "stick" => new List<CaptureStep>
            {
                new() { Kind = "axis", Target = row.Key == "leftStick" ? "lx" : "rx", Prompt = "push the stick fully RIGHT" },
                new() { Kind = "axis", Target = row.Key == "leftStick" ? "ly" : "ry", Prompt = "now push the stick fully DOWN" }
            },
            "dpadDir" => new List<CaptureStep> { new() { Kind = "dpad", Target = row.Key["dpad".Length..].ToLowerInvariant(), Prompt = $"press {row.Label}" } },
            _ => new List<CaptureStep> { new() { Kind = "button", Target = row.Key, Prompt = $"press {row.Label} on the pad" } }
        };

        _capture = new CaptureSession { ElementKey = row.Key, DeviceId = device.DeviceId, Steps = steps };
        UpdateCapturePrompt();
        _captureTimer.Start();
    }

    private void UpdateCapturePrompt()
    {
        if (_capture is null)
        {
            return;
        }
        string suffix = _capture.Phase == "release" ? " (let go of everything first…)" : "";
        _captureStatusLabel.ForeColor = Color.DarkOrange;
        _captureStatusLabel.Text = $"{_capture.Steps[_capture.Idx].Prompt}{suffix} — Esc cancels";
    }

    private void CancelCapture(bool announce)
    {
        if (_capture is null)
        {
            return;
        }
        _capture = null;
        _captureTimer.Stop();
        if (announce)
        {
            ShowCaptureMessage("Cancelled.", Color.Gray);
        }
        else
        {
            _captureStatusLabel.Text = string.Empty;
        }
    }

    private void ShowCaptureMessage(string text, Color color)
    {
        _captureStatusLabel.ForeColor = color;
        _captureStatusLabel.Text = text;
        _captureMessageTimer.Stop();
        _captureMessageTimer.Start();
    }

    private void CaptureTick()
    {
        if (_capture is null)
        {
            return;
        }
        RawBridgeFrame? frame = _service.PadManager.GetRawBridgeFrame(_capture.DeviceId);
        if (frame is null)
        {
            return;
        }
        float[] axes = frame.Axes;
        bool[] buttons = frame.Buttons;

        if (_capture.Phase == "release")
        {
            bool quiet = _capture.LastAxes is not null;
            if (quiet)
            {
                for (int i = 0; i < buttons.Length && quiet; i++)
                {
                    if (i < _capture.LastButtons!.Length && buttons[i] != _capture.LastButtons[i])
                    {
                        quiet = false;
                    }
                }
                for (int i = 0; i < axes.Length && quiet; i++)
                {
                    if (i < _capture.LastAxes!.Length && Math.Abs(axes[i] - _capture.LastAxes[i]) > 0.12f)
                    {
                        quiet = false;
                    }
                }
            }
            _capture.LastAxes = (float[])axes.Clone();
            _capture.LastButtons = (bool[])buttons.Clone();
            _capture.Stable = quiet ? _capture.Stable + 1 : 0;
            if (_capture.Stable >= 4)
            {
                _capture.RestAxes = (float[])axes.Clone();
                _capture.RestButtons = (bool[])buttons.Clone();
                _capture.Phase = "detect";
                UpdateCapturePrompt();
            }
            return;
        }

        CaptureStep step = _capture.Steps[_capture.Idx];
        float[] rest = _capture.RestAxes!;
        bool[] restBtns = _capture.RestButtons!;

        int axHit = -1;
        float best = 0.55f;
        for (int i = 0; i < axes.Length; i++)
        {
            if (_capture.FirstAxisIndex == i)
            {
                continue;
            }
            float d = Math.Abs(axes[i] - (i < rest.Length ? rest[i] : 0f));
            if (d > best)
            {
                best = d;
                axHit = i;
            }
        }
        int btnHit = -1;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] && (i >= restBtns.Length || !restBtns[i]))
            {
                btnHit = i;
                break;
            }
        }
        if (axHit < 0 && btnHit < 0)
        {
            return;
        }

        switch (step.Kind)
        {
            case "axis":
                if (axHit < 0)
                {
                    return;
                }
                int sign = axes[axHit] - (axHit < rest.Length ? rest[axHit] : 0f) > 0 ? 1 : -1;
                _capture.Pending.Axes[step.Target] = new PadMapAxisEntry { Index = axHit, Sign = sign };
                _capture.FirstAxisIndex ??= axHit;
                break;
            case "dpad":
                _capture.Pending.Dpad[step.Target] = btnHit >= 0
                    ? new PadMapDpadEntry { ButtonIndex = btnHit }
                    : new PadMapDpadEntry { AxisIndex = axHit, MatchValue = axes[axHit], RestValue = axHit < rest.Length ? rest[axHit] : 0 };
                break;
            case "button":
                _capture.Pending.Buttons[step.Target] = btnHit >= 0
                    ? new PadMapButtonEntry { Index = btnHit }
                    : new PadMapButtonEntry { AxisIndex = axHit, Sign = axes[axHit] - (axHit < rest.Length ? rest[axHit] : 0f) > 0 ? 1 : -1 };
                break;
        }

        _capture.Idx++;
        if (_capture.Idx >= _capture.Steps.Count)
        {
            FinishCapture();
            return;
        }
        _capture.Phase = "release";
        _capture.Stable = 0;
        _capture.LastAxes = null;
        _capture.LastButtons = null;
        UpdateCapturePrompt();
    }

    private void FinishCapture()
    {
        CaptureSession done = _capture!;
        string padKey = _currentPad.ToString();
        _service.UpdateConfig(cfg =>
        {
            PadConfig p = cfg.Pads[padKey];
            p.PadMap ??= new PadMapConfig();
            foreach (KeyValuePair<string, PadMapButtonEntry> kv in done.Pending.Buttons)
            {
                p.PadMap.Buttons[kv.Key] = kv.Value;
            }
            foreach (KeyValuePair<string, PadMapDpadEntry> kv in done.Pending.Dpad)
            {
                p.PadMap.Dpad[kv.Key] = kv.Value;
            }
            foreach (KeyValuePair<string, PadMapAxisEntry> kv in done.Pending.Axes)
            {
                p.PadMap.Axes[kv.Key] = kv.Value;
            }
        });
        _capture = null;
        _captureTimer.Stop();
        ShowCaptureMessage($"{done.ElementKey} saved.", Color.SeaGreen);
    }

}

internal sealed class MarkerDot : Control
{
    private bool _on;

    public bool On
    {
        get => _on;
        set
        {
            if (_on != value)
            {
                _on = value;
                Invalidate();
            }
        }
    }

    public MarkerDot()
    {
        Size = new Size(16, 16);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using GraphicsPath path = new();
        path.AddEllipse(0, 0, Width, Height);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color fill = On ? Color.FromArgb(56, 208, 56) : Color.FromArgb(80, 200, 80);
        Color border = On ? Color.FromArgb(12, 92, 12) : Color.FromArgb(30, 122, 30);
        using SolidBrush b = new(fill);
        e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        using Pen p = new(border, 2);
        e.Graphics.DrawEllipse(p, 1, 1, Width - 3, Height - 3);
    }
}
