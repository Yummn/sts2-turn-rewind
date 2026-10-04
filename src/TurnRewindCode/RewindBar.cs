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
    private bool _dragging;
    private int _selectedIndex = -1;

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
            OffsetTop = 88f,
            OffsetBottom = 160f,
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 95
        };
        bar.Build();
        ui.AddChild(bar);
        Bars.Add(new WeakReference<RewindBar>(bar));
        bar.Refresh();
        MainFile.Logger.Info("[TurnRewind] drag-select rewind bar attached to combat UI.");
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
        _title!.Text = snapshots.Count == 0 ? "↶ 等待回合记录" : "↶ 拖动选择回溯回合";
        _counter!.Text = snapshots.Count == 0 ? "等待中" : $"{snapshots.Count}/10  松手恢复";
        if (_selectedIndex >= snapshots.Count)
            _selectedIndex = snapshots.Count - 1;

        for (var i = 0; i < MaxSegments; i++)
        {
            if (_segments.GetChild(i) is RewindSegment segment)
            {
                segment.SetSnapshot(i < snapshots.Count ? snapshots[i] : null);
                segment.SetSelected(i == _selectedIndex && _dragging);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
                BeginDrag(mouse.Position);
            else
                EndDrag();
            AcceptEvent();
            return;
        }

        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
                BeginDrag(touch.Position);
            else
                EndDrag();
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion motion && _dragging)
        {
            SelectAt(motion.Position);
            AcceptEvent();
        }
        else if (@event is InputEventScreenDrag drag && _dragging)
        {
            SelectAt(drag.Position);
            AcceptEvent();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!_dragging)
            return;

        switch (@event)
        {
            case InputEventMouseMotion motion:
                SelectAt(motion.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenDrag drag:
                SelectAt(drag.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton mouse when mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed:
                EndDrag();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch when !touch.Pressed:
                EndDrag();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void BeginDrag(Vector2 screenPosition)
    {
        if (SnapshotManager.Snapshots.Count == 0 || SnapshotManager.IsRestoreBusy)
            return;

        _dragging = true;
        SelectAt(screenPosition);
        Refresh();
    }

    private void EndDrag()
    {
        if (!_dragging)
            return;

        _dragging = false;
        var index = _selectedIndex;
        var snapshot = index >= 0 && index < SnapshotManager.Snapshots.Count
            ? SnapshotManager.Snapshots[index]
            : null;
        Refresh();
        if (snapshot is not null)
            SnapshotManager.Restore(snapshot);
    }

    private void SelectAt(Vector2 screenPosition)
    {
        if (_segments is null || SnapshotManager.Snapshots.Count == 0)
            return;

        var first = _segments.GetChild<Control>(0).GetGlobalRect();
        var lastIndex = Math.Min(SnapshotManager.Snapshots.Count, MaxSegments) - 1;
        var last = _segments.GetChild<Control>(lastIndex).GetGlobalRect();
        var width = Math.Max(last.End.X - first.Position.X, 1f);
        var ratio = Mathf.Clamp((screenPosition.X - first.Position.X) / width, 0f, 0.99999f);
        _selectedIndex = Mathf.Clamp((int)(ratio * SnapshotManager.Snapshots.Count), 0, SnapshotManager.Snapshots.Count - 1);
        for (var i = 0; i < MaxSegments; i++)
            if (_segments.GetChild(i) is RewindSegment segment)
                segment.SetSelected(i == _selectedIndex && _dragging);

        _counter!.Text = $"回合 {SnapshotManager.Snapshots[_selectedIndex].PlayerTurnNumber} · 松手恢复";
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
    private Label? _caption;
    private Label? _smallCaption;
    private TurnSnapshot? _snapshot;
    private StyleBoxFlat? _normalStyle;
    private StyleBoxFlat? _hoverStyle;
    private StyleBoxFlat? _selectedStyle;
    private StyleBoxFlat? _disabledStyle;

    public int SlotIndex { get; set; }

    public override void _Ready()
    {
        ToggleMode = false;
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(72f, 34f);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        Text = "";
        BuildStyles();

        _caption = new Label
        {
            Name = "Caption",
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
    }

    public void SetSnapshot(TurnSnapshot? snapshot)
    {
        _snapshot = snapshot;
        Disabled = snapshot is null;
        _caption!.Text = snapshot?.Label ?? "-";
        _smallCaption!.Text = snapshot is null ? "" : $"#{snapshot.Sequence}";
        TooltipText = snapshot is null ? "暂无回合快照" : "拖动到此回合，松手恢复";
        Modulate = snapshot is null ? new Color(1f, 1f, 1f, 0.34f) : Colors.White;
    }

    public void SetSelected(bool selected)
    {
        AddThemeStyleboxOverride("normal", selected ? _selectedStyle : Disabled ? _disabledStyle : _normalStyle);
        AddThemeStyleboxOverride("hover", selected ? _selectedStyle : _hoverStyle);
        SelfModulate = selected ? new Color(1f, 1f, 0.88f, 1f) : Colors.White;
        if (_smallCaption is not null && selected)
            _smallCaption.Text = "松手";
    }

    private void BuildStyles()
    {
        _normalStyle = MakeStyle(new Color(0.43f, 0.245f, 0.095f, 0.96f), new Color(0.105f, 0.052f, 0.018f, 0.98f));
        _hoverStyle = MakeStyle(new Color(0.53f, 0.30f, 0.105f, 0.98f), new Color(0.16f, 0.075f, 0.024f, 1f));
        _selectedStyle = MakeStyle(new Color(0.70f, 0.38f, 0.10f, 1f), new Color(0.98f, 0.72f, 0.30f, 1f));
        _disabledStyle = MakeStyle(new Color(0.15f, 0.105f, 0.075f, 0.66f), new Color(0.075f, 0.052f, 0.035f, 0.80f));
        AddThemeStyleboxOverride("normal", _normalStyle);
        AddThemeStyleboxOverride("hover", _hoverStyle);
        AddThemeStyleboxOverride("pressed", _selectedStyle);
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
