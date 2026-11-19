using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Silk.Ui;

public readonly record struct Rgb(double R, double G, double B)
{
    public static Rgb Lerp(Rgb a, Rgb b, double t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    public Color ToColor(double alpha = 1) => Color.FromArgb(
        (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), Channel(R), Channel(G), Channel(B));

    public double Distance(Rgb o) => Math.Abs(R - o.R) + Math.Abs(G - o.G) + Math.Abs(B - o.B);

    private static byte Channel(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    public static Rgb FromHsl(double h, double s, double l)
    {
        h = (h % 360 + 360) % 360 / 60;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Rgb(r + m, g + m, b + m);
    }
}

public sealed record Palette(Rgb A, Rgb B, Rgb C, bool Light)
{
    public static readonly Palette Default = new(
        Rgb.FromHsl(208, .62, .15), Rgb.FromHsl(190, .62, .28), Rgb.FromHsl(178, .5, .68), false);

    public static readonly Palette LightDefault = new(
        Rgb.FromHsl(38, .22, .87), Rgb.FromHsl(170, .12, .8), Rgb.FromHsl(40, .3, .97), true);

    public static Palette FromImage(BitmapSource source)
    {
        var scaled = new TransformedBitmap(source, new ScaleTransform(32d / source.PixelWidth, 32d / source.PixelHeight));
        var bitmap = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);

        const int bucketCount = 24;
        var weight = new double[bucketCount];
        var saturation = new double[bucketCount];
        double lightSum = 0, satSum = 0;
        var count = pixels.Length / 4;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var (h, s, l) = ToHsl(pixels[i + 2] / 255d, pixels[i + 1] / 255d, pixels[i] / 255d);
            lightSum += l;
            satSum += s;
            var bucket = (int)(h / 360 * bucketCount) % bucketCount;
            var w = s * (1 - Math.Abs(2 * l - 1)) + 0.002;
            weight[bucket] += w;
            saturation[bucket] += s * w;
        }

        var first = Array.IndexOf(weight, weight.Max());
        var second = -1;
        for (var i = 0; i < bucketCount; i++)
        {
            var gap = Math.Abs(i - first);
            if (Math.Min(gap, bucketCount - gap) < 3) continue;
            if (second < 0 || weight[i] > weight[second]) second = i;
        }

        var hue = (first + .5) * 360 / bucketCount;
        var hasSecond = second >= 0 && weight[second] > weight[first] * .16;
        var hue2 = hasSecond ? (second + .5) * 360 / bucketCount : hue + 26;
        var sat = saturation[first] / weight[first];
        var averageLight = lightSum / count;
        if (satSum / count < .07) sat = .08;

        if (averageLight > .72)
        {
            return new Palette(
                Rgb.FromHsl(hue, Math.Min(sat, .28), .87),
                Rgb.FromHsl(hue2, Math.Min(sat, .2), .79),
                Rgb.FromHsl(hue, Math.Min(sat, .34), .97),
                true);
        }

        var s1 = sat <= .08 ? .08 : Math.Clamp(sat * .8, .3, .56);
        return new Palette(
            Rgb.FromHsl(hue, s1, .15),
            Rgb.FromHsl(hue2, s1 * .88, .26),
            Rgb.FromHsl(hue2, Math.Min(s1 + .08, .62), .66),
            false);
    }

    private static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;
        if (d < 1e-6) return (0, 0, l);
        var s = d / (1 - Math.Abs(2 * l - 1));
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }
}

/// <summary>Live brushes shared by the whole UI; every colour eases toward the current album.</summary>
public static class Theme
{
    private static readonly Rgb White = new(1, 1, 1);
    private static readonly Rgb Ink = new(38 / 255d, 31 / 255d, 26 / 255d);
    private static readonly Dictionary<SolidColorBrush, ColorSource> Sources = [];

    // Styles freeze brushes they hold unless a binding keeps them live.
    public static SolidColorBrush Fg { get; } = Live();
    public static SolidColorBrush Fg70 { get; } = Live();
    public static SolidColorBrush Fg45 { get; } = Live();
    public static SolidColorBrush Fg24 { get; } = Live();
    public static SolidColorBrush Fg12 { get; } = Live();
    public static SolidColorBrush Fg07 { get; } = Live();
    public static SolidColorBrush Panel { get; } = Live();
    public static SolidColorBrush Drawer { get; } = Live();
    public static SolidColorBrush Base { get; } = Live();

