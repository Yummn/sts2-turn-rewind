using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace TurnRewind;

public partial class RewindBar : PanelContainer
{
    public const string NodeName = "TurnRewindBar";
    private const int MaxSegments = 10;
    private const float MoveThreshold = 12f;
    private static readonly List<WeakReference<RewindBar>> Bars = [];

    private HBoxContainer? _segments;
    private Label? _title;
    private Label? _counter;
    private bool _pointerDown;
    private bool _moving;
    private Vector2 _pointerStart;
    private Vector2 _lastPointer;

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
            // Leave the relic/status strip unobstructed on phone and desktop.
            OffsetTop = 178f,
            OffsetBottom = 250f,
            MouseFilter = MouseFilterEnum.Pass,
            ZIndex = 90
        };
        bar.Build();
        ui.AddChild(bar);
        Bars.Add(new WeakReference<RewindBar>(bar));
        bar.Refresh();
        MainFile.Logger.Info("[TurnRewind] long-press rewind bar attached; drag moves panel only.");
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

        var root = new VBoxContainer { Name = "Root", MouseFilter = MouseFilterEnum.Ignore };
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
        header.AddChild(MakeOrnament("◆"));

        _title = new Label
        {
            Text = "↶ 等待回合记录",
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
            Text = "等待中",
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _counter.AddThemeFontSizeOverride("font_size", 13);
        _counter.AddThemeConstantOverride("outline_size", 2);
        _counter.AddThemeColorOverride("font_color", new Color(0.93f, 0.74f, 0.43f, 0.94f));
        _counter.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.025f, 0.005f, 0.95f));
        header.AddChild(_counter);
        header.AddChild(MakeOrnament("◆"));

        _segments = new HBoxContainer { Name = "Segments", MouseFilter = MouseFilterEnum.Ignore };
        _segments.AddThemeConstantOverride("separation", 5);
        root.AddChild(_segments);
        for (var i = 0; i < MaxSegments; i++)
            _segments.AddChild(new RewindSegment { Name = $"Segment{i}", SlotIndex = i });
    }

    public void Refresh()
    {
        if (_segments is null)
            return;

        var snapshots = SnapshotManager.Snapshots;
        _title!.Text = snapshots.Count == 0 ? "↶ 等待回合记录" : "↶ 长按回溯 · 拖动移动";
        _counter!.Text = snapshots.Count == 0 ? "等待中" : $"{snapshots.Count}/10";

        for (var i = 0; i < MaxSegments; i++)
        {
            if (_segments.GetChild(i) is RewindSegment segment)
                segment.SetSnapshot(i < snapshots.Count ? snapshots[i] : null);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (SnapshotManager.IsRestoreBusy)
            return;

        switch (@event)
        {
            case InputEventMouseButton mouse when mouse.ButtonIndex == MouseButton.Left:
                if (mouse.Pressed && GetGlobalRect().HasPoint(mouse.Position))
                    BeginPointer(mouse.Position);
                else if (!mouse.Pressed && _pointerDown)
                    EndPointer();
                break;
            case InputEventScreenTouch touch:
                if (touch.Pressed && GetGlobalRect().HasPoint(touch.Position))
                    BeginPointer(touch.Position);
                else if (!touch.Pressed && _pointerDown)
                    EndPointer();
                break;
            case InputEventMouseMotion motion when _pointerDown:
                TrackPointer(motion.Position);
                break;
            case InputEventScreenDrag drag when _pointerDown:
                TrackPointer(drag.Position);
                break;
        }
    }

    private void BeginPointer(Vector2 position)
    {
        _pointerDown = true;
        _moving = false;
        _pointerStart = position;
        _lastPointer = position;
    }

    private void TrackPointer(Vector2 position)
    {
        if (!_pointerDown)
            return;

        if (!_moving && position.DistanceTo(_pointerStart) >= MoveThreshold)
        {
            _moving = true;
            CancelSegmentHolds();
        }

        if (!_moving)
            return;

        MovePanelBy(position - _lastPointer);
        _lastPointer = position;
        GetViewport().SetInputAsHandled();
    }

    private void EndPointer()
    {
        _pointerDown = false;
        _moving = false;
        _lastPointer = Vector2.Zero;
    }

    private void CancelSegmentHolds()
    {
        if (_segments is null)
            return;
        foreach (var segment in _segments.GetChildren().OfType<RewindSegment>())
            segment.CancelHoldFromParent();
    }

    private void MovePanelBy(Vector2 delta)
    {
        var rect = GetGlobalRect();
        var viewport = GetViewportRect();
        var x = Math.Clamp(delta.X, 8f - rect.Position.X, viewport.Size.X - 8f - rect.End.X);
        var y = Math.Clamp(delta.Y, 8f - rect.Position.Y, viewport.Size.Y - 8f - rect.End.Y);
        if (Math.Abs(x) < 0.01f && Math.Abs(y) < 0.01f)
            return;

        OffsetLeft += x;
        OffsetRight += x;
        OffsetTop += y;
        OffsetBottom += y;
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
            OffsetLeft = 4f,
            OffsetTop = 4f,
            OffsetRight = 0f,
            OffsetBottom = -4f,
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
        SetSnapshot(null);
    }

    public override void _Process(double delta)
    {
        if (!_holding || _snapshot is null)
            return;

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
        else if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
                BeginHold();
            else
                CancelHold();
            AcceptEvent();
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

    public void CancelHoldFromParent() => CancelHold();

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
        {
            _smallCaption.Visible = true;
            _smallCaption.Text = _snapshot is null ? "" : $"#{_snapshot.Sequence}";
        }
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
            _smallCaption.Text = $"{Math.Round(progress * 100f):0}%";
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

    private static StyleBoxFlat MakeStyle(Color bg, Color border) => new()
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
