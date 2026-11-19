using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Silk.Ui;

public sealed record LyricStyle(double FontSize, double ActiveScale, double Spacing, double Dim, int Align, bool Bold, bool Blur);

/// <summary>
/// Lyrics that move like a sheet of separate lines: each line springs to its place a moment after the
/// one before it, lines away from the sung one soften out of focus, and breaks marked in the lyric
/// data show three breathing dots. Timing comes only from the lyric file; nothing is guessed.
/// </summary>
public sealed class LyricView : FrameworkElement
{
    /// <summary>Lines light up slightly before their timestamp, as the voice arrives.</summary>
    private const double Lead = .18;
    private const double StaggerStep = .05;
    private const int StaggerRows = 7;
    /// <summary>Seconds after the last wheel turn before the view follows the song again.</summary>
    private const double BrowseHold = 3;
    /// <summary>Shortest lead-in before the first line, or marked break, that shows the dots.</summary>
    private const double BreakMinimum = 3;
    private const double Stiffness = 110;
    private const double Damping = 17;

    private readonly VisualCollection _children;
    private readonly List<LyricRow> _rows = [];
    private readonly bool _reducedMotion = !SystemParameters.ClientAreaAnimation;
    private IReadOnlyList<LyricLine> _source = [];
    private Func<double> _clock = () => 0;
    private LyricStyle _style = new(19, 1.3, 2.8, .56, 1, true, true);
    private double _laidOutWidth = -1;
    private bool _dirty = true;
    private bool _snap = true;
    private bool _rendering;
    /// <summary>Lyrics without timestamps: shown as plain text that can be scrolled, with nothing highlighted.</summary>
    private bool _plain;
    private int _focus = -1;
    private double _now;
    private double _goal;
    private double _contentHeight;
    private double? _browse;
    private double _lastWheel = double.NegativeInfinity;
    private TimeSpan _lastFrame;