    public static Rgb A { get; private set; } = Palette.Default.A;
    public static Rgb B { get; private set; } = Palette.Default.B;
    public static Rgb C { get; private set; } = Palette.Default.C;
    public static Rgb Foreground { get; private set; } = White;

    private static Palette _target = Palette.Default;
    private static double _lightness;
    private static bool _settled;
    private static bool _solidPanel;

    public static double Glow { get; set; } = .35;

    public static void SetSolidPanel(bool solid)
    {
        _solidPanel = solid;
        Apply();
    }

    static Theme() => Apply();

    public static void Set(Palette palette)
    {
        _target = palette;
        _settled = false;
    }

    public static void Tick(double dt)
    {
        if (_settled) return;
        var k = 1 - Math.Exp(-dt / .26);
        A = Rgb.Lerp(A, _target.A, k);
        B = Rgb.Lerp(B, _target.B, k);
        C = Rgb.Lerp(C, _target.C, k);
        _lightness += ((_target.Light ? 1 : 0) - _lightness) * k;
        Foreground = Rgb.Lerp(White, Ink, Math.Clamp((_lightness - .35) / .3, 0, 1));
        _settled = A.Distance(_target.A) + B.Distance(_target.B) + C.Distance(_target.C) < .004
            && Math.Abs(_lightness - (_target.Light ? 1 : 0)) < .004;
        Apply();
    }

    private static void Apply()
    {
        Paint(Fg, Foreground.ToColor());
        Paint(Fg70, Foreground.ToColor(.7));
        Paint(Fg45, Foreground.ToColor(.45));
        Paint(Fg24, Foreground.ToColor(.24));
        Paint(Fg12, Foreground.ToColor(.12));
        Paint(Fg07, Foreground.ToColor(.07));
        Paint(Base, A.ToColor());
        var dark = new Rgb(A.R * .55, A.G * .55, A.B * .55);
        var paper = new Rgb(1, .98, .95);
        var tone = Rgb.Lerp(dark, paper, _lightness);
        Paint(Panel, tone.ToColor(_solidPanel ? .9 : .5));
        Paint(Drawer, Rgb.Lerp(new Rgb(A.R * .62, A.G * .62, A.B * .62), paper, _lightness).ToColor());
    }

    private static void Paint(SolidColorBrush brush, Color color) => Sources[brush].Value = color;

    private static SolidColorBrush Live()
    {
        var brush = new SolidColorBrush();
        var source = new ColorSource();
        Sources[brush] = source;
        BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty,
            new Binding(nameof(ColorSource.Value)) { Source = source, Mode = BindingMode.OneWay });
        return brush;
    }

    private sealed class ColorSource : INotifyPropertyChanged
    {
        private Color _value;
        public event PropertyChangedEventHandler? PropertyChanged;

        public Color Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }
}

public static class ImageCache
{
    public const int FullWidth = 720;
    private const long ByteBudget = 180L * 1024 * 1024;

    private sealed class Entry(Task<BitmapSource?> task, LinkedListNode<string> node)
    {
        public Task<BitmapSource?> Task { get; } = task;
        public LinkedListNode<string> Node { get; } = node;
        public long Bytes { get; set; }
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Dictionary<string, Entry> Items = [];
    private static readonly LinkedList<string> Recent = [];
    private static long _bytes;

    public static Task<BitmapSource?> GetAsync(string url, int width = FullWidth)
    {
        lock (Items)
        {
            // A sharper copy already in memory is always good enough for a smaller slot.
            if (Touch($"{FullWidth}|{url}") is { } sharper && sharper.IsCompletedSuccessfully && sharper.Result is not null)
                return sharper;
            var key = $"{width}|{url}";
            if (Touch(key) is { } existing) return existing;
            var node = Recent.AddLast(key);
            var entry = new Entry(LoadAsync(key, url, width), node);
            Items[key] = entry;
            return entry.Task;
        }
    }

    private static Task<BitmapSource?>? Touch(string key)
    {
        if (!Items.TryGetValue(key, out var entry)) return null;
        Recent.Remove(entry.Node);
        Recent.AddLast(entry.Node);
        return entry.Task;
    }

