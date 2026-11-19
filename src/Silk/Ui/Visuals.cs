using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Silk.Core;

namespace Silk.Ui;

/// <summary>GPU silk: slow folded sine fields tinted by the album palette, dithered against banding.</summary>
public sealed class SilkEffect : ShaderEffect
{
    private const string Source = """
        float4 P : register(c0);
        float4 A : register(c1);
        float4 B : register(c2);
        float4 C : register(c3);
        float4 main(float2 uv : TEXCOORD0) : COLOR
        {
            float t = P.x, u = uv.x, v = uv.y;
            float p = sin(u * 3.1 + t * .5 + 2. * sin(v * 2.3 - t * .33));
            float q = sin(v * 4. - t * .28 + 2.4 * sin(u * 2.6 + t * .2));
            float f = sin((p + q) * 2.3 + u * 1.6);
            float sheen = pow((f + 1.) * .5, 3.) * P.y * .5;
            float crease = pow(abs(sin((p - q) * 2.6 + v * 1.8)), 14.) * P.y * .07;
            float3 c = lerp(A.rgb, B.rgb, (p + 1.) * .36 * saturate(P.y * 3.));
            c = lerp(c, C.rgb, saturate(sheen + crease));
            float n = frac(sin(dot(uv * P.zw, float2(12.9898, 78.233))) * 43758.5453);
            return float4(c + (n - .5) / 170., 1.);
        }
        """;

    private static readonly PixelShader? Shader = Compile();

    public static readonly DependencyProperty ParamsProperty = Register("Params", 0);
    public static readonly DependencyProperty ColorAProperty = Register("ColorA", 1);
    public static readonly DependencyProperty ColorBProperty = Register("ColorB", 2);
    public static readonly DependencyProperty ColorCProperty = Register("ColorC", 3);

    public static bool IsAvailable => Shader is not null && RenderCapability.Tier >> 16 >= 2;

    public SilkEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(ParamsProperty);
        UpdateShaderValue(ColorAProperty);
        UpdateShaderValue(ColorBProperty);
        UpdateShaderValue(ColorCProperty);
    }

    public void Update(double time, double width, double height)
    {
        SetValue(ParamsProperty, new Point4D(time, Theme.Glow, width, height));
        SetValue(ColorAProperty, ToPoint(Theme.A));
        SetValue(ColorBProperty, ToPoint(Theme.B));
        SetValue(ColorCProperty, ToPoint(Theme.C));
    }

    private static Point4D ToPoint(Rgb c) => new(c.R, c.G, c.B, 1);

    private static DependencyProperty Register(string name, int slot) => DependencyProperty.Register(
        name, typeof(Point4D), typeof(SilkEffect),
        new UIPropertyMetadata(new Point4D(0, 0, 0, 0), PixelShaderConstantCallback(slot)));

    private static PixelShader? Compile()
    {
        try
        {
            var source = Encoding.ASCII.GetBytes(Source);
            var hr = D3DCompile(source, source.Length, null, 0, 0, "main", "ps_3_0", 1 << 15, 0, out var code, out var errors);
            if (errors is not null) Marshal.ReleaseComObject(errors);
            if (hr < 0 || code is null) return null;
            var bytes = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            Marshal.ReleaseComObject(code);
            var shader = new PixelShader();
            shader.SetStreamSource(new MemoryStream(bytes));
            return shader;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            return null;
        }
    }

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(byte[] data, nint size, string? name, nint defines, nint include,
        string entry, string target, uint flags1, uint flags2,
        [MarshalAs(UnmanagedType.Interface)] out ID3DBlob? code,
        [MarshalAs(UnmanagedType.Interface)] out ID3DBlob? errors);

    [ComImport, Guid("8BA5FB08-5195-40E2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        [PreserveSig] nint GetBufferPointer();
        [PreserveSig] nint GetBufferSize();
    }
}

public sealed class HeartButton : System.Windows.Controls.Button
{
    public static readonly DependencyProperty IsLikedProperty = DependencyProperty.Register(
        nameof(IsLiked), typeof(bool), typeof(HeartButton), new PropertyMetadata(false));

    public bool IsLiked
    {
        get => (bool)GetValue(IsLikedProperty);
        set => SetValue(IsLikedProperty, value);
    }
}

/// <summary>
/// The song's measured loudness drawn as the progress bar. The played part lights up,
/// and clicking or dragging moves the playhead.
/// </summary>
public sealed class WaveformView : FrameworkElement
{
    private const int Samples = WaveAnalyzer.Points - 1;
    private const double RestingLevel = .035;