    public LyricView()
    {
        _children = new VisualCollection(this);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Animated);
        IsVisibleChanged += (_, _) => SetRendering(IsVisible);
    }

    public event Action<double>? Seek;

    /// <summary>
    /// While the player slides in or out, blurred lines would be re-blurred on every frame of the fade,
    /// which halves the frame rate. Calm drops the blur for the transition; it eases back in afterwards.
    /// </summary>
    public bool Calm { get; set; }

    public void Attach(IReadOnlyList<LyricLine> lines, Func<double> clock)
    {
        if (_source is INotifyCollectionChanged old) old.CollectionChanged -= OnLinesChanged;
        _source = lines;
        _clock = clock;
        if (lines is INotifyCollectionChanged events) events.CollectionChanged += OnLinesChanged;
        _dirty = true;
    }

    public void ApplyStyle(LyricStyle style)
    {
        if (style == _style) return;
        var relayout = style.FontSize != _style.FontSize || style.ActiveScale != _style.ActiveScale
            || style.Spacing != _style.Spacing || style.Align != _style.Align;
        _style = style;
        if (relayout)
        {
            LayoutRows();
            _snap = true;
        }
    }

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size available) => new(
        double.IsInfinity(available.Width) ? 0 : available.Width,
        double.IsInfinity(available.Height) ? 0 : available.Height);

    protected override Size ArrangeOverride(Size final)
    {
        if (Math.Abs(final.Width - _laidOutWidth) > .5) LayoutRows(final.Width);
        foreach (var row in _rows) row.Arrange(new Rect(0, 0, final.Width, row.LocalHeight));
        return final;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => LayoutRows();

    // A transparent surface so the wheel works between lines too.
    protected override void OnRender(DrawingContext dc) => dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_rows.Count == 0) return;
        var half = ActualHeight / 2;
        _browse = Math.Clamp((_browse ?? _goal) - e.Delta * .55, -half, Math.Max(-half, _contentHeight - half));
        _lastWheel = _now;
        foreach (var row in _rows) row.ReleaseAt = 0;
        e.Handled = true;
    }

    internal void Activate(LyricRow row)
    {
        if (!row.CanSeek) return;
        _browse = null;
        foreach (var each in _rows) each.ReleaseAt = 0;
        Seek?.Invoke(row.Start);
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => _dirty = true;

    private void SetRendering(bool on)
    {
        if (on == _rendering) return;
        _rendering = on;
        if (on)
        {
            _snap = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }
        else
        {
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == _lastFrame) return;
        var dt = _lastFrame == TimeSpan.Zero ? 0 : Math.Min(.05, (time - _lastFrame).TotalSeconds);
        _lastFrame = time;
        _now += dt;
        if (_dirty) Rebuild();
        if (_rows.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        Step(dt, _clock());
    }

    private void Rebuild()
    {
        _dirty = false;
        _rows.Clear();
        _children.Clear();
        _focus = -1;
        _browse = null;
        _snap = true;

        var lines = _source.ToList();
        _plain = lines.Count > 0 && lines.All(l => !l.Timed);
        // The lead-in runs from the start, or from the last credit line, to the first sung line.
        var firstSung = lines.FindIndex(l => l.Text.Length > 0 && !l.IsCredit);
        var leadIn = firstSung > 0 && lines[firstSung - 1].IsCredit ? lines[firstSung - 1].Time : 0;
        var showLeadIn = firstSung >= 0 && lines[firstSung].Timed && lines[firstSung].Time - leadIn >= BreakMinimum
            && !lines.Take(firstSung).Any(l => l.Text.Length == 0);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i == firstSung && showLeadIn)
                Add(new LyricRow(this, "", leadIn, line.Time, isBreak: true, timed: true));
            if (line.Text.Length > 0)
            {
                Add(new LyricRow(this, line.Text, line.Time, line.Time, isBreak: false, timed: line.Timed, isCredit: line.IsCredit));
                continue;
            }
            // An empty timed line marks where the singing stops; the break lasts until the next line.
            var next = lines.Skip(i + 1).FirstOrDefault(l => l.Text.Length > 0);
            if (next is null || next.Time - line.Time < BreakMinimum) continue;
            if (_rows.Count > 0 && _rows[^1].IsBreak) continue;
            Add(new LyricRow(this, "", line.Time, next.Time, isBreak: true, timed: true));
        }
        LayoutRows();

        void Add(LyricRow row)
        {
            _rows.Add(row);
            _children.Add(row);
        }
    }

    private void LayoutRows(double width = double.NaN)
    {
        if (double.IsNaN(width)) width = ActualWidth;
        if (width <= 0 || _rows.Count == 0) return;
        _laidOutWidth = width;
        var family = TextElement.GetFontFamily(this);
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var sample = new FormattedText("國A", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), _style.FontSize, Brushes.White, dip);
        var gap = Math.Max(_style.FontSize * .45, _style.FontSize * _style.Spacing - sample.Height);
        foreach (var row in _rows) row.Layout(width, _style, family, dip, gap);
        InvalidateArrange();
    }

    private void Step(double dt, double position)
    {
        var active = -1;
        if (!_plain)
            for (var i = 0; i < _rows.Count && _rows[i].Start <= position + Lead; i++) active = i;
        var focus = Math.Max(active, 0);
        if (focus != _focus)
        {
            Stagger(_focus, focus);
            _focus = focus;
        }

        var instant = _snap || _reducedMotion;
        var ease = instant ? 1 : 1 - Math.Exp(-dt / .16);
        var appear = instant ? 1 : 1 - Math.Exp(-dt / .22);
        var scale = _style.ActiveScale;
        double top = 0;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var on = i == active;
            if (row.IsBreak) row.Appear = Approach(row.Appear, on ? 1 : 0, appear, .002);
            else row.Emphasis = Approach(row.Emphasis, on && !row.IsCredit ? 1 : 0, ease, .002);
            row.Top = top;
            top += row.CurrentHeight(scale);
        }
        _contentHeight = top;

        var height = ActualHeight;
        var focused = _rows[focus];
        var nominal = _style.FontSize * _style.Spacing;
        var follow = _plain
            ? (_contentHeight <= height ? -(height - _contentHeight) / 2 : -nominal * .5)
            : focused.Top + focused.CurrentHeight(scale) / 2 - height / 2;
        if (_browse is not null && !_plain && _now - _lastWheel > BrowseHold)
        {
            _browse = null;
            foreach (var row in _rows) row.ReleaseAt = 0;
        }
        _goal = _browse ?? follow;

        var browsing = _browse is not null || IsMouseOver || _plain;
        foreach (var row in _rows)
        {
            var target = _now < row.ReleaseAt ? row.HeldTarget : _goal;
            if (instant)
            {
                row.Scroll = target;
                row.Velocity = 0;
            }
            else
            {
                Spring(ref row.Scroll, ref row.Velocity, target, dt);
            }

            var y = row.Top - row.Scroll;
            var rowHeight = row.CurrentHeight(scale);
            var visible = y + rowHeight > -nominal && y < height + nominal;
            row.Place(y, scale, visible, _style, _plain);
            if (!visible) continue;

            var distance = Math.Abs(y + rowHeight / 2 - height / 2) / nominal;
            var blur = _style.Blur && !browsing && !Calm && !row.IsBreak && !row.IsCredit ? Math.Clamp(distance - .6, 0, 4) * _style.FontSize * .055 : 0;
            row.SetBlur(Calm ? 0 : Approach(row.Blur, blur, instant ? 1 : 1 - Math.Exp(-dt / .2), .02));
            if (row.IsBreak && row.Appear > .005) row.Animate(position, _now, _reducedMotion);
        }
        _snap = false;
    }

    /// <summary>When the sung line changes, the lines start moving one after another in the direction of travel.</summary>
    private void Stagger(int from, int to)
    {
        var together = from < 0 || _browse is not null || _reducedMotion || Math.Abs(to - from) > StaggerRows;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (together)
            {
                row.ReleaseAt = 0;
                continue;
            }
            var order = to > from ? i - (to - 1) : to + 1 - i;
            row.HeldTarget = _goal;
            row.ReleaseAt = _now + Math.Clamp(order, 0, StaggerRows) * StaggerStep;
        }
    }

    /// <summary>Eases towards the target and lands on it exactly, so a settled view stops redrawing.</summary>
    private static double Approach(double value, double target, double amount, double close)
    {
        var next = value + (target - value) * amount;
        return Math.Abs(target - next) < close ? target : next;
    }

    private static void Spring(ref double x, ref double v, double target, double dt)
    {
        if (x == target && v == 0) return;
        if (Math.Abs(target - x) < .05 && Math.Abs(v) < .5)
        {
            x = target;
            v = 0;
            return;
        }
        var steps = Math.Max(1, (int)Math.Ceiling(dt * 240));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            v += (Stiffness * (target - x) - Damping * v) * h;
            x += v * h;
        }
    }
}

