using System.IO;
using System.Text.Json;

namespace Silk.Core;

public sealed class AppSettings
{
    public const int DefaultLines = 9;
    public const int DefaultFont = 19;
    public const int DefaultScale = 130;
    public const int DefaultSpacing = 28;
    public const int DefaultDim = 56;

    private static string FilePath => Path.Combine(Bridge.DataDirectory, "settings.json");

    public int Glow { get; set; } = 1;
    public int Panel { get; set; }
    public int Color { get; set; }
    public int Lyric { get; set; } = 1;
    public int LyricLines { get; set; } = DefaultLines;
    public int LyricFont { get; set; }
    public int LyricScale { get; set; } = DefaultScale;
    public int LyricSpacing { get; set; } = DefaultSpacing;
    public int LyricDim { get; set; } = DefaultDim;
    public int LyricAlign { get; set; } = 1;
    public bool LyricBold { get; set; } = true;
    public bool LyricBlur { get; set; } = true;
    public int Cover { get; set; } = 1;
    public int Wave { get; set; } = 1;
    public int WaveStyle { get; set; }
    public int Quality { get; set; }
    public int Mode { get; set; }
    public int CloseAction { get; set; }
    public bool TrayHintShown { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool ShowDaily { get; set; } = true;
    public bool ShowRecent { get; set; } = true;
    public bool ShowCharts { get; set; } = true;
    public bool ShowAlbums { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()).Normalized();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings().Normalized();
        }
    }

    public AppSettings Normalized()
    {
        // Older settings stored lyric size as small, medium or large.
        if (LyricFont == 0) LyricFont = Lyric switch { 0 => 16, 2 => 23, _ => DefaultFont };
        LyricFont = Math.Clamp(LyricFont, 14, 34);
        LyricLines = Math.Clamp(LyricLines | 1, 3, 15);
        LyricScale = Math.Clamp(LyricScale, 100, 170);
        LyricSpacing = Math.Clamp(LyricSpacing, 16, 40);
        LyricDim = Math.Clamp(LyricDim, 15, 100);
        LyricAlign = Math.Clamp(LyricAlign, 0, 2);
        Cover = Math.Clamp(Cover, 0, 2);
        WaveStyle = Math.Clamp(WaveStyle, 0, 2);
        return this;
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
