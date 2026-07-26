using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace TurnRewind;

public partial class RewindBar : PanelContainer
{
    public const string NodeName = "TurnRewindBar";
    private const int MaxSegments = 10;
    private static readonly List<WeakReference<RewindBar>> Bars = [];

    private HBoxContainer? _segments;
    private Label? _title;
    private Label? _counter;

    public static void Attach(NCombatUi ui)
    {
        if (ui.GetNodeOrNull<RewindBar>(NodeName) is { } existing)
        {
            existing.Refresh();
            return;
        }

        var bar = new RewindBar
        {
            Name = NodeName,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -405f,
            OffsetRight = 405f,
            // Keep the rewind bar below the game's top status strip.  It used to
            // start around y=54 and could overlap HP/gold/relic icons at some
            // resolutions; lowering it preserves the top bar and still leaves the
            // combat board unobstructed.
            OffsetTop = 88f,
            OffsetBottom = 160f,
            MouseFilter = MouseFilterEnum.Pass,
            ZIndex = 95
        };
        bar.Build();
        ui.AddChild(bar);
        Bars.Add(new WeakReference<RewindBar>(bar));
        bar.Refresh();
        MainFile.Logger.Info("[TurnRewind] rewind bar attached to combat UI.");
    }

    public static void RefreshAllBars()
    {
        for (var i = Bars.Count - 1; i >= 0; i--)
        {
            if (Bars[i].TryGetTarget(out var bar) && GodotObject.IsInstanceValid(bar))
                bar.Refresh();
            else
                Bars.RemoveAt(i);
        }
    }

    private void Build()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            // Warm hand-painted wood panel instead of a glossy/tech overlay.
            BgColor = new Color(0.255f, 0.135f, 0.055f, 0.94f),
            BorderColor = new Color(0.075f, 0.035f, 0.012f, 0.98f),
            BorderWidthLeft = 5,
            BorderWidthTop = 5,
            BorderWidthRight = 5,
            BorderWidthBottom = 5,
            CornerRadiusTopLeft = 22,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 22,
            ContentMarginLeft = 14,
            ContentMarginTop = 6,
            ContentMarginRight = 14,
            ContentMarginBottom = 8,
            ShadowColor = new Color(0.02f, 0.012f, 0.006f, 0.38f),
            ShadowSize = 4,
            ShadowOffset = new Vector2(2f, 3f)
        });

        var root = new VBoxContainer { Name = "Root", MouseFilter = MouseFilterEnum.Pass };
        root.AddThemeConstantOverride("separation", 2);
        AddChild(root);

        var header = new HBoxContainer
        {
            Name = "Header",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        header.AddThemeConstantOverride("separation", 8);
        root.AddChild(header);

        var leftOrnament = MakeOrnament("◆");
        header.AddChild(leftOrnament);

        _title = new Label
        {
            Text = "↶ 回合回溯",
            HorizontalAlignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _title.AddThemeFontSizeOverride("font_size", 15);
        _title.AddThemeConstantOverride("outline_size", 2);
        _title.AddThemeColorOverride("font_color", new Color(1f, 0.80f, 0.44f, 1f));
        _title.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.035f, 0.01f, 0.95f));
        header.AddChild(_title);

        _counter = new Label
        {
            Text = "0/10",
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _counter.AddThemeFontSizeOverride("font_size", 13);
        _counter.AddThemeConstantOverride("outline_size", 2);
        _counter.AddThemeColorOverride("font_color", new Color(0.93f, 0.74f, 0.43f, 0.94f));
        _counter.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.025f, 0.005f, 0.95f));
        header.AddChild(_counter);

        var rightOrnament = MakeOrnament("◆");
        header.AddChild(rightOrnament);

        _segments = new HBoxContainer { Name = "Segments", MouseFilter = MouseFilterEnum.Pass };
        _segments.AddThemeConstantOverride("separation", 5);
        root.AddChild(_segments);

        for (var i = 0; i < MaxSegments; i++)
        {
            var segment = new RewindSegment { Name = $"Segment{i}", SlotIndex = i };
            _segments.AddChild(segment);
        }
    }

    public void Refresh()
    {
        if (_segments is null)
            return;

        var snapshots = SnapshotManager.Snapshots;
        _title!.Text = snapshots.Count == 0 ? "↶ 等待回合记录" : "↶ 回合回溯";
        if (_counter is not null)
            _counter.Text = snapshots.Count == 0 ? "等待中" : $"{snapshots.Count}/10  长按返回";

        for (var i = 0; i < MaxSegments; i++)
        {
            if (_segments.GetChild(i) is RewindSegment segment)
            {
                var snapshot = i < snapshots.Count ? snapshots[i] : null;
                segment.SetSnapshot(snapshot);
            }
        }
    }

    private static Label MakeOrnament(string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeColorOverride("font_color", new Color(0.88f, 0.55f, 0.20f, 0.94f));
        label.AddThemeColorOverride("font_outline_color", new Color(0.09f, 0.025f, 0.005f, 0.96f));
        return label;
    }
}