    private static readonly Brush Fade = CreateFade();
    private static readonly Pen RestPen = CreatePen(Theme.Fg45, 1.15);
    private static readonly Pen PlayedPen = CreatePen(Theme.Fg, 1.6);
    private static readonly Pen GuidePen = CreatePen(Theme.Fg45, 1);

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(WaveformView),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HeightsProperty = DependencyProperty.Register(
        nameof(Heights), typeof(double[]), typeof(WaveformView),
        new PropertyMetadata(null, (d, _) => ((WaveformView)d).OnHeightsChanged()));

    public static readonly DependencyProperty HasTrackProperty = DependencyProperty.Register(
        nameof(HasTrack), typeof(bool), typeof(WaveformView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => ((WaveformView)d).Cursor = (bool)e.NewValue ? Cursors.Hand : null));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(WaveformView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlainProperty = DependencyProperty.Register(
        nameof(Plain), typeof(bool), typeof(WaveformView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(bool), typeof(WaveformView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((WaveformView)d).Rebuild()));

    private readonly double[] _heights = new double[Samples + 1];
    private StreamGeometry? _area;
    private StreamGeometry? _line;
    // The bars as a ready-made transparency picture; see EnsureBars.
    private ImageBrush? _bars;
    private double[] _barCentres = [];
    private double[] _barTops = [];
    private Size _builtFor;
    private double? _dragValue;
    private double? _hoverX;

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double[]? Heights
    {
        get => (double[]?)GetValue(HeightsProperty);
        set => SetValue(HeightsProperty, value);
    }

    public bool HasTrack
    {
        get => (bool)GetValue(HasTrackProperty);
        set => SetValue(HasTrackProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>Draws an ordinary thin progress line, leaving the loudness out.</summary>
    public bool Plain
    {
        get => (bool)GetValue(PlainProperty);
        set => SetValue(PlainProperty, value);
    }

    /// <summary>Draws the loudness as separate rounded bars instead of a smooth ribbon.</summary>
    public bool Bars
    {
        get => (bool)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public event Action<double>? Seeked;

    public WaveformView()
    {
        Focusable = false;
        Array.Fill(_heights, RestingLevel);
        MouseMove += (_, e) =>
        {
            _hoverX = e.GetPosition(this).X;
            if (IsMouseCaptured) _dragValue = RatioAt(e);
            InvalidateVisual();
        };
        MouseLeave += (_, _) =>
        {
            _hoverX = null;
            InvalidateVisual();
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!HasTrack) return;
            CaptureMouse();
            _dragValue = RatioAt(e);
            InvalidateVisual();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!IsMouseCaptured) return;
            var value = RatioAt(e);
            ReleaseMouseCapture();
            _dragValue = null;
            Seeked?.Invoke(value);
            InvalidateVisual();
        };
        LostMouseCapture += (_, _) =>
        {
            if (_dragValue is null) return;
            _dragValue = null;
            InvalidateVisual();
        };
    }

    private double RatioAt(MouseEventArgs e) => Math.Clamp(e.GetPosition(this).X / Math.Max(1, ActualWidth), 0, 1);

    internal void RequestSeek(double ratio) => Seeked?.Invoke(Math.Clamp(ratio, 0, 1));

    protected override AutomationPeer OnCreateAutomationPeer() => new WaveformPeer(this);

    private void OnHeightsChanged()
    {
        var incoming = Heights;
        var target = new double[Samples + 1];
        for (var i = 0; i <= Samples; i++)
            target[i] = Math.Max(RestingLevel, incoming is { Length: Samples + 1 } ? incoming[i] : 0);

        var start = (double[])_heights.Clone();
        if (!IsLoaded)
        {
            target.CopyTo(_heights, 0);
            Rebuild();
            return;
        }
        Tween.Run(this, 300, t =>
        {
            for (var i = 0; i <= Samples; i++) _heights[i] = start[i] + (target[i] - start[i]) * t;
            Rebuild();
        }, Tween.EaseInOut);
    }

    private void Rebuild()
    {
        _area = null;
        _line = null;
        _bars = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (!HasTrack || w < 8 || h < 4) return;

        if (Plain)
        {
            RenderPlain(dc, w, h);
            return;
        }
        if (Bars)
        {
            RenderBars(dc, w, h);
            return;
        }

        EnsureGeometry(w, h);
        dc.PushOpacityMask(Fade);
        dc.DrawGeometry(Theme.Fg12, null, _area);
        dc.Pop();
        dc.DrawGeometry(null, RestPen, _line);

        var ratio = Math.Clamp(_dragValue ?? Progress, 0, 1);
        var head = ratio * w;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, head, h)));
        dc.PushOpacityMask(Fade);
        dc.DrawGeometry(Theme.Fg45, null, _area);
        dc.Pop();
        dc.DrawGeometry(null, PlayedPen, _line);
        dc.Pop();

        if (_hoverX is { } hover && _dragValue is null)
            dc.DrawLine(GuidePen, new Point(hover, YAt(hover / w, h)), new Point(hover, h));
        if (ratio > 0)
            dc.DrawEllipse(Theme.Fg, null, new Point(head, YAt(ratio, h)), Compact ? 3 : 4, Compact ? 3 : 4);
    }