    private static async Task<BitmapSource?> LoadAsync(string key, string url, int width)
    {
        await Task.Yield();
        BitmapSource? image = null;
        try
        {
            byte[]? bytes = null;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var address = uri!.Host.EndsWith(".gtimg.cn", StringComparison.OrdinalIgnoreCase)
                    ? new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri
                    : uri;
                bytes = await Http.GetByteArrayAsync(address);
            }
            else if (File.Exists(url))
            {
                bytes = await File.ReadAllBytesAsync(url);
            }
            if (bytes is not null) image = await Task.Run(() => Decode(bytes, width));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException
            or NotSupportedException or FileFormatException)
        {
            image = null;
        }

        lock (Items)
        {
            if (image is null)
            {
                // Failed downloads are not remembered, so the cover can load on the next visit.
                if (Items.Remove(key, out var failed)) Recent.Remove(failed.Node);
                return null;
            }
            if (Items.TryGetValue(key, out var entry))
            {
                entry.Bytes = image.PixelWidth * (long)image.PixelHeight * 4;
                _bytes += entry.Bytes;
            }
            Trim();
        }
        return image;
    }

    private static void Trim()
    {
        for (var node = Recent.First; node is not null && _bytes > ByteBudget && Recent.Count > 1;)
        {
            var next = node.Next;
            var entry = Items[node.Value];
            if (entry.Task.IsCompleted)
            {
                _bytes -= entry.Bytes;
                Items.Remove(node.Value);
                Recent.Remove(node);
            }
            node = next;
        }
    }

    public static BitmapSource Decode(byte[] bytes, int width = 0)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 0) image.DecodePixelWidth = width;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public static class Cover
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(string), typeof(Cover), new PropertyMetadata(null, OnUrlChanged));

    /// <summary>Keeps the previous artwork on screen until the next one is ready.</summary>
    public static readonly DependencyProperty KeepProperty = DependencyProperty.RegisterAttached(
        "Keep", typeof(bool), typeof(Cover), new PropertyMetadata(false));

    public static readonly DependencyProperty DecodeProperty = DependencyProperty.RegisterAttached(
        "Decode", typeof(int), typeof(Cover), new PropertyMetadata(ImageCache.FullWidth));

    public static string? GetUrl(DependencyObject o) => (string?)o.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject o, string? value) => o.SetValue(UrlProperty, value);
    public static bool GetKeep(DependencyObject o) => (bool)o.GetValue(KeepProperty);
    public static void SetKeep(DependencyObject o, bool value) => o.SetValue(KeepProperty, value);
    public static int GetDecode(DependencyObject o) => (int)o.GetValue(DecodeProperty);
    public static void SetDecode(DependencyObject o, int value) => o.SetValue(DecodeProperty, value);

    private static async void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var border = (Border)d;
        var url = e.NewValue as string;
        if (string.IsNullOrEmpty(url))
        {
            border.Background = Theme.Fg12;
            return;
        }

        var task = ImageCache.GetAsync(url, GetDecode(border));
        if (task.IsCompletedSuccessfully)
        {
            Show(border, task.Result, false);
            return;
        }

        var hadImage = GetKeep(border) && border.Background is ImageBrush;
        if (!hadImage) border.Background = Theme.Fg12;
        var image = await task;
        if (GetUrl(border) == url) Show(border, image, !hadImage);
    }

    private static void Show(Border border, BitmapSource? image, bool reveal)
    {
        if (image is null)
        {
            border.Background = Theme.Fg12;
            return;
        }
        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        border.Background = brush;
        if (reveal)
            brush.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(180)));
        else
            brush.Freeze();
    }
}

public static class Tween
{
    private static readonly Dictionary<object, Action> Running = [];

    /// <summary>True while any transition is playing.</summary>
    public static bool Busy => Running.Count > 0;

    public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    public static double EaseIn(double t) => t * t * t;
    public static double EaseInOut(double t) => t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    public static void Run(object key, double ms, Action<double> step, Func<double, double>? ease = null, Action? done = null)
    {
        if (Running.Remove(key, out var cancel)) cancel();
        ease ??= EaseOut;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        EventHandler? handler = null;
        void Stop() => CompositionTarget.Rendering -= handler;
        handler = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
            step(ease(t));
            if (t < 1) return;
            Stop();
            Running.Remove(key);
            done?.Invoke();
        };
        Running[key] = Stop;
        CompositionTarget.Rendering += handler;
        step(ease(0));
    }
}