/// <summary>One lyric line, or the dots shown during a marked break.</summary>
internal sealed class LyricRow : FrameworkElement
{
    private readonly LyricView _owner;
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _move = new();
    private FormattedText? _normal;
    private FormattedText? _bold;
    private BlurEffect? _blurEffect;
    private double _textHeight;
    private double _gap;
    private double _box;
    private double _offsetX;
    private double _alignFactor;
    private double _fontSize;
    private bool _boldShown;
    private double _position;
    private double _clock;
    private bool _still;

    public double Emphasis;
    public double Appear;
    public double Top;
    public double Scroll;
    public double Velocity;
    public double HeldTarget;
    public double ReleaseAt;
    public double Blur;

    internal LyricRow(LyricView owner, string text, double start, double end, bool isBreak, bool timed, bool isCredit = false)
    {
        _owner = owner;
        Text = text;
        Start = start;
        End = end;
        IsBreak = isBreak;
        Timed = timed;
        IsCredit = isCredit;
        RenderTransform = new TransformGroup { Children = { _scale, _move } };
        if (CanSeek) Cursor = Cursors.Hand;
    }

    public string Text { get; }
    public double Start { get; }
    public double End { get; }
    public bool IsBreak { get; }
    public bool Timed { get; }
    public bool IsCredit { get; }
    public bool CanSeek => !IsBreak && Timed && !IsCredit;
    public double LocalHeight => IsBreak ? DotBox + _gap : _textHeight + _gap;
    private double DotBox => _fontSize * 1.1;

    public double CurrentHeight(double activeScale) => IsBreak
        ? (DotBox + _gap) * Appear
        : _textHeight * (1 + (activeScale - 1) * Emphasis) + _gap;