    private void RenderPlain(DrawingContext dc, double w, double h)
    {
        var active = _hoverX is not null || _dragValue is not null;
        var thickness = (Compact ? 3 : 4) + (active ? 2 : 0);
        // In the player the line sits level with the time shown beside it; on the home bar it is centred.
        var y = Compact ? h / 2 : Math.Clamp(h - 16, thickness, h - thickness);
        var ratio = Math.Clamp(_dragValue ?? Progress, 0, 1);
        var head = ratio * w;
        var round = thickness / 2;
        dc.DrawRoundedRectangle(Theme.Fg24, null, new Rect(0, y - round, w, thickness), round, round);
        if (head > 0) dc.DrawRoundedRectangle(Theme.Fg, null, new Rect(0, y - round, Math.Max(thickness, head), thickness), round, round);
        if (ratio > 0)
        {
            var dot = (Compact ? 5 : 6) + (active ? 1 : 0);
            dc.DrawEllipse(Theme.Fg, null, new Point(head, y), dot, dot);
        }
    }

    private void RenderBars(DrawingContext dc, double w, double h)
    {
        EnsureBars(w, h);
        var ratio = Math.Clamp(_dragValue ?? Progress, 0, 1);
        var head = ratio * w;
        var strip = new Rect(0, 0, w, h);
        dc.PushOpacityMask(_bars);
        dc.DrawRectangle(Theme.Fg24, null, strip);
        dc.Pop();
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, head, h)));
        dc.PushOpacityMask(_bars);
        dc.DrawRectangle(Theme.Fg, null, strip);
        dc.Pop();
        dc.Pop();
        if (_hoverX is { } hover && _dragValue is null)
            dc.DrawLine(GuidePen, new Point(hover, BarTopAt(hover)), new Point(hover, h));
        if (ratio > 0)
            dc.DrawEllipse(Theme.Fg, null, new Point(head, BarTopAt(head)), Compact ? 3 : 4, Compact ? 3 : 4);
    }

    /// <summary>The height of the bar tops at x, eased between neighbouring bar centres so the dot glides instead of stepping.</summary>
    private double BarTopAt(double x)
    {
        if (_barCentres.Length == 0) return ActualHeight;
        if (x <= _barCentres[0]) return _barTops[0];
        if (x >= _barCentres[^1]) return _barTops[^1];
        var i = Array.BinarySearch(_barCentres, x);
        if (i >= 0) return _barTops[i];
        i = ~i;
        var t = (x - _barCentres[i - 1]) / (_barCentres[i] - _barCentres[i - 1]);
        t = t * t * (3 - 2 * t);
        return _barTops[i - 1] + (_barTops[i] - _barTops[i - 1]) * t;
    }

    private void EnsureBars(double w, double h)
    {
        var size = new Size(w, h);
        if (_bars is not null && _builtFor == size) return;
        _builtFor = size;
        _area = null;
        _line = null;

        // Hundreds of rounded bars as geometry would be re-tessellated on every redraw of the window, even while
        // paused. Instead they are drawn once into a transparency picture at device resolution, and each redraw
        // only fills through it. Widths and positions are whole device pixels so the bars stay crisp.
        var dpi = VisualTreeHelper.GetDpi(this);
        var sx = dpi.DpiScaleX;
        var sy = dpi.DpiScaleY;
        var barPx = (int)Math.Max(1, Math.Round((Compact ? 2 : 3) * sx));
        var gapPx = (int)Math.Max(1, Math.Round((Compact ? 2 : 2.5) * sx));
        var widthPx = Math.Max(1, (int)Math.Round(w * sx));
        var heightPx = Math.Max(1, (int)Math.Round(h * sy));
        var count = Math.Max(1, (widthPx + gapPx) / (barPx + gapPx));
        var startPx = (widthPx - (count * (barPx + gapPx) - gapPx)) / 2;
        var radius = barPx / 2.0;
        var pixels = new byte[widthPx * heightPx * 4];

        _barCentres = new double[count];
        _barTops = new double[count];
        for (var i = 0; i < count; i++)
        {
            var left = startPx + i * (barPx + gapPx);
            // Each bar shows the loudest moment it covers, so short peaks are not averaged away.
            var from = left / (double)widthPx * Samples;
            var to = (left + barPx) / (double)widthPx * Samples;
            var value = RestingLevel;
            for (var s = (int)Math.Floor(from); s <= Math.Min(Samples, (int)Math.Ceiling(to)); s++) value = Math.Max(value, _heights[s]);
            var top = heightPx - Math.Max(barPx, value * Usable(h) * sy);
            _barCentres[i] = (left + barPx / 2.0) / sx;
            _barTops[i] = top / sy;

            var capCentreY = top + radius;
            var capCentreX = left + radius;
            for (var y = Math.Max(0, (int)Math.Floor(top)); y < heightPx; y++)
            {
                for (var x = left; x < left + barPx && x < widthPx; x++)
                {
                    double cover;
                    if (y + .5 < capCentreY)
                    {
                        // Rounded top: coverage falls off across the edge of the cap circle.
                        var distance = Math.Sqrt(Math.Pow(x + .5 - capCentreX, 2) + Math.Pow(y + .5 - capCentreY, 2));
                        cover = Math.Clamp(radius - distance + .5, 0, 1);
                    }
                    else
                    {
                        cover = 1;
                    }
                    var alpha = (byte)Math.Round(cover * 255);
                    var at = (y * widthPx + x) * 4;
                    pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = alpha;
                }
            }
        }

        var bitmap = BitmapSource.Create(widthPx, heightPx, 96 * sx, 96 * sy, PixelFormats.Pbgra32, null, pixels, widthPx * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, widthPx / sx, heightPx / sy),
        };
        brush.Freeze();
        _bars = brush;
    }

    private double Usable(double h) => h * (Compact ? .86 : .9);

    private double YAt(double ratio, double h)
    {
        var position = Math.Clamp(ratio, 0, 1) * Samples;
        var index = Math.Min(Samples - 1, (int)position);
        var value = _heights[index] + (_heights[index + 1] - _heights[index]) * (position - index);
        return h - 1 - value * Usable(h);
    }

    private void EnsureGeometry(double w, double h)
    {
        var size = new Size(w, h);
        if (_area is not null && _builtFor == size) return;
        _builtFor = size;
        _bars = null;
        var points = new Point[Samples + 1];
        for (var i = 0; i <= Samples; i++)
            points[i] = new Point(i / (double)Samples * w, h - 1 - _heights[i] * Usable(h));
        _area = Build(points, h, true);
        _line = Build(points, h, false);
    }

    private static StreamGeometry Build(Point[] p, double h, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(p[0], closed, closed);
            for (var i = 0; i < p.Length - 1; i++)
            {
                var p0 = p[Math.Max(0, i - 1)];
                var p1 = p[i];
                var p2 = p[i + 1];
                var p3 = p[Math.Min(p.Length - 1, i + 2)];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                g.BezierTo(c1, c2, p2, true, true);
            }
            if (closed)
            {
                g.LineTo(new Point(p[^1].X, h), false, false);
                g.LineTo(new Point(p[0].X, h), false, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static Pen CreatePen(Brush brush, double width) => new(brush, width)
    {
        LineJoin = PenLineJoin.Round,
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    };

    private static Brush CreateFade()
    {
        var brush = new LinearGradientBrush(Color.FromArgb(255, 0, 0, 0), Color.FromArgb(40, 0, 0, 0), 90);
        brush.Freeze();
        return brush;
    }

    private sealed class WaveformPeer(WaveformView owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private WaveformView View => (WaveformView)Owner;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(WaveformView);
        public override object? GetPattern(PatternInterface pattern) =>
            pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);

        public double Value => View.Progress * 100;
        public double Minimum => 0;
        public double Maximum => 100;
        public double SmallChange => 1;
        public double LargeChange => 5;
        public bool IsReadOnly => false;
        public void SetValue(double value) => View.RequestSeek(value / 100);
    }
}
