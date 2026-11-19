using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using Silk.Ui;

namespace Silk.Core;

/// <summary>Offline content for layout and audio checks (<c>--demo</c>): original synth tracks and drawn covers.</summary>
public sealed class DemoSource : IMusicSource
{
    private sealed record Album(string Id, string Title, string Artist, Color Back, Color Mid, Color Light, double[] Root, string[] Lyrics);

    private static readonly Album[] Albums =
    [
        new("tide", "潮間帶", "林間信號", Color.FromRgb(9, 34, 66), Color.FromRgb(16, 92, 118), Color.FromRgb(126, 214, 214),
            [146.83, 116.54, 174.61, 130.81],
            ["退潮的時候", "腳印先學會了離開", "風把鹽留在袖口", "我把名字留給海", "燈塔還亮著", "像沒說完的一句話", "等下一次漲潮", "再替我說出來"]),
        new("noon", "赤色午後", "夏日電台", Color.FromRgb(88, 16, 24), Color.FromRgb(172, 44, 38), Color.FromRgb(255, 168, 108),
            [110, 130.81, 98, 146.83],
            ["柏油路在發燙", "收音機唱到一半", "你笑著闖紅燈", "整條街都變慢", "汽水冒著氣泡，像整個夏天都在輕輕地笑", "夏天沒有答案", "我們往西邊走", "追一個黃昏"]),
        new("mist", "晨霧", "小島日記", Color.FromRgb(226, 213, 194), Color.FromRgb(192, 205, 204), Color.FromRgb(255, 251, 240),
            [196, 164.81, 220, 146.83],
            ["窗外還沒有醒", "霧替山收好輪廓", "水壺輕輕地響", "日子重新開始", "你留下的杯子", "還有一點溫熱", "今天不必趕路", "慢慢走就好"]),
        new("night", "夜行列車", "月台樂隊", Color.FromRgb(16, 42, 38), Color.FromRgb(28, 110, 88), Color.FromRgb(210, 226, 140),
            [123.47, 98, 146.83, 110],
            ["末班車進站了", "窗上有城市的影子", "我數著隧道的燈", "數到你的名字", "下一站還很遠", "耳機裡下著雨", "如果天亮以前", "能夠到達這裡"]),
        new("violet", "紫色回音", "第七個房間", Color.FromRgb(52, 34, 92), Color.FromRgb(122, 76, 170), Color.FromRgb(240, 180, 214),
            [138.59, 103.83, 155.56, 123.47],
            ["走廊盡頭有光", "門縫裡傳來鋼琴", "我喊了一聲", "回音比我更輕", "牆上的時鐘", "忘記往前走", "這一晚這麼長", "夠我慢慢地懂"]),
    ];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "silk-demo-v2");

    public DemoSource() => Directory.CreateDirectory(_directory);

    private readonly HashSet<string> _liked = [];

    public bool IsSignedIn => true;

    public string StreamQuality { get; set; } = "best";

    public async Task<IReadOnlyList<Song>> GetRecommendedSongsAsync(CancellationToken ct = default)
    {
        await Task.Delay(180, ct);
        return Albums.SelectMany(a => TracksOf(a).Skip(1).Take(2)).Take(8).ToList();
    }

    public async Task<IReadOnlyList<Collection>> GetChartsAsync(CancellationToken ct = default)
    {
        await Task.Delay(180, ct);
        string[] names = ["飆升榜", "熱歌榜", "新歌榜", "流行榜", "搖滾榜"];
        return Albums.Select((a, i) => new Collection(CollectionKind.Toplist, a.Id, names[i], "100 首", CoverPath(a))).ToList();
    }

    public async Task<IReadOnlyList<Collection>> GetNewAlbumsAsync(CancellationToken ct = default)
    {
        await Task.Delay(180, ct);
        return Albums.AsEnumerable().Reverse()
            .Select(a => new Collection(CollectionKind.Album, a.Id, a.Title, a.Artist, CoverPath(a))).ToList();
    }

    public Task<IReadOnlySet<string>> GetLikedIdsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(_liked.ToHashSet());

    public async Task SetLikedAsync(Song song, bool liked, CancellationToken ct = default)
    {
        await Task.Delay(120, ct);
        if (liked) _liked.Add(song.Mid); else _liked.Remove(song.Mid);
    }

    public Task<UserProfile?> GetProfileAsync(CancellationToken ct = default) =>
        Task.FromResult<UserProfile?>(new UserProfile("晚風", ""));

    public Task<IReadOnlyList<Collection>> GetHomeAsync(CancellationToken ct = default) => GetLibraryAsync(ct);

    // The delay stands in for the network so page transitions are tested under realistic timing.
    public async Task<IReadOnlyList<Collection>> GetLibraryAsync(CancellationToken ct = default)
    {
        await Task.Delay(280, ct);
        return Albums
            .Select(a => new Collection(CollectionKind.Album, a.Id, a.Title, a.Artist, CoverPath(a)))
            .ToList();
    }

    public async Task<IReadOnlyList<Song>> GetLikedAsync(CancellationToken ct = default)
    {
        await Task.Delay(280, ct);
        return Albums.SelectMany(TracksOf).Take(12).ToList();
    }

    public Task<IReadOnlyList<Song>> GetSongsAsync(Collection collection, CancellationToken ct = default)
    {
        var album = Albums.First(a => a.Id == collection.Id);
        return Task.FromResult<IReadOnlyList<Song>>(TracksOf(album).ToList());
    }

    public Task<IReadOnlyList<Song>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        IReadOnlyList<Song> songs = Albums.SelectMany(TracksOf)
            .Where(s => s.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || s.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult(songs);
    }

    public async Task<IReadOnlyList<Collection>> SearchSingersAsync(string keyword, CancellationToken ct = default)
    {
        await Task.Delay(180, ct);
        return Albums.Where(a => a.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(a => new Collection(CollectionKind.Singer, a.Id, a.Artist, "4 首", CoverPath(a))).ToList();
    }

    public async Task<IReadOnlyList<Collection>> SearchCollectionsAsync(string keyword, CollectionKind kind, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        var matches = Albums.Where(a => a.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || a.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        return kind == CollectionKind.Album
            ? matches.Select(a => new Collection(CollectionKind.Album, a.Id, a.Title, $"{a.Artist} · 2026", CoverPath(a))).ToList()
            : matches.Select(a => new Collection(CollectionKind.Playlist, a.Id, $"{a.Title}的午後歌單", "4 首 · 晚風", CoverPath(a))).ToList();
    }

    public async Task<Detail> GetSingerAsync(string mid, CancellationToken ct = default)
    {
        await Task.Delay(160, ct);
        var album = Albums.First(a => a.Id == mid);
        var others = Albums.Where(a => a != album).Take(2)
            .Select(a => new Collection(CollectionKind.Album, a.Id, a.Title, "2025", CoverPath(a)));
        IReadOnlyList<Collection> albums = [new Collection(CollectionKind.Album, album.Id, album.Title, "2026", CoverPath(album)), .. others];
        return new Detail(album.Artist, CoverPath(album), true, "示範歌手 · 2026",
            $"{album.Artist}是示範內容裡的虛構樂團，用來檢查歌手頁的版面。\n這段簡介故意寫得長一些，好確認收合與展開都正常：簡介只顯示前幾行，按下「展開」才看得到全部內容。",
            albums, null);
    }

    public async Task<Detail> GetAlbumAsync(string mid, CancellationToken ct = default)
    {
        await Task.Delay(160, ct);
        var album = Albums.First(a => a.Id == mid);
        return new Detail(album.Title, CoverPath(album), false, $"{album.Artist} · 2026-05-20 · 流行 · 示範唱片",
            $"《{album.Title}》是示範用的虛構專輯，四首曲目都由程式即時合成。", [],
            new Collection(CollectionKind.Singer, album.Id, album.Artist, "歌手", CoverPath(album)));
    }

    public async Task<string?> GetStreamAsync(Song song, CancellationToken ct = default)
    {
        var album = Albums.First(a => song.Mid.StartsWith(a.Id, StringComparison.Ordinal));
        var index = int.Parse(song.Mid[(album.Id.Length + 1)..]);
        var path = Path.Combine(_directory, $"{song.Mid}.wav");
        if (!File.Exists(path)) await Task.Run(() => Synthesize(path, album, index), ct);
        return path;
    }

    public Task<string?> GetAnalysisStreamAsync(Song song, CancellationToken ct = default) => GetStreamAsync(song, ct);

    public Task<IReadOnlyList<LyricEntry>> GetLyricsAsync(Song song, CancellationToken ct = default)
    {
        var album = Albums.First(a => song.Mid.StartsWith(a.Id, StringComparison.Ordinal));
        // A song-like shape: credits, a short intro, eight lines, a marked break, then the last four lines again.
        List<LyricEntry> lines = [new(0, $"作詞：{album.Artist}"), new(1.2, $"作曲：{album.Artist}")];
        lines.AddRange(album.Lyrics.Select((text, i) => new LyricEntry(4.6 + i * 2.9, text)));
        lines.Add(new LyricEntry(27.4, ""));
        lines.AddRange(album.Lyrics.Skip(4).Select((text, i) => new LyricEntry(33 + i * 2.5, text)));
        return Task.FromResult<IReadOnlyList<LyricEntry>>(lines);
    }

    public Task<QrTicket> CreateQrAsync(string channel, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<QrStatus> CheckQrAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(new QrStatus(801, ""));

    public Task SignOutAsync() => Task.CompletedTask;

    private IEnumerable<Song> TracksOf(Album album)
    {
        string[] suffix = ["", "（夜版）", "（現場）", "（鋼琴）"];
        return Enumerable.Range(0, 4).Select(i => new Song(
            $"{album.Id}-{i}", album.Title + suffix[i], album.Artist, album.Title, album.Id, 42, CoverPath(album), SingerMid: album.Id));
    }

    private string CoverPath(Album album)
    {
        var path = Path.Combine(_directory, $"{album.Id}.png");
        if (File.Exists(path)) return path;
        const int size = 720;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var seed = Array.IndexOf(Albums, album);
            dc.DrawRectangle(new SolidColorBrush(album.Back), null, new Rect(0, 0, size, size));
            var center = new Point(size * (.3 + seed * .08), size * (.62 - seed * .05));
            dc.DrawEllipse(new SolidColorBrush(album.Mid), null, center, size * .46, size * .46);
            dc.DrawEllipse(new SolidColorBrush(album.Light), null, new Point(center.X + size * .34, center.Y - size * .34), size * .17, size * .17);
            var wave = new StreamGeometry();
            using (var g = wave.Open())
            {
                var y = size * (.78 - seed * .03);
                g.BeginFigure(new Point(0, y), true, true);
                g.BezierTo(new Point(size * .25, y - 130), new Point(size * .42, y + 90), new Point(size * .6, y), true, true);
                g.BezierTo(new Point(size * .75, y - 70), new Point(size * .9, y - 60), new Point(size, y - 20), true, true);
                g.LineTo(new Point(size, size), true, true);
                g.LineTo(new Point(0, size), true, true);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(190, album.Back.R, album.Back.G, album.Back.B)), null, wave);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private static void Synthesize(string path, Album album, int variant)
    {
        const int rate = 44100;
        const double seconds = 42, bpm = 98;
        var beat = 60 / bpm;
        var length = (int)(seconds * rate);
        var left = new float[length];
        var right = new float[length];
        var random = new Random(album.Id.Length * 31 + variant);
        double[] ratios = [1, 1.2599, 1.4983, 2];
        (double At, double Gain)[] arrangement =
        [
            (0, .12), (5 + variant, .34), (11 + variant, .58), (17 + variant, 1), (24 + variant, .3),
            (29 + variant, 1), (36, .42), (42, .1),
        ];
        double GainAt(double time)
        {
            for (var s = 0; s < arrangement.Length - 1; s++)
            {
                var (from, a) = arrangement[s];
                var (to, b) = arrangement[s + 1];
                if (time > to) continue;
                var x = Math.Clamp((time - from) / Math.Min(1.6, to - from), 0, 1);
                return a + (b - a) * x * x * (3 - 2 * x);
            }
            return arrangement[^1].Gain;
        }

        for (var i = 0; i < length; i++)
        {
            var t = i / (double)rate;
            var beats = t / beat;
            var bar = (int)(beats / 4) % album.Root.Length;
            var root = album.Root[bar] * (variant == 2 ? 1.5 : 1);
            var pan = Math.Sin(t * .7) * .4;
            double pad = 0;
            foreach (var ratio in ratios)
                for (var h = 1; h <= 4; h++)
                    pad += Math.Sin(Math.Tau * root * ratio * h * t * (1 + .0015 * h)) / (h * h * 1.0);
            pad *= .035 * (.75 + .25 * Math.Sin(t * 1.3));
            var bass = Math.Sin(Math.Tau * root / 2 * t) * .22;

            var step = beats * 2;
            var arpIndex = (int)step;
            var arpPhase = step - arpIndex;
            var arpPitch = root * 2 * ratios[(arpIndex * (variant + 1)) % ratios.Length];
            var arp = (Math.Sin(Math.Tau * arpPitch * t) + .4 * Math.Sin(Math.Tau * arpPitch * 2.01 * t))
                * Math.Exp(-arpPhase * 3.4) * .13;
            var arpPan = arpIndex % 2 == 0 ? -.65 : .65;

            var beatPhase = (beats % 2) * beat;
            var kick = Math.Sin(Math.Tau * (48 * beatPhase + 34 * (1 - Math.Exp(-beatPhase * 24)))) * Math.Exp(-beatPhase * 9) * .42;
            var hatPhase = (beats + .5) % 1 * beat;
            var hat = (random.NextDouble() * 2 - 1) * Math.Exp(-hatPhase * 42) * .09;

            var l = pad * (1 - pan) + bass + kick + arp * (1 - arpPan) * .75 + hat * 1.2;
            var r = pad * (1 + pan) + bass + kick + arp * (1 + arpPan) * .75 + hat * .6;
            var fade = Math.Min(1, Math.Min(t / .4, (seconds - t) / .8));
            var gain = GainAt(t);
            left[i] = (float)(Math.Tanh(l) * fade * .8 * gain);
            right[i] = (float)(Math.Tanh(r) * fade * .8 * gain);
        }

        var temporary = path + ".tmp";
        using (var writer = new WaveFileWriter(temporary, new WaveFormat(rate, 16, 2)))
        {
            var frame = new float[2];
            for (var i = 0; i < length; i++)
            {
                frame[0] = left[i];
                frame[1] = right[i];
                writer.WriteSamples(frame, 0, 2);
            }
        }
        File.Move(temporary, path, true);
    }
}