public partial class RewindSegment : Button
{
    private const double HoldSeconds = 0.85;
    private ColorRect? _fill;
    private Label? _caption;
    private Label? _smallCaption;
    private bool _holding;
    private double _held;
    private TurnSnapshot? _snapshot;
    private string _snapshotLabel = "-";
    private StyleBoxFlat? _normalStyle;
    private StyleBoxFlat? _hoverStyle;
    private StyleBoxFlat? _disabledStyle;

    public int SlotIndex { get; set; }

    public override void _Ready()
    {
        ToggleMode = false;
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(72f, 34f);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        Text = "";
        BuildStyles();

        _fill = new ColorRect
        {
            Name = "HoldFill",
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 0f,
            AnchorBottom = 1f,
            // Draw the hold progress inside the button border.  Filling the full
            // rect made the progress paint visually drift over the thick
            // hand-drawn border on mobile.
            OffsetLeft = 4f,
            OffsetTop = 4f,
            OffsetRight = 0f,
            OffsetBottom = -4f,
            // Long-press fill: muted ember paint, not neon progress.
            Color = new Color(0.72f, 0.23f, 0.075f, 0.46f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1
        };
        AddChild(_fill);

        var shine = new ColorRect
        {
            Name = "TopShine",
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 0f,
            OffsetLeft = 5f,
            OffsetTop = 5f,
            OffsetRight = -5f,
            OffsetBottom = 8f,
            // A flat painted highlight strip, deliberately low contrast.
            Color = new Color(0.96f, 0.70f, 0.38f, 0.10f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 2
        };
        AddChild(shine);

        _caption = new Label
        {
            Name = "Caption",
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 4
        };
        _caption.AddThemeFontSizeOverride("font_size", 15);
        _caption.AddThemeConstantOverride("outline_size", 2);
        _caption.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.56f, 1f));
        _caption.AddThemeColorOverride("font_outline_color", new Color(0.10f, 0.025f, 0.005f, 0.98f));
        AddChild(_caption);

        _smallCaption = new Label
        {
            Name = "SmallCaption",
            AnchorLeft = 0f,
            AnchorTop = 1f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetTop = -13f,
            OffsetBottom = -2f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 5
        };
        _smallCaption.AddThemeFontSizeOverride("font_size", 9);
        _smallCaption.AddThemeConstantOverride("outline_size", 1);
        _smallCaption.AddThemeColorOverride("font_color", new Color(0.96f, 0.64f, 0.30f, 0.80f));
        _smallCaption.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.018f, 0.004f, 0.95f));
        AddChild(_smallCaption);
        SetSnapshot(_snapshot);
    }

    public override void _Process(double delta)
    {
        if (!_holding || _snapshot is null)
            return;

        if (!GetGlobalRect().Grow(8f).HasPoint(GetGlobalMousePosition()))
        {
            CancelHold();
            return;
        }

        _held += delta;
        UpdateHoldVisual();
        if (_held >= HoldSeconds)
        {
            var snapshot = _snapshot;
            CancelHold();
            SnapshotManager.Restore(snapshot);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_snapshot is null || Disabled)
            return;

        if (@event is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
                BeginHold();
            else
                CancelHold();
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton rightMouse && rightMouse.ButtonIndex == MouseButton.Right && rightMouse.Pressed)
        {
            // Desktop convenience path for testing with a mouse. Mobile still uses the required long-press gesture.
            CancelHold();
            SnapshotManager.Restore(_snapshot);
            AcceptEvent();
        }
        else if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
                BeginHold();
            else
                CancelHold();
            AcceptEvent();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_snapshot is null || Disabled)
            return;

        if (@event is InputEventMouseButton mouse)
        {
            var inside = GetGlobalRect().HasPoint(mouse.Position);
            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.Pressed && inside)
                {
                    BeginHold();
                    GetViewport().SetInputAsHandled();
                }
                else if (!mouse.Pressed && _holding)
                {
                    CancelHold();
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed && inside)
            {
                CancelHold();
                SnapshotManager.Restore(_snapshot);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventScreenTouch touch)
        {
            var inside = GetGlobalRect().HasPoint(touch.Position);
            if (touch.Pressed && inside)
            {
                BeginHold();
                GetViewport().SetInputAsHandled();
            }
            else if (!touch.Pressed && _holding)
            {
                CancelHold();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
            CancelHold();
    }

    public void SetSnapshot(TurnSnapshot? snapshot)
    {
        _snapshot = snapshot;
        Disabled = snapshot is null;
        _snapshotLabel = snapshot?.Label ?? "-";
        if (_caption is not null)
            _caption.Text = _snapshotLabel;
        if (_smallCaption is not null)
            _smallCaption.Text = snapshot is null ? "" : $"#{snapshot.Sequence}";
        TooltipText = snapshot is null
            ? "暂无回合快照"
            : $"长按回到第 {snapshot.PlayerTurnNumber} 回合（记录 #{snapshot.Sequence}）";
        CancelHold();
        Modulate = snapshot is null ? new Color(1f, 1f, 1f, 0.34f) : Colors.White;
    }

    private void BeginHold()
    {
        _holding = true;
        _held = 0;
        AddThemeStyleboxOverride("normal", _hoverStyle);
        AddThemeStyleboxOverride("hover", _hoverStyle);
        UpdateHoldVisual();
    }

    private void CancelHold()
    {
        _holding = false;
        _held = 0;
        if (_normalStyle is not null)
            AddThemeStyleboxOverride("normal", Disabled ? _disabledStyle : _normalStyle);
        if (_fill is not null)
            _fill.AnchorRight = 0f;
        if (_caption is not null)
        {
            _caption.Text = _snapshotLabel;
            _caption.Modulate = new Color(1f, 0.86f, 0.56f, 1f);
        }
        if (_smallCaption is not null)
            _smallCaption.Visible = true;
        // Do not scale Control nodes during touch-hold.  Godot scales controls
        // around their origin by default, which makes the visual state drift away
        // from the actual touch rect and looks like a misplaced long-press UI.
        Scale = Vector2.One;
        SelfModulate = Colors.White;
    }

    private void UpdateHoldVisual()
    {
        var progress = (float)Math.Clamp(_held / HoldSeconds, 0.0, 1.0);
        if (_fill is not null)
            _fill.AnchorRight = progress;
        SelfModulate = new Color(1f, 0.98f, 0.92f, 1f);
        if (_caption is not null)
        {
            _caption.Text = _snapshotLabel;
            _caption.Modulate = new Color(1f, 0.93f, 0.64f, 1f);
        }
        if (_smallCaption is not null)
        {
            _smallCaption.Visible = true;
            _smallCaption.Text = $"{Math.Round(progress * 100f):0}%";
        }
    }

    private void BuildStyles()
    {
        _normalStyle = MakeStyle(new Color(0.43f, 0.245f, 0.095f, 0.96f), new Color(0.105f, 0.052f, 0.018f, 0.98f));
        _hoverStyle = MakeStyle(new Color(0.53f, 0.30f, 0.105f, 0.98f), new Color(0.16f, 0.075f, 0.024f, 1f));
        _disabledStyle = MakeStyle(new Color(0.15f, 0.105f, 0.075f, 0.66f), new Color(0.075f, 0.052f, 0.035f, 0.80f));
        AddThemeStyleboxOverride("normal", _normalStyle);
        AddThemeStyleboxOverride("hover", _hoverStyle);
        AddThemeStyleboxOverride("pressed", _hoverStyle);
        AddThemeStyleboxOverride("disabled", _disabledStyle);
    }

    private static StyleBoxFlat MakeStyle(Color bg, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 4,
            BorderWidthTop = 4,
            BorderWidthRight = 4,
            BorderWidthBottom = 4,
            CornerRadiusTopLeft = 13,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 13,
            ContentMarginLeft = 5,
            ContentMarginTop = 2,
            ContentMarginRight = 5,
            ContentMarginBottom = 2,
            ShadowColor = new Color(0.02f, 0.012f, 0.006f, 0.26f),
            ShadowSize = 2,
            ShadowOffset = new Vector2(1f, 2f)
        };
    }
}