    internal void Layout(double width, LyricStyle style, FontFamily family, double dip, double gap)
    {
        // Credit lines are set smaller, like the small print they are.
        _fontSize = IsCredit ? Math.Round(style.FontSize * .72) : style.FontSize;
        _gap = IsCredit ? gap * .55 : gap;
        _alignFactor = style.Align switch { 0 => 0, 2 => 1, _ => .5 };
        // Lines wrap at a width that still fits once the sung line is enlarged.
        _box = Math.Max(60, width / style.ActiveScale - 8);
        // Centred lyrics sit a little left of the column's middle, which reads as centred beside the cover.
        var shift = style.Align == 1 ? -Math.Round(Math.Min(24, width * .04)) : 0;
        _offsetX = (width - _box) * _alignFactor + shift;
        if (!IsBreak)
        {
            _normal = Make(FontWeights.Normal);
            _bold = Make(FontWeights.SemiBold);
            _textHeight = Math.Max(_normal.Height, _bold.Height);
        }
        _scale.CenterX = width * _alignFactor + shift;
        _scale.CenterY = _gap / 2;
        InvalidateVisual();

        FormattedText Make(FontWeight weight) => new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), _fontSize, Theme.Fg, dip)
        {
            MaxTextWidth = _box,
            TextAlignment = style.Align switch { 0 => TextAlignment.Left, 2 => TextAlignment.Right, _ => TextAlignment.Center },
        };
    }

    internal void Place(double y, double activeScale, bool visible, LyricStyle style, bool plain)
    {
        var state = visible ? Visibility.Visible : Visibility.Hidden;
        if (Visibility != state) Visibility = state;
        _move.Y = y;
        if (IsBreak) return;
        var scale = 1 + (activeScale - 1) * Emphasis;
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
        Opacity = plain ? .9 : IsCredit ? style.Dim * .85 : style.Dim + (1 - style.Dim) * Emphasis;
        var bold = style.Bold && Emphasis > .5;
        if (bold == _boldShown) return;
        _boldShown = bold;
        InvalidateVisual();
    }

    internal void SetBlur(double radius)
    {
        Blur = radius;
        var shown = Math.Round(radius * 4) / 4;
        if (shown < .25)
        {
            if (Effect is not null) Effect = null;
            return;
        }
        _blurEffect ??= new BlurEffect { RenderingBias = RenderingBias.Performance, KernelType = KernelType.Gaussian };
        if (_blurEffect.Radius != shown) _blurEffect.Radius = shown;
        if (Effect is null) Effect = _blurEffect;
    }

    internal void Animate(double position, double clock, bool still)
    {
        _position = position;
        _clock = clock;
        _still = still;
        InvalidateVisual();
    }

    internal void Invoke() => _owner.Activate(this);

    protected override void OnMouseEnter(MouseEventArgs e) => InvalidateVisual();
    protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _owner.Activate(this);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LyricRowPeer(this);

    protected override void OnRender(DrawingContext dc)
    {
        if (IsBreak)
        {
            DrawDots(dc);
            return;
        }
        var text = _boldShown ? _bold : _normal;
        if (text is null) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, LocalHeight));
        var origin = new Point(_offsetX, _gap / 2);
        if (IsMouseOver && CanSeek)
        {
            var hover = Theme.Fg.Color;
            var brush = new SolidColorBrush(Color.FromArgb(22, hover.R, hover.G, hover.B));
            brush.Freeze();
            var padX = _fontSize * .55;
            var padY = Math.Min(_gap * .35, _fontSize * .4);
            var left = origin.X + (_box - text.Width) * _alignFactor - padX;
            dc.DrawRoundedRectangle(brush, null, new Rect(left, origin.Y - padY, text.Width + padX * 2, _textHeight + padY * 2), 12, 12);
        }
        dc.DrawText(text, origin);
    }

    /// <summary>Three dots fill one after another across the break, breathe, and fold away as the next line arrives.</summary>
    private void DrawDots(DrawingContext dc)
    {
        if (Appear < .005) return;
        var span = Math.Max(.1, End - Start);
        var progress = Math.Clamp((_position - Start) / span, 0, 1);
        var leaving = Math.Clamp((End - _position) / .45, 0, 1);
        var breath = _still ? 1 : 1 + .1 * Math.Sin(_clock * Math.PI * 2 / 2.4);
        var size = Appear * (1 - Math.Pow(1 - leaving, 3)) * breath;
        if (size < .01) return;

        var radius = _fontSize * .21;
        var step = radius * 3.1;
        var height = (DotBox + _gap) * Appear;
        var width = step * 2;
        var left = _offsetX + (_box - width) * _alignFactor;
        var color = Theme.Fg.Color;
        dc.PushTransform(new ScaleTransform(size, size, left + width / 2, height / 2));
        for (var i = 0; i < 3; i++)
        {
            var lit = Math.Clamp(progress * 3 - i, 0, 1);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * (.28 + .72 * lit)), color.R, color.G, color.B));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(left + i * step, height / 2), radius, radius);
        }
        dc.Pop();
    }
}

internal sealed class LyricRowPeer(LyricRow owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() =>
        owner.IsBreak ? AutomationControlType.Text : AutomationControlType.Button;

    protected override string GetNameCore() => owner.IsBreak ? "間奏" : owner.Text;
    protected override string GetClassNameCore() => nameof(LyricRow);

    public override object? GetPattern(PatternInterface pattern) =>
        pattern == PatternInterface.Invoke && owner.CanSeek ? this : base.GetPattern(pattern);

    public void Invoke() => owner.Invoke();
}
