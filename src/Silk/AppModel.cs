using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Silk.Core;
using Silk.Ui;

namespace Silk;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class CardItem : Observable
{
    private double _width;
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Cover { get; init; }
    public Collection? Collection { get; init; }
    public Song? Song { get; init; }
    public bool IsHero { get; init; }
    public IReadOnlyList<Song>? PlayQueue { get; init; }
    public int PlayIndex { get; init; }
    public double Width { get => _width; set => Set(ref _width, value); }
}

/// <summary>The top of a singer or album page. It is the first item of the song list, so it scrolls with the songs.</summary>
public sealed class DetailHeader(Detail detail, IReadOnlyList<CardItem> albums, bool isSinger) : Observable
{
    private const double CollapsedDescription = 60;
    private bool _expanded;

    public Detail Detail { get; } = detail;
    public IReadOnlyList<CardItem> Albums { get; } = albums;
    public bool HasAlbums => Albums.Count > 0;
    public CornerRadius CoverCorner => new(Detail.RoundCover ? 70 : 22);
    public bool HasMeta => Detail.Meta.Length > 0;
    public bool HasDescription => Detail.Description.Length > 0;
    public bool CanExpand => Detail.Description.Length > 80 || Detail.Description.Contains('\n');
    public bool HasSinger => Detail.Singer is not null;
    public string SingerText => Detail.Singer is { } singer ? $"歌手：{singer.Title}" : "";
    public bool ShowSongHeading => isSinger;
    public double DescriptionHeight => _expanded ? double.PositiveInfinity : CollapsedDescription;
    public string ExpandText => _expanded ? "收合" : "展開";

    public void ToggleExpanded()
    {
        _expanded = !_expanded;
        Raise(nameof(DescriptionHeight));
        Raise(nameof(ExpandText));
    }
}

/// <summary>The matching singers above search results.</summary>
public sealed class SingerHeader(IReadOnlyList<CardItem> singers)
{
    public IReadOnlyList<CardItem> Singers { get; } = singers;
}

public sealed class SongRow(Song song, int number, IReadOnlyList<Song>? queue = null) : Observable
{
    private bool _isCurrent;
    private bool _isLiked;
    private int _number = number;
    public Song Song { get; } = song;
    public int Number { get => _number; set => Set(ref _number, value); }
    public string LikeMenuText => IsLiked ? "取消收藏" : "收藏";
    public IReadOnlyList<Song> Queue { get; } = queue ?? [song];
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    public string LikeLabel => $"{(IsLiked ? "取消收藏" : "收藏")} {Song.Title}";

    public bool IsLiked
    {
        get => _isLiked;
        set
        {
            if (!Set(ref _isLiked, value)) return;
            Raise(nameof(LikeLabel));
            Raise(nameof(LikeMenuText));
        }
    }
}

/// <summary>A timed lyric line. Empty text marks where the singing stops in the lyric file.</summary>
public sealed class LyricLine(LyricEntry entry, bool timed = true, bool isCredit = false)
{
    private static readonly string[] CreditRoles =
    [
        "詞", "词", "曲", "編曲", "编曲", "製作", "制作", "監製", "监制", "混音", "和聲", "和声", "錄音", "录音", "母帶", "母带",
        "吉他", "貝斯", "贝斯", "鼓", "鍵盤", "键盘", "弦樂", "弦乐", "出品", "發行", "发行", "企劃", "企划", "統籌", "统筹",
        "OP", "SP", "Lyric", "Compose", "Arrange", "Produce", "Mix", "Master",
    ];

    public string Text { get; } = entry.Text;
    public double Time { get; } = entry.Time;
    /// <summary>False for a placeholder message or lyrics without timestamps; they are shown but cannot be clicked to seek.</summary>
    public bool Timed { get; } = timed;
    /// <summary>A title or "lyrics by" line at the start, shown small and never highlighted.</summary>
    public bool IsCredit { get; } = isCredit;

    /// <summary>
    /// Turns lyric entries into lines. Leading title and credit lines are marked as credits,
    /// and lyrics whose lines all share one timestamp are treated as plain text.
    /// </summary>
    public static List<LyricLine> From(IReadOnlyList<LyricEntry> entries)
    {
        var timed = entries.Count < 2 || entries.Select(e => e.Time).Distinct().Count() > 1;
        var credits = 0;
        while (credits < entries.Count && credits < 12 && IsCreditText(entries[credits].Text, credits)) credits++;
        // Only a run that is followed by real lyrics counts; a song of credit-like lines is left alone.
        if (credits == entries.Count) credits = 0;
        return entries.Select((e, i) => new LyricLine(e, timed, i < credits && e.Text.Length > 0)).ToList();
    }

    private static bool IsCreditText(string text, int index)
    {
        if (text.Length == 0) return index > 0;
        if (index == 0 && text.Contains(" - ", StringComparison.Ordinal)) return true;
        var colon = text.IndexOfAny(['：', ':']);
        if (colon is <= 0 or > 14) return false;
        var role = text[..colon];
        return CreditRoles.Any(r => role.Contains(r, StringComparison.OrdinalIgnoreCase));
    }
}

public enum Page { Home, Library, Liked, Collection, Search, Login, Message, Settings }

public sealed record AppOptions(bool Demo, bool Muted, bool Autoplay, string? StartView, string? StartAlbum, bool SingleInstance)
{
    public static AppOptions Parse(string[] args)
    {
        var view = args.FirstOrDefault(a => a.StartsWith("--view=", StringComparison.Ordinal))?["--view=".Length..];
        var album = args.FirstOrDefault(a => a.StartsWith("--album=", StringComparison.Ordinal))?["--album=".Length..];
        var demo = args.Contains("--demo");
        return new AppOptions(demo, args.Contains("--mute"), args.Contains("--autoplay"), view, album, !demo || args.Contains("--single"));
    }
}

public sealed class AppModel : Observable, IDisposable
{
    private static readonly string StateFile = Path.Combine(Bridge.DataDirectory, "state.json");
    private static readonly string WaveDirectory = Path.Combine(Bridge.DataDirectory, "waves");
    private static readonly string RecentFile = Path.Combine(Bridge.DataDirectory, "recent.json");
    private const int RecentLimit = 16;
    private static readonly string[] Channels = ["qq", "wechat", "qqmusic"];
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(2);

    private sealed class Cached<T>(IReadOnlyList<T> items)
    {
        private readonly DateTime _loadedAt = DateTime.UtcNow;
        public IReadOnlyList<T> Items { get; } = items;
        public bool IsFresh => DateTime.UtcNow - _loadedAt < RefreshAfter;
    }

    private sealed record HomeFeed(IReadOnlyList<Song> Songs, IReadOnlyList<Collection> Charts, IReadOnlyList<Collection> Albums)
    {
        public DateTime LoadedAt { get; } = DateTime.UtcNow;
        public bool IsFresh => DateTime.UtcNow - LoadedAt < RefreshAfter;
    }

    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly AppSettings _settings;
    private readonly HashSet<string> _likedIds = [];
    private readonly Dictionary<string, bool> _likeWanted = [];
    private List<Song> _recentSongs = [];
    private HomeFeed? _feed;
    private Palette? _coverPalette;
    private DispatcherTimer? _saveTimer;
    private sealed record SearchExtras(IReadOnlyList<Collection> Singers, IReadOnlyList<Collection> Albums, IReadOnlyList<Collection> Playlists)
    {
        public bool Any => Singers.Count + Albums.Count + Playlists.Count > 0;
    }

    private static readonly SearchExtras NoExtras = new([], [], []);
    private static readonly string SearchHistoryFile = Path.Combine(Bridge.DataDirectory, "search.json");
    private const int SearchHistoryLimit = 12;
    private readonly Dictionary<string, SearchExtras> _searchCache = [];
    private SearchExtras _searchExtras = NoExtras;
    private int _searchTab;
    private Collection? _currentCollection;
    private readonly Stack<Collection> _trail = new();
    private readonly Dictionary<string, Detail> _detailCache = [];
    private string _searchKeyword = "";
    private Page _searchOrigin = Page.Home;
    private readonly AppOptions _options;
    private Bridge? _bridge;
    private SystemMedia? _media;
    private IMusicSource? _source;
    private CancellationTokenSource _pageCts = new();
    private CancellationTokenSource _trackCts = new();
    private CancellationTokenSource? _loginCts;
    private IReadOnlyList<Song> _visibleSongs = [];
    private string? _songsKey;
    private Cached<Collection>? _homeCache;
    private Cached<Collection>? _libraryCache;
    private readonly Dictionary<string, Cached<Song>> _songCache = [];

    private double[]? _waveHeights;
    private Page _returnPage = Page.Home;
    private bool _streamOpened;
    private int _failures;
    private double _clock;
    private double _coverHeight = 300;
    private double _heroWidth = 300;
    private double _smallWidth = 200;

    private Page _page = Page.Message;
    private string _pageTitle = "";
    private string _pageSubtitle = "";
    private string _message = "正在準備";
    private bool _canRetry;
    private bool _busy = true;
    private bool _canGoBack;
    private string _userName = "";
    private string _userSubtitle = "";
    private string _avatar = "";
    private bool _isSignedIn;
    private BitmapSource? _qrImage;
    private string _loginHint = "";
    private int _channel;
    private Song? _current;
    private bool _isPlaying;
    private double _position;
    private double _duration;
    private double _progress;
    private string _positionText = "0:00";
    private string _durationText = "0:00";

    private string _toast = "";

    public ObservableCollection<CardItem> Cards { get; } = [];
    public ObservableCollection<CardItem> Library { get; } = [];
    public ObservableCollection<SongRow> Songs { get; } = [];
    public ObservableCollection<SongRow> Queue { get; } = [];
    public ObservableCollection<LyricLine> Lyrics { get; } = [];
    public ObservableCollection<SongRow> Recommended { get; } = [];
    public ObservableCollection<CardItem> Recent { get; } = [];
    public ObservableCollection<CardItem> Charts { get; } = [];
    public ObservableCollection<CardItem> NewAlbums { get; } = [];
    public ObservableCollection<CardItem> Singers { get; } = [];
    /// <summary>Items shown above the songs in the song list: a singer or album header, or matching singers.</summary>
    public ObservableCollection<object> SongsHeader { get; } = [];
    public ObservableCollection<CardItem> SearchCollections { get; } = [];
    public ObservableCollection<string> SearchHistory { get; } = [];

    public AudioEngine Audio { get; }


    public event Action? ToastRaised;
    /// <summary>A song's album or singer was opened from a menu, which may sit over the player.</summary>
    public event Action? NavigationRequested;
    public event Action? PlayerRequested;
    public event Action? SongsReset;
    public event Action? LyricLayoutChanged;

    public AppModel(AppOptions options)
    {
        _options = options;
        _settings = options.Demo ? new AppSettings().Normalized() : AppSettings.Load();
        if (!options.SingleInstance) _settings.CloseAction = 1;
        ApplyAppearance();
        Audio = new AudioEngine(options.Muted);
        Audio.Ended += () => _ui.BeginInvoke(OnTrackEnded);
        Audio.Failed += _ => _ui.BeginInvoke(HandleTrackFailure);
        Audio.DurationKnown += d => _ui.BeginInvoke(() =>
        {
            if (d <= 1) return;
            Duration = d;
            DurationText = Song.Format(d);
        });
        Audio.DeviceOpened += () => _ui.BeginInvoke(RefreshOutput);
        RefreshOutput();
    }

    public Page Page
    {
        get => _page;
        private set
        {
            if (!Set(ref _page, value)) return;
            Raise(nameof(IsCardsView));
            Raise(nameof(IsGridView));
            Raise(nameof(IsSongsView));
            Raise(nameof(IsLoginView));
            Raise(nameof(IsMessageView));
            Raise(nameof(IsSettingsView));
            RaiseSearchTabs();
            Raise(nameof(NavIndex));
        }
    }

    public bool IsCardsView => Page == Page.Home;
    public bool IsGridView => Page == Page.Library;
    public bool IsSongsView => Page is Page.Liked or Page.Collection or Page.Search;
    public bool IsLoginView => Page == Page.Login;
    public bool IsMessageView => Page == Page.Message;
    public bool IsSettingsView => Page == Page.Settings;
    public bool ShowSearchTabs => Page == Page.Search;
    public bool ShowSongList => Page != Page.Search || _searchTab == 0;
    public bool ShowCollectionGrid => Page == Page.Search && _searchTab != 0;
    public int SearchTab => _searchTab;
    public bool ShowDailySection => _settings.ShowDaily && Recommended.Count > 0;
    public bool ShowRecentSection => _settings.ShowRecent && Recent.Count > 0;
    public bool ShowChartsSection => _settings.ShowCharts && Charts.Count > 0;
    public bool ShowAlbumsSection => _settings.ShowAlbums && NewAlbums.Count > 0;
    public double LyricLines
    {
        get => _settings.LyricLines;
        set => ChangeLyric(() => _settings.LyricLines = Math.Clamp((int)Math.Round(value) | 1, 3, 15));
    }

    public double LyricFontSize
    {
        get => _settings.LyricFont;
        set => ChangeLyric(() => _settings.LyricFont = (int)Math.Round(value));
    }

    public double LyricScale
    {
        get => _settings.LyricScale;
        set => ChangeLyric(() => _settings.LyricScale = (int)Math.Round(value));
    }

    public double LyricSpacing
    {
        get => _settings.LyricSpacing / 10d;
        set => ChangeLyric(() => _settings.LyricSpacing = (int)Math.Round(value * 10));
    }

    public double LyricDim
    {
        get => _settings.LyricDim;
        set => ChangeLyric(() => _settings.LyricDim = (int)Math.Round(value));
    }

    // The row must stay tall enough that an enlarged current line never touches its neighbours.
    public double LyricLineHeight => Math.Round(LyricFontSize * Math.Max(LyricSpacing, LyricScale / 100 * 1.25));
    public LyricStyle LyricStyle => new(LyricFontSize, LyricScale / 100, LyricSpacing, LyricDim / 100,
        _settings.LyricAlign, _settings.LyricBold, _settings.LyricBlur);
    public double CoverScale => Pick(_settings.Cover, .8d, 1d, 1.14d);
    public double PlayerWaveHeight => Pick(_settings.Wave, 40d, 62d, 90d);
    public bool WaveBars => _settings.WaveStyle == 1;
    /// <summary>A plain progress line instead of the song's loudness.</summary>
    public bool WavePlain => _settings.WaveStyle == 2;
    public double PillWaveHeight => Pick(_settings.Wave, 22d, 30d, 40d);
    public int PlayMode => Math.Clamp(_settings.Mode, 0, 2);
    public bool CloseToTray => _settings.CloseAction == 0;
    public string ModeLabel => Pick(PlayMode, "清單循環", "單曲循環", "隨機播放");
    public bool CurrentLiked => Current is { } song && _likedIds.Contains(song.Mid);
    public string LikeLabel => CurrentLiked ? "取消收藏" : "收藏";
    // A singer, album or playlist keeps the section it was opened from highlighted.
    public int NavIndex => Page switch
    {
        Page.Library => 1,
        Page.Liked => 2,
        Page.Settings => 3,
        Page.Collection or Page.Message when _currentCollection is not null => _returnPage switch { Page.Library => 1, Page.Liked => 2, _ => 0 },
        _ => 0,
    };

    public string PageTitle { get => _pageTitle; private set => Set(ref _pageTitle, value); }
    public string PageSubtitle { get => _pageSubtitle; private set => Set(ref _pageSubtitle, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool CanRetry { get => _canRetry; private set => Set(ref _canRetry, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public bool CanGoBack { get => _canGoBack; private set => Set(ref _canGoBack, value); }
    public string UserName { get => _userName; private set => Set(ref _userName, value); }
    public string UserSubtitle { get => _userSubtitle; private set => Set(ref _userSubtitle, value); }
    public string Avatar { get => _avatar; private set => Set(ref _avatar, value); }
    public bool IsSignedIn { get => _isSignedIn; private set => Set(ref _isSignedIn, value); }
    public BitmapSource? QrImage { get => _qrImage; private set => Set(ref _qrImage, value); }
    public string LoginHint { get => _loginHint; private set => Set(ref _loginHint, value); }
    public int Channel { get => _channel; private set => Set(ref _channel, value); }
    public string Toast { get => _toast; private set => Set(ref _toast, value); }

    public Song? Current
    {
        get => _current;
        private set
        {
            if (!Set(ref _current, value)) return;
            Raise(nameof(HasTrack));
            Raise(nameof(CurrentCover));
            Raise(nameof(CurrentLiked));
            Raise(nameof(LikeLabel));
            Raise(nameof(CurrentTitle));
            Raise(nameof(CurrentArtist));
        }
    }

    public bool HasTrack => Current is not null;
    public string CurrentCover => Current?.Cover ?? "";
    public string CurrentTitle => Current?.Title ?? "還沒有播放";
    public string CurrentArtist => Current?.Artist ?? "選一首歌開始";
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public double Position { get => _position; private set => Set(ref _position, value); }
    public double Duration { get => _duration; private set => Set(ref _duration, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string PositionText { get => _positionText; private set => Set(ref _positionText, value); }
    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }
    /// <summary>The playback position read fresh each frame, for motion that has to follow the music closely.</summary>
    public double PlaybackPosition => _streamOpened ? Audio.Position : Position;
    public double[]? WaveHeights { get => _waveHeights; private set => Set(ref _waveHeights, value); }
    public int QueueIndex { get; private set; } = -1;

    public async Task StartAsync()
    {
        try
        {
            _media = new SystemMedia();
            _media.Toggle += () => _ui.BeginInvoke(() => Toggle());
            _media.Next += () => _ui.BeginInvoke(() => Next());
            _media.Previous += () => _ui.BeginInvoke(() => Previous());
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or TypeLoadException or UnauthorizedAccessException)
        {
            _media = null;
        }

        await ConnectAsync();
    }

    public async Task RetryAsync()
    {
        if (_source is null) await ConnectAsync();
        else await EnterAsync();
    }

    private async Task ConnectAsync()
    {
        ShowMessage("正在連線", busy: true);
        try
        {
            if (_options.Demo)
            {
                _source = new DemoSource();
            }
            else
            {
                _bridge = await Bridge.StartAsync();
                _source = new QqMusicSource(_bridge);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception
            or OperationCanceledException or FormatException)
        {
            ShowMessage("音樂服務無法啟動。請確認已安裝 Node.js，然後再試一次。", canRetry: true);
            return;
        }
        await EnterAsync();
    }

    private async Task EnterAsync()
    {
        ShowMessage("正在連線", busy: true);
        _source!.StreamQuality = Pick(_settings.Quality, "best", "320", "128");
        UserProfile? profile = null;
        try
        {
            profile = await _source!.GetProfileAsync();
        }
        catch (Exception e) when (IsLoadFailure(e))
        {
            if (_source!.IsSignedIn)
            {
                ShowMessage("連不上 QQ 音樂，請檢查網路。", canRetry: true);
                return;
            }
        }

        if (profile is null)
        {
            IsSignedIn = false;
            UserName = "尚未登入";
            UserSubtitle = "掃碼後開始聽歌";
            Avatar = "";
            await ShowLoginAsync(Channel);
            return;
        }

        IsSignedIn = true;
        UserName = profile.Name;
        UserSubtitle = "QQ 音樂";
        Avatar = profile.AvatarUrl;
        RestoreLastSong();
        LoadRecent();
        LoadSearchHistory();
        _ = LoadLikedIdsAsync();
        await ShowHomeAsync();

        if (_options.Autoplay && Cards.FirstOrDefault(c => c.Collection is not null && (_options.StartAlbum is null ? !c.IsHero : c.Title == _options.StartAlbum))?.Collection is { } first)
        {
            var songs = await _source.GetSongsAsync(first);
            if (songs.Count > 0) await PlayAsync(songs, 0);
        }
    }

    public async Task ShowHomeAsync()
    {
        if (!RequireSignIn()) return;
        var ct = BeginPage(Page.Home, Greeting(), Current is null ? "今天想聽點什麼" : $"接著聽《{Current.Title}》");
        _ = RefreshFeedAsync(ct);
        if (_homeCache is { } cached)
        {
            ApplyHome(cached.Items);
            Busy = false;
            if (cached.IsFresh) return;
        }
        try
        {
            var items = await _source!.GetHomeAsync(ct);
            ct.ThrowIfCancellationRequested();
            _homeCache = new Cached<Collection>(items);
            ApplyHome(items);
            EndPage(Cards.Count == 0 ? "這裡還沒有內容" : null);
        }
        catch (Exception e) when (IsLoadFailure(e) && !ct.IsCancellationRequested)
        {
            if (_homeCache is null) ShowMessage("首頁載入失敗，請稍後再試。", canRetry: true);
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyHome(IReadOnlyList<Collection> items)
    {
        var next = new List<CardItem>();
        if (Current is { } song)
            next.Add(new CardItem { Title = "繼續聽", Subtitle = song.Title, Cover = song.Cover, Song = song, IsHero = true });
        foreach (var item in items)
        {
            next.Add(new CardItem
            {
                Title = item.Title,
                Subtitle = item.Subtitle,
                Cover = item.Cover,
                Collection = item,
                IsHero = next.Count == 0,
            });
        }
        SyncCards(Cards, next);
        ApplyCardSizes();
        ApplyRecent();
    }

    private async Task RefreshFeedAsync(CancellationToken ct)
    {
        if (_feed is { } cached)
        {
            ApplyFeed(cached);
            if (cached.IsFresh) return;
        }

        var songs = SafeListAsync(() => _source!.GetRecommendedSongsAsync(ct));
        var charts = SafeListAsync(() => _source!.GetChartsAsync(ct));
        var albums = SafeListAsync(() => _source!.GetNewAlbumsAsync(ct));
        await Task.WhenAll(songs, charts, albums);
        if (ct.IsCancellationRequested) return;
        _feed = new HomeFeed(songs.Result, charts.Result, albums.Result);
        ApplyFeed(_feed);
    }

    private static async Task<IReadOnlyList<T>> SafeListAsync<T>(Func<Task<IReadOnlyList<T>>> load)
    {
        try { return await load(); }
        catch (Exception e) when (IsLoadFailure(e) || e is OperationCanceledException) { return []; }
    }

    private void ApplyFeed(HomeFeed feed)
    {
        if (!Recommended.Select(r => r.Song.Mid).SequenceEqual(feed.Songs.Select(s => s.Mid)))
        {
            Recommended.Clear();
            var number = 1;
            foreach (var song in feed.Songs) Recommended.Add(CreateRow(song, number++, feed.Songs));
        }
        SyncCards(Charts, ToCards(feed.Charts));
        SyncCards(NewAlbums, ToCards(feed.Albums));
        RaiseSections();
    }

    private static List<CardItem> ToCards(IReadOnlyList<Collection> items) => items
        .Select(item => new CardItem { Title = item.Title, Subtitle = item.Subtitle, Cover = item.Cover, Collection = item })
        .ToList();

    private SongRow CreateRow(Song song, int number, IReadOnlyList<Song> queue) => new(song, number, queue)
    {
        IsCurrent = song.Mid == Current?.Mid,
        IsLiked = _likedIds.Contains(song.Mid),
    };

    private void RaiseSections()
    {
        Raise(nameof(ShowDailySection));
        Raise(nameof(ShowRecentSection));
        Raise(nameof(ShowChartsSection));
        Raise(nameof(ShowAlbumsSection));
    }

    private void ApplyRecent()
    {
        var songs = _recentSongs.Where(s => s.Mid != Current?.Mid).ToList();
        SyncCards(Recent, songs.Select((song, index) => new CardItem
        {
            Title = song.Title, Subtitle = song.Artist, Cover = song.Cover, Song = song, PlayQueue = songs, PlayIndex = index,
        }).ToList());
        Raise(nameof(ShowRecentSection));
    }

    private void RecordRecent(Song song)
    {
        _recentSongs.RemoveAll(s => s.Mid == song.Mid);
        _recentSongs.Insert(0, song);
        if (_recentSongs.Count > RecentLimit) _recentSongs.RemoveRange(RecentLimit, _recentSongs.Count - RecentLimit);
        ApplyRecent();
        if (_options.Demo) return;
        try { File.WriteAllText(RecentFile, JsonSerializer.Serialize(_recentSongs)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void LoadRecent()
    {
        if (_options.Demo) return;
        try { _recentSongs = JsonSerializer.Deserialize<List<Song>>(File.ReadAllText(RecentFile)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _recentSongs = []; }
    }

    private async Task LoadLikedIdsAsync()
    {
        try
        {
            var ids = await _source!.GetLikedIdsAsync();
            _likedIds.Clear();
            _likedIds.UnionWith(ids);
            RefreshLikedRows();
        }
        catch (Exception e) when (IsLoadFailure(e)) { }
    }

    private void RefreshLikedRows()
    {
        foreach (var row in Songs.Concat(Queue).Concat(Recommended)) row.IsLiked = _likedIds.Contains(row.Song.Mid);
        Raise(nameof(CurrentLiked));
        Raise(nameof(LikeLabel));
    }

    public void ToggleCurrentLike()
    {
        if (Current is { } song) _ = ToggleLikeAsync(song);
    }

    public async Task ToggleLikeAsync(Song song)
    {
        if (_source is null) return;
        var wanted = !_likedIds.Contains(song.Mid);
        SetLikedLocally(song.Mid, wanted);
        var alreadySending = _likeWanted.ContainsKey(song.Mid);
        _likeWanted[song.Mid] = wanted;
        if (alreadySending) return;

        try
        {
            // Quick repeated taps collapse into the last choice; each write runs one at a time.
            bool sent;
            do
            {
                sent = _likeWanted[song.Mid];
                await _source.SetLikedAsync(song, sent);
            }
            while (_likeWanted[song.Mid] != sent);
            _songCache.Remove("liked");
            ShowToast(sent ? "已加入我喜歡" : "已取消收藏");
        }
        catch (Exception e) when (IsLoadFailure(e))
        {
            ShowToast(e is InvalidOperationException ? e.Message : "收藏失敗，請稍後再試");
            await LoadLikedIdsAsync();
        }
        finally
        {
            _likeWanted.Remove(song.Mid);
        }
    }

    private void SetLikedLocally(string mid, bool liked)
    {
        if (liked) _likedIds.Add(mid); else _likedIds.Remove(mid);
        RefreshLikedRows();
    }

    public async Task PlayRecommendedAsync()
    {
        if (Recommended.Count > 0) await PlayAsync(Recommended[0].Queue, 0);
    }

    public void ShowSettings()
    {
        BeginPage(Page.Settings, "設定", "依你的習慣調整");
        Busy = false;
    }

    public int GetSetting(string key) => key switch
    {
        "Glow" => _settings.Glow,
        "Color" => _settings.Color,
        "Panel" => _settings.Panel,
        "Align" => _settings.LyricAlign,
        "Cover" => _settings.Cover,
        "Wave" => _settings.Wave,
        "WaveStyle" => _settings.WaveStyle,
        "Quality" => _settings.Quality,
        "Mode" => _settings.Mode,
        "Close" => _settings.CloseAction,
        _ => 0,
    };

    public void ApplySetting(string key, int value)
    {
        switch (key)
        {
            case "Glow": _settings.Glow = value; break;
            case "Close": _settings.CloseAction = value; break;
            case "Color": _settings.Color = value; break;
            case "Panel": _settings.Panel = value; break;
            case "Align":
                ChangeLyric(() => _settings.LyricAlign = value);
                break;
            case "Cover":
                _settings.Cover = value;
                Raise(nameof(CoverScale));
                break;
            case "WaveStyle":
                _settings.WaveStyle = value;
                Raise(nameof(WaveBars));
                Raise(nameof(WavePlain));
                break;
            case "Wave":
                _settings.Wave = value;
                Raise(nameof(PlayerWaveHeight));
                Raise(nameof(PillWaveHeight));
                break;
            case "Quality":
                _settings.Quality = value;
                if (_source is not null) _source.StreamQuality = Pick(value, "best", "320", "128");
                ShowToast("下一首開始套用");
                break;
            case "Mode":
                SetMode(value);
                break;
        }
        ApplyAppearance();
        SaveSettings();
    }

    public bool GetSection(string key) => key switch
    {
        "Daily" => _settings.ShowDaily,
        "Recent" => _settings.ShowRecent,
        "Charts" => _settings.ShowCharts,
        "Albums" => _settings.ShowAlbums,
        "Bold" => _settings.LyricBold,
        "Blur" => _settings.LyricBlur,
        _ => false,
    };

    public void SetSection(string key, bool visible)
    {
        switch (key)
        {
            case "Daily": _settings.ShowDaily = visible; break;
            case "Recent": _settings.ShowRecent = visible; break;
            case "Charts": _settings.ShowCharts = visible; break;
            case "Albums": _settings.ShowAlbums = visible; break;
            case "Bold":
                ChangeLyric(() => _settings.LyricBold = visible);
                return;
            case "Blur":
                ChangeLyric(() => _settings.LyricBlur = visible);
                return;
        }
        RaiseSections();
        SaveSettings();
    }

    /// <summary>The window's last normal bounds, or null on first run and in demo mode.</summary>
    public Rect? SavedWindowBounds =>
        _options.Demo || _settings is not { WindowLeft: { } x, WindowTop: { } y, WindowWidth: > 0 and var w, WindowHeight: > 0 and var h }
            ? null
            : new Rect(x, y, w, h);

    public bool SavedWindowMaximized => !_options.Demo && _settings.WindowMaximized;

    public void RememberWindow(Rect bounds, bool maximized)
    {
        if (_options.Demo || bounds.IsEmpty || bounds.Width <= 0) return;
        _settings.WindowLeft = Math.Round(bounds.Left);
        _settings.WindowTop = Math.Round(bounds.Top);
        _settings.WindowWidth = Math.Round(bounds.Width);
        _settings.WindowHeight = Math.Round(bounds.Height);
        _settings.WindowMaximized = maximized;
        _settings.Save();
    }

    public bool TakeTrayHint()
    {
        if (_settings.TrayHintShown) return false;
        _settings.TrayHintShown = true;
        SaveSettings();
        return true;
    }

    public void CycleMode()
    {
        SetMode((PlayMode + 1) % 3);
        SaveSettings();
        ShowToast(ModeLabel);
    }

    private void SetMode(int mode)
    {
        _settings.Mode = mode;
        Raise(nameof(PlayMode));
        Raise(nameof(ModeLabel));
    }

    public void ResetPlayerOptions()
    {
        _settings.Cover = 1;
        _settings.Wave = 1;
        _settings.WaveStyle = 0;
        Raise(nameof(WaveBars));
        Raise(nameof(WavePlain));
        Raise(nameof(CoverScale));
        Raise(nameof(PlayerWaveHeight));
        Raise(nameof(PillWaveHeight));
        ChangeLyric(() =>
        {
            _settings.LyricLines = AppSettings.DefaultLines;
            _settings.LyricFont = AppSettings.DefaultFont;
            _settings.LyricScale = AppSettings.DefaultScale;
            _settings.LyricSpacing = AppSettings.DefaultSpacing;
            _settings.LyricDim = AppSettings.DefaultDim;
            _settings.LyricAlign = 1;
            _settings.LyricBold = true;
            _settings.LyricBlur = true;
        });
    }

    // ---------------------------------------------------------------- lyric shift, per output device

    private OutputDevice _output = OutputDevice.Unknown;
    private const int MaxLyricShift = 10_000;

    /// <summary>The lyric shift in seconds for the device music plays on; positive shows lyrics earlier.</summary>
    public double LyricOffset => LyricShiftMs / 1000d;
    private int LyricShiftMs => _settings.LyricOffsets.GetValueOrDefault(_output.Id);
    public string OutputName => $"跟著輸出裝置記住 · 目前：{_output.Name}";
    public bool HasLyricShift => LyricShiftMs != 0;
    public string LyricShiftText => LyricShiftMs switch
    {
        > 0 => $"提前 {LyricShiftMs / 1000d:0.0} 秒",
        < 0 => $"延後 {-LyricShiftMs / 1000d:0.0} 秒",
        _ => "未調整",
    };

    public void ShiftLyrics(int deltaMs) => SetLyricShift(Math.Clamp(LyricShiftMs + deltaMs, -MaxLyricShift, MaxLyricShift));

    public void ResetLyricShift() => SetLyricShift(0);

    /// <summary>Checks which device music plays on; a Bluetooth headset and the speakers keep separate shifts.</summary>
    public void RefreshOutput()
    {
        var device = Audio.CurrentDevice();
        if (device == _output) return;
        _output = device;
        RaiseLyricShift();
        Raise(nameof(OutputName));
    }

    private void SetLyricShift(int ms)
    {
        if (ms == 0) _settings.LyricOffsets.Remove(_output.Id);
        else _settings.LyricOffsets[_output.Id] = ms;
        RaiseLyricShift();
        QueueSave();
    }

    private void RaiseLyricShift()
    {
        Raise(nameof(LyricOffset));
        Raise(nameof(LyricShiftText));
        Raise(nameof(HasLyricShift));
    }

    private void ChangeLyric(Action apply)
    {
        apply();
        Raise(nameof(LyricLines));
        Raise(nameof(LyricFontSize));
        Raise(nameof(LyricScale));
        Raise(nameof(LyricSpacing));
        Raise(nameof(LyricDim));
        Raise(nameof(LyricLineHeight));
        LyricLayoutChanged?.Invoke();
        QueueSave();
    }



    // Sliders change continuously, so writes wait until the value settles.
    private void QueueSave()
    {
        if (_options.Demo) return;
        _saveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            _saveTimer?.Stop();
            _settings.Save();
        }, _ui);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSettings()
    {
        if (!_options.Demo) _settings.Save();
    }

    private void ApplyAppearance()
    {
        Theme.Glow = Pick(_settings.Glow, 0d, .35d, .68d, 1d);
        Theme.SetSolidPanel(_settings.Panel == 1);
        ApplyPalette();
    }

    private void ApplyPalette() => Theme.Set(_settings.Color switch
    {
        1 => Palette.Default,
        2 => Palette.LightDefault,
        _ => _coverPalette ?? Palette.Default,
    });

    private static T Pick<T>(int index, params T[] values) => values[Math.Clamp(index, 0, values.Length - 1)];

    /// <summary>Updates only what changed, so unchanged covers are never rebuilt on screen.</summary>
    private static void SyncCards(ObservableCollection<CardItem> target, List<CardItem> next)
    {
        for (var i = 0; i < next.Count; i++)
        {
            if (i >= target.Count) target.Add(next[i]);
            else if (!SameCard(target[i], next[i])) target[i] = next[i];
        }
        while (target.Count > next.Count) target.RemoveAt(target.Count - 1);
    }

    private static bool SameCard(CardItem a, CardItem b) =>
        a.Title == b.Title && a.Subtitle == b.Subtitle && a.Cover == b.Cover && a.IsHero == b.IsHero && a.PlayIndex == b.PlayIndex
        && a.Song?.Mid == b.Song?.Mid && a.Collection == b.Collection;

    public async Task ShowLibraryAsync()
    {
        if (!RequireSignIn()) return;
        var ct = BeginPage(Page.Library, "音樂庫", "");
        if (_libraryCache is { } cached)
        {
            ApplyLibrary(cached.Items);
            Busy = false;
            if (cached.IsFresh) return;
        }
        try
        {
            var items = await _source!.GetLibraryAsync(ct);
            ct.ThrowIfCancellationRequested();
            _libraryCache = new Cached<Collection>(items);
            ApplyLibrary(items);
            EndPage(items.Count == 0 ? "你的音樂庫還是空的" : null);
        }
        catch (Exception e) when (IsLoadFailure(e) && !ct.IsCancellationRequested)
        {
            if (_libraryCache is null) ShowMessage("音樂庫載入失敗，請稍後再試。", canRetry: true);
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyLibrary(IReadOnlyList<Collection> items)
    {
        SyncCards(Library, items
            .Select(item => new CardItem { Title = item.Title, Subtitle = item.Subtitle, Cover = item.Cover, Collection = item })
            .ToList());
        PageSubtitle = $"{items.Count} 個歌單與專輯";
    }

    public Task ShowLikedAsync() => RequireSignIn()
        ? LoadSongsAsync(Page.Liked, "喜歡", "liked", ct => _source!.GetLikedAsync(ct))
        : Task.CompletedTask;

    public Task OpenCollectionAsync(Collection collection)
    {
        if (_currentCollection is { } from)
        {
            // Opening an album from a singer page (or the reverse) remembers the way back.
            if (from != collection) _trail.Push(from);
        }
        else
        {
            _trail.Clear();
            _returnPage = Page switch { Page.Library => Page.Library, Page.Search => Page.Search, Page.Liked => Page.Liked, _ => Page.Home };
        }
        return ShowCollectionAsync(collection);
    }

    private Task ShowCollectionAsync(Collection collection)
    {
        // Set first, so the page change already knows which section to highlight.
        _currentCollection = collection;
        var songs = LoadSongsAsync(Page.Collection, collection.Title, $"{collection.Kind}:{collection.Id}",
            ct => _source!.GetSongsAsync(collection, ct), true);
        Raise(nameof(NavIndex));
        if (collection.Kind is CollectionKind.Singer or CollectionKind.Album)
        {
            var key = $"{collection.Kind}:{collection.Id}";
            // Show what is already known at once, so the header never pops in empty.
            ApplyDetail(_detailCache.GetValueOrDefault(key)
                ?? new Detail(collection.Title, collection.Cover, collection.Kind == CollectionKind.Singer, "", "", [], null), collection.Kind);
            if (!_detailCache.ContainsKey(key)) _ = LoadDetailAsync(collection, key, _pageCts.Token);
        }
        return songs;
    }

    private async Task LoadDetailAsync(Collection collection, string key, CancellationToken ct)
    {
        try
        {
            var detail = collection.Kind == CollectionKind.Singer
                ? await _source!.GetSingerAsync(collection.Id, ct)
                : await _source!.GetAlbumAsync(collection.Id, ct);
            if (ct.IsCancellationRequested) return;
            if (_detailCache.Count >= 40) _detailCache.Clear();
            _detailCache[key] = detail;
            if (_currentCollection == collection) ApplyDetail(detail, collection.Kind);
        }
        catch (Exception e) when (IsLoadFailure(e) || e is OperationCanceledException) { }
    }

    private void ApplyDetail(Detail detail, CollectionKind kind)
    {
        if (string.IsNullOrEmpty(detail.Cover) && _currentCollection is { } collection) detail = detail with { Cover = collection.Cover };
        SetHeader(new DetailHeader(detail, ToCards(detail.Albums), kind == CollectionKind.Singer));
    }

    private void SetHeader(object? header)
    {
        if (SongsHeader.Count == 1 && ReferenceEquals(SongsHeader[0], header)) return;
        SongsHeader.Clear();
        if (header is not null) SongsHeader.Add(header);
    }

    public Task PlayAllAsync() => _visibleSongs.Count > 0 ? PlayAsync(_visibleSongs, 0) : Task.CompletedTask;

    public void OpenAlbumOf(Song song)
    {
        if (string.IsNullOrEmpty(song.AlbumMid))
        {
            ShowToast("找不到這首歌的專輯");
            return;
        }
        NavigationRequested?.Invoke();
        _ = OpenCollectionAsync(new Collection(CollectionKind.Album, song.AlbumMid, song.Album, song.Artist, song.Cover));
    }

    public void OpenSingerOf(Song song)
    {
        if (string.IsNullOrEmpty(song.SingerMid) || _source is null)
        {
            ShowToast("找不到這首歌的歌手");
            return;
        }
        NavigationRequested?.Invoke();
        var name = song.Artist.Split(" / ")[0];
        _ = OpenCollectionAsync(new Collection(CollectionKind.Singer, song.SingerMid, name, "歌手", ""));
    }

    public void OpenDetailSinger(DetailHeader header)
    {
        if (header.Detail.Singer is { } singer) _ = OpenCollectionAsync(singer);
    }

    public Task SearchAsync(string keyword)
    {
        keyword = keyword.Trim();
        if (keyword.Length == 0 || _source is null) return Task.CompletedTask;
        if (!IsSignedIn)
        {
            ShowToast("登入後才能搜尋");
            return Task.CompletedTask;
        }
        if (Page == Page.Collection)
        {
            if (_returnPage != Page.Search) _searchOrigin = _returnPage;
        }
        else if (Page is not (Page.Search or Page.Message))
        {
            _searchOrigin = Page is Page.Library or Page.Liked ? Page : Page.Home;
        }

        if (keyword != _searchKeyword)
        {
            _searchExtras = _searchCache.GetValueOrDefault(keyword) ?? NoExtras;
            _searchTab = 0;
        }
        _searchKeyword = keyword;
        RecordSearch(keyword);
        var songs = LoadSongsAsync(Page.Search, $"搜尋「{keyword}」", $"search:{keyword}", ct => _source.SearchAsync(keyword, ct), true);
        ApplySearchExtras();
        if (!_searchCache.ContainsKey(keyword)) _ = LoadSearchExtrasAsync(keyword, _pageCts.Token);
        return songs;
    }

    /// <summary>Singers, albums and playlists load beside the songs; any of them can be the only match.</summary>
    private async Task LoadSearchExtrasAsync(string keyword, CancellationToken ct)
    {
        var singers = SafeListAsync(() => _source!.SearchSingersAsync(keyword, ct));
        var albums = SafeListAsync(() => _source!.SearchCollectionsAsync(keyword, CollectionKind.Album, ct));
        var playlists = SafeListAsync(() => _source!.SearchCollectionsAsync(keyword, CollectionKind.Playlist, ct));
        await Task.WhenAll(singers, albums, playlists);
        if (ct.IsCancellationRequested) return;
        var extras = new SearchExtras(singers.Result, albums.Result, playlists.Result);
        if (_searchCache.Count >= 40) _searchCache.Clear();
        _searchCache[keyword] = extras;
        if (keyword != _searchKeyword) return;
        _searchExtras = extras;
        if (extras.Any && Page == Page.Message && _songsKey == $"search:{keyword}")
        {
            Message = "";
            CanRetry = false;
            Busy = false;
            Page = Page.Search;
        }
        ApplySearchExtras();
    }

    private void ApplySearchExtras()
    {
        SyncCards(Singers, ToCards(_searchExtras.Singers));
        SyncCards(SearchCollections, ToCards(_searchTab switch
        {
            1 => _searchExtras.Albums,
            2 => _searchExtras.Playlists,
            _ => [],
        }));
        if (Page == Page.Search) SetHeader(_searchTab == 0 && Singers.Count > 0 ? new SingerHeader(Singers.ToList()) : null);
        if (Page == Page.Search && _searchTab != 0)
            PageSubtitle = _searchTab == 1 ? $"{_searchExtras.Albums.Count} 張專輯" : $"{_searchExtras.Playlists.Count} 個歌單";
        else if (Page == Page.Search)
            PageSubtitle = $"{_visibleSongs.Count} 首";
    }

    public void SetSearchTab(int tab)
    {
        if (tab == _searchTab || Page != Page.Search) return;
        _searchTab = Math.Clamp(tab, 0, 2);
        RaiseSearchTabs();
        ApplySearchExtras();
    }

    private void RaiseSearchTabs()
    {
        Raise(nameof(ShowSearchTabs));
        Raise(nameof(ShowSongList));
        Raise(nameof(ShowCollectionGrid));
        Raise(nameof(SearchTab));
    }

    private void RecordSearch(string keyword)
    {
        var existing = SearchHistory.IndexOf(keyword);
        if (existing == 0) return;
        if (existing > 0) SearchHistory.RemoveAt(existing);
        SearchHistory.Insert(0, keyword);
        while (SearchHistory.Count > SearchHistoryLimit) SearchHistory.RemoveAt(SearchHistory.Count - 1);
        SaveSearchHistory();
    }

    public void ClearSearchHistory()
    {
        SearchHistory.Clear();
        SaveSearchHistory();
    }

    private void SaveSearchHistory()
    {
        if (_options.Demo) return;
        try { File.WriteAllText(SearchHistoryFile, JsonSerializer.Serialize(SearchHistory)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void LoadSearchHistory()
    {
        if (_options.Demo) return;
        try
        {
            SearchHistory.Clear();
            foreach (var keyword in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SearchHistoryFile)) ?? [])
                if (!string.IsNullOrWhiteSpace(keyword) && SearchHistory.Count < SearchHistoryLimit) SearchHistory.Add(keyword);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public Task GoBackAsync()
    {
        if (_currentCollection is not null && _trail.Count > 0) return ShowCollectionAsync(_trail.Pop());
        if (Page == Page.Search || (Page == Page.Message && _songsKey?.StartsWith("search:", StringComparison.Ordinal) == true))
            return ShowPageAsync(_searchOrigin);
        return _returnPage == Page.Search ? SearchAsync(_searchKeyword) : ShowPageAsync(_returnPage);
    }

    private Task ShowPageAsync(Page page) => page switch
    {
        Page.Library => ShowLibraryAsync(),
        Page.Liked => ShowLikedAsync(),
        _ => ShowHomeAsync(),
    };

    private async Task LoadSongsAsync(Page page, string title, string key,
        Func<CancellationToken, Task<IReadOnlyList<Song>>> load, bool back = false)
    {
        var ct = BeginPage(page, title, "");
        CanGoBack = back;
        if (_songCache.TryGetValue(key, out var cached))
        {
            ApplySongs(key, cached.Items);
            Busy = false;
            if (cached.IsFresh) return;
        }
        else if (key != _songsKey)
        {
            // Never leave the previous list on screen under a new title.
            _songsKey = key;
            _visibleSongs = [];
            Songs.Clear();
        }

        try
        {
            var songs = await load(ct);
            ct.ThrowIfCancellationRequested();
            if (_songCache.Count >= 40) _songCache.Clear();
            _songCache[key] = new Cached<Song>(songs);
            if (songs.SequenceEqual(_visibleSongs)) PageSubtitle = $"{songs.Count} 首";
            else ApplySongs(key, songs);
            EndPage(songs.Count == 0 && !(page == Page.Search && _searchExtras.Any) ? "沒有找到歌曲" : null);
        }
        catch (Exception e) when (IsLoadFailure(e) && !ct.IsCancellationRequested)
        {
            if (_songCache.ContainsKey(key)) return;
            ShowMessage(e is InvalidOperationException ? e.Message : "載入失敗，請稍後再試。", canRetry: page != Page.Search);
            CanGoBack = back;
        }
        catch (OperationCanceledException) { }
    }

    private void ApplySongs(string key, IReadOnlyList<Song> songs)
    {
        var moved = key != _songsKey;
        _songsKey = key;
        PageSubtitle = $"{songs.Count} 首";
        if (!moved && songs.SequenceEqual(_visibleSongs)) return;
        _visibleSongs = songs;
        Songs.Clear();
        var number = 1;
        foreach (var song in songs)
            Songs.Add(CreateRow(song, number++, songs));
        if (Page == Page.Search) ApplySearchExtras();
        if (moved) SongsReset?.Invoke();
    }

    private CancellationToken BeginPage(Page page, string title, string subtitle)
    {
        _pageCts.Cancel();
        _pageCts = new CancellationTokenSource();
        CancelLogin();
        if (page != Page.Collection) _currentCollection = null;
        SetHeader(null);
        CanGoBack = false;
        CanRetry = false;
        PageTitle = title;
        PageSubtitle = subtitle;
        Message = "";
        Busy = true;
        Page = page;
        return _pageCts.Token;
    }

    private void EndPage(string? emptyMessage)
    {
        Busy = false;
        if (emptyMessage is null) return;
        Message = emptyMessage;
        Page = Page.Message;
    }

    private void ShowMessage(string text, bool busy = false, bool canRetry = false)
    {
        CancelLogin();
        Message = text;
        Busy = busy;
        CanRetry = canRetry;
        Page = Page.Message;
    }

    private bool RequireSignIn()
    {
        if (IsSignedIn) return true;
        if (_source is not null && Page != Page.Login) _ = ShowLoginAsync(Channel);
        return false;
    }

    public async Task ShowLoginAsync(int channel)
    {
        _pageCts.Cancel();
        CancelLogin();
        var cts = _loginCts = new CancellationTokenSource();
        Channel = channel;
        PageTitle = "掃碼登入";
        PageSubtitle = "用手機確認後就能開始聽";
        CanGoBack = false;
        Busy = false;
        LoginHint = "正在取得登入碼";
        QrImage = null;
        Page = Page.Login;

        try
        {
            var ticket = await _source!.CreateQrAsync(Channels[channel], cts.Token);
            QrImage = ImageCache.Decode(ticket.Image);
            LoginHint = channel switch
            {
                1 => "請用微信掃描",
                2 => "請用 QQ 音樂 App 掃描",
                _ => "請用手機 QQ 掃描",
            };

            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1800, cts.Token);
                var status = await _source.CheckQrAsync(ticket.Key, cts.Token);
                switch (status.Code)
                {
                    case 802:
                        LoginHint = "已掃描，請在手機上確認";
                        break;
                    case 803:
                        LoginHint = "登入成功";
                        await EnterAsync();
                        return;
                    case 800:
                        QrImage = null;
                        LoginHint = status.Message.Length > 0 ? status.Message : "登入碼已失效";
                        CanRetry = true;
                        return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (IsLoadFailure(e) || e is FormatException)
        {
            if (!cts.IsCancellationRequested)
            {
                QrImage = null;
                LoginHint = "暫時無法取得登入碼";
                CanRetry = true;
            }
        }
    }

    private void CancelLogin()
    {
        _loginCts?.Cancel();
        _loginCts = null;
    }

    public async Task SignOutAsync()
    {
        if (_source is null) return;
        Stop();
        await _source.SignOutAsync();
        try { File.Delete(StateFile); } catch (IOException) { }
        Current = null;
        WaveHeights = null;
        Queue.Clear();
        Lyrics.Clear();
        Cards.Clear();
        Library.Clear();
        Songs.Clear();
        _homeCache = null;
        _libraryCache = null;
        _feed = null;
        _coverPalette = null;
        _searchCache.Clear();
        _detailCache.Clear();
        _searchExtras = NoExtras;
        SearchHistory.Clear();
        try { File.Delete(SearchHistoryFile); } catch (IOException) { }
        Singers.Clear();
        _searchKeyword = "";
        _likedIds.Clear();
        _recentSongs = [];
        Recommended.Clear();
        Recent.Clear();
        Charts.Clear();
        NewAlbums.Clear();
        RaiseSections();
        try { File.Delete(RecentFile); } catch (IOException) { }
        _songCache.Clear();
        _songsKey = null;
        _visibleSongs = [];
        Theme.Set(Palette.Default);
        await EnterAsync();
    }

    public void SetCardMetrics(double coverHeight, double heroWidth, double smallWidth)
    {
        _coverHeight = coverHeight;
        _heroWidth = heroWidth;
        _smallWidth = smallWidth;
        Raise(nameof(CoverHeight));
        ApplyCardSizes();
    }

    public double CoverHeight => _coverHeight;

    private void ApplyCardSizes()
    {
        foreach (var card in Cards) card.Width = card.IsHero ? _heroWidth : _smallWidth;
    }

    public async Task ActivateCardAsync(CardItem card)
    {
        if (card.PlayQueue is { } queue)
        {
            await PlayAsync(queue, card.PlayIndex);
            return;
        }
        if (card.Song is not null)
        {
            if (!_streamOpened || !IsPlaying) Toggle();
            PlayerRequested?.Invoke();
            return;
        }
        if (card.Collection is not null) await OpenCollectionAsync(card.Collection);
    }

    public Task PlayRowAsync(SongRow row) => PlayAsync(row.Queue, row.Number - 1);

    public void PlayQueueRow(SongRow row)
    {
        var index = Queue.IndexOf(row);
        if (index < 0 || index == QueueIndex) return;
        QueueIndex = index;
        _ = LoadCurrentAsync(true);
    }

    public void PlaySong(SongRow row)
    {
        if (Queue.Contains(row)) PlayQueueRow(row);
        else _ = PlayRowAsync(row);
    }

    public void PlayNext(Song song)
    {
        if (Current is null || QueueIndex < 0 || Queue.Count == 0)
        {
            _ = PlayAsync([song], 0);
            return;
        }
        if (Current.Mid == song.Mid) return;
        var existing = IndexInQueue(song.Mid);
        if (existing >= 0)
        {
            Queue.RemoveAt(existing);
            if (existing < QueueIndex) QueueIndex--;
        }
        Queue.Insert(QueueIndex + 1, CreateRow(song, 0, [song]));
        Renumber();
        ShowToast("將在下一首播放");
    }

    public void AddToQueue(Song song)
    {
        if (Current is null || QueueIndex < 0 || Queue.Count == 0)
        {
            _ = PlayAsync([song], 0);
            return;
        }
        if (IndexInQueue(song.Mid) >= 0)
        {
            ShowToast("已在播放清單中");
            return;
        }
        Queue.Add(CreateRow(song, 0, [song]));
        Renumber();
        ShowToast("已加入播放清單");
    }

    public void RemoveFromQueue(SongRow row)
    {
        var index = Queue.IndexOf(row);
        if (index < 0) return;
        Queue.RemoveAt(index);
        if (index < QueueIndex)
        {
            QueueIndex--;
        }
        else if (index == QueueIndex)
        {
            // Removing the song that is playing moves on to the one that took its place.
            if (Queue.Count == 0)
            {
                Stop();
                QueueIndex = -1;
            }
            else
            {
                QueueIndex = Math.Min(index, Queue.Count - 1);
                _ = LoadCurrentAsync(IsPlaying);
            }
        }
        Renumber();
    }

    public void MoveInQueue(SongRow row, int to)
    {
        var from = Queue.IndexOf(row);
        to = Math.Clamp(to, 0, Queue.Count - 1);
        if (from < 0 || from == to) return;
        Queue.Move(from, to);
        if (from == QueueIndex) QueueIndex = to;
        else if (from < QueueIndex && to >= QueueIndex) QueueIndex--;
        else if (from > QueueIndex && to <= QueueIndex) QueueIndex++;
        Renumber();
    }

    /// <summary>Empties the queue but keeps the song that is playing.</summary>
    public void ClearQueue()
    {
        if (Queue.Count <= 1) return;
        var keep = QueueIndex >= 0 && QueueIndex < Queue.Count ? Queue[QueueIndex] : null;
        Queue.Clear();
        if (keep is not null) Queue.Add(keep);
        QueueIndex = keep is null ? -1 : 0;
        Renumber();
        ShowToast("已清空播放清單");
    }

    private int IndexInQueue(string mid)
    {
        for (var i = 0; i < Queue.Count; i++)
            if (Queue[i].Song.Mid == mid) return i;
        return -1;
    }

    private void Renumber()
    {
        for (var i = 0; i < Queue.Count; i++)
        {
            Queue[i].Number = i + 1;
            Queue[i].IsCurrent = i == QueueIndex;
        }
    }

    public async Task PlayAsync(IReadOnlyList<Song> songs, int index)
    {
        if (index < 0 || index >= songs.Count) return;
        Queue.Clear();
        var number = 1;
        foreach (var song in songs) Queue.Add(CreateRow(song, number++, songs));
        QueueIndex = index;
        _failures = 0;
        await LoadCurrentAsync(true);
    }

    private async Task LoadCurrentAsync(bool autoplay)
    {
        if (QueueIndex < 0 || QueueIndex >= Queue.Count || _source is null) return;
        _trackCts.Cancel();
        _trackCts = new CancellationTokenSource();
        var ct = _trackCts.Token;
        var song = Queue[QueueIndex].Song;
        Audio.Stop();
        _streamOpened = false;
        Current = song;
        MarkCurrentRows();
        ResetTimeline(song);
        IsPlaying = autoplay;
        _media?.SetTrack(song);
        _media?.SetPlaying(autoplay);
        SaveLastSong(song);
        RecordRecent(song);
        if (Page == Page.Home) PageSubtitle = $"接著聽《{song.Title}》";
        RefreshHero(song);
        _ = LoadPaletteAsync(song);
        _ = LoadLyricsAsync(song, ct);
        var waveReady = ShowSavedWave(song);

        try
        {
            var url = await _source.GetStreamAsync(song, ct);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(url))
            {
                HandleTrackFailure();
                return;
            }
            _streamOpened = true;
            Audio.Open(url, autoplay);
            if (!waveReady) _ = AnalyzeWaveAsync(song, url, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) when (IsLoadFailure(e))
        {
            HandleTrackFailure();
        }
    }

    private void RefreshHero(Song song)
    {
        if (Cards.Count == 0 || Cards[0].Song?.Mid == song.Mid) return;
        var hero = new CardItem
        {
            Title = "繼續聽", Subtitle = song.Title, Cover = song.Cover, Song = song, IsHero = true, Width = _heroWidth,
        };
        var first = Cards[0];
        if (first.Song is not null)
        {
            Cards[0] = hero;
            return;
        }
        Cards[0] = new CardItem
        {
            Title = first.Title, Subtitle = first.Subtitle, Cover = first.Cover, Collection = first.Collection, Width = _smallWidth,
        };
        Cards.Insert(0, hero);
    }

    private void ResetTimeline(Song song)
    {
        Position = 0;
        Progress = 0;
        Duration = song.Seconds;
        PositionText = "0:00";
        DurationText = Song.Format(song.Seconds);
        Lyrics.Clear();
    }

    private void MarkCurrentRows()
    {
        foreach (var row in Songs.Concat(Recommended)) row.IsCurrent = row.Song.Mid == Current?.Mid;
        for (var i = 0; i < Queue.Count; i++) Queue[i].IsCurrent = i == QueueIndex;
    }

    private bool ShowSavedWave(Song song)
    {
        WaveHeights = _options.Demo ? null : WaveAnalyzer.ReadCache(WaveDirectory, song.Mid);
        return WaveHeights is not null;
    }

    private async Task AnalyzeWaveAsync(Song song, string playbackUrl, CancellationToken ct)
    {
        void Show(double[] heights) => _ui.BeginInvoke(() =>
        {
            if (!ct.IsCancellationRequested && Current?.Mid == song.Mid) WaveHeights = heights;
        });

        try
        {
            var source = await _source!.GetAnalysisStreamAsync(song, ct) ?? playbackUrl;
            var heights = await WaveAnalyzer.AnalyzeAsync(source, song.Seconds, Show, ct);
            if (heights is null && source != playbackUrl && !ct.IsCancellationRequested)
                heights = await WaveAnalyzer.AnalyzeAsync(playbackUrl, song.Seconds, Show, ct);
            if (heights is null || ct.IsCancellationRequested) return;
            Show(heights);
            if (!_options.Demo) WaveAnalyzer.WriteCache(WaveDirectory, song.Mid, heights);
        }
        catch (Exception e) when (e is OperationCanceledException || IsLoadFailure(e)) { }
    }

    private async Task LoadPaletteAsync(Song song)
    {
        var image = await ImageCache.GetAsync(song.Cover);
        if (Current?.Mid != song.Mid || image is null) return;
        _coverPalette = await Task.Run(() => Palette.FromImage(image));
        ApplyPalette();
    }

    private async Task LoadLyricsAsync(Song song, CancellationToken ct)
    {
        try
        {
            var lines = await _source!.GetLyricsAsync(song, ct);
            ct.ThrowIfCancellationRequested();
            Lyrics.Clear();
            if (lines.All(l => l.Text.Length == 0))
            {
                Lyrics.Add(new LyricLine(new LyricEntry(0, "這首歌沒有歌詞"), timed: false));
                return;
            }
            foreach (var line in LyricLine.From(lines)) Lyrics.Add(line);
        }
        catch (Exception e) when (e is OperationCanceledException || IsLoadFailure(e)) { }
    }

    private void HandleTrackFailure()
    {
        IsPlaying = false;
        _streamOpened = false;
        _media?.SetPlaying(false);
        ShowToast("這首目前無法播放");
        if (++_failures >= 4 || Queue.Count <= 1) return;
        var token = _trackCts.Token;
        _ = Task.Delay(1100, token).ContinueWith(t =>
        {
            if (!t.IsCanceled) _ui.BeginInvoke(() => Next(keepFailures: true));
        }, TaskScheduler.Default);
    }

    public void Toggle()
    {
        if (Current is null) return;
        if (!_streamOpened)
        {
            if (Queue.Count == 0)
            {
                Queue.Add(new SongRow(Current, 1) { IsCurrent = true });
                QueueIndex = 0;
            }
            _ = LoadCurrentAsync(true);
            return;
        }
        if (Audio.IsPlaying) Audio.Pause(); else Audio.Play();
        IsPlaying = Audio.IsPlaying;
        _media?.SetPlaying(IsPlaying);
    }

    public void Next(bool keepFailures = false)
    {
        if (Queue.Count == 0) return;
        if (!keepFailures) _failures = 0;
        QueueIndex = NextIndex();
        _ = LoadCurrentAsync(true);
    }

    private int NextIndex()
    {
        if (PlayMode != 2 || Queue.Count < 2) return (QueueIndex + 1) % Queue.Count;
        int index;
        do index = Random.Shared.Next(Queue.Count);
        while (index == QueueIndex);
        return index;
    }

    private void OnTrackEnded()
    {
        if (PlayMode == 1) _ = LoadCurrentAsync(true);
        else Next();
    }

    public void Previous()
    {
        if (Queue.Count == 0) return;
        if (Audio.Position > 3)
        {
            SeekTo(0);
            return;
        }
        _failures = 0;
        QueueIndex = (QueueIndex - 1 + Queue.Count) % Queue.Count;
        _ = LoadCurrentAsync(true);
    }

    public void SeekRatio(double ratio)
    {
        if (Duration > 0) SeekTo(ratio * Duration);
    }

    public void SeekBy(double seconds) => SeekTo(Audio.Position + seconds);

    public void SeekTo(double seconds)
    {
        if (!_streamOpened) return;
        Audio.Seek(seconds);
        var target = Duration > 0 ? Math.Clamp(seconds, 0, Duration) : Math.Max(0, seconds);
        Position = target;
        PositionText = Song.Format(target);
        Progress = Duration > 0 ? target / Duration : 0;
        _clock = 0;
    }

    private void Stop()
    {
        _trackCts.Cancel();
        Audio.Stop();
        IsPlaying = false;
        _streamOpened = false;
        _media?.SetPlaying(false);
    }

    /// <summary>Advances the on-screen clock; called from the render loop.</summary>
    public void Tick(double dt)
    {
        _clock += dt;
        if (_clock < .1) return;
        _clock = 0;
        if (!_streamOpened) return;

        var position = Audio.Position;
        Position = position;
        PositionText = Song.Format(position);
        Progress = Duration > 0 ? Math.Clamp(position / Duration, 0, 1) : 0;
    }

    public void ShowToast(string text)
    {
        Toast = text;
        ToastRaised?.Invoke();
    }

    private void RestoreLastSong()
    {
        if (Current is not null || _options.Demo) return;
        try
        {
            var song = JsonSerializer.Deserialize<Song>(File.ReadAllText(StateFile));
            if (song is null || string.IsNullOrEmpty(song.Mid)) return;
            if (song.LocalPath is null && song.CoverUrl is { } cover && !cover.StartsWith("http", StringComparison.Ordinal) && !File.Exists(cover)) return;
            Current = song;
            Queue.Clear();
            Queue.Add(new SongRow(song, 1) { IsCurrent = true });
            QueueIndex = 0;
            ResetTimeline(song);
            ShowSavedWave(song);
            _ = LoadPaletteAsync(song);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    private void SaveLastSong(Song song)
    {
        if (_options.Demo) return;
        try { File.WriteAllText(StateFile, JsonSerializer.Serialize(song)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string Greeting() => DateTime.Now.Hour switch
    {
        < 5 => "夜深了",
        < 11 => "早安",
        < 14 => "午安",
        < 18 => "下午好",
        _ => "晚上好",
    };

    private static bool IsLoadFailure(Exception e) => e is HttpRequestException or TaskCanceledException
        or JsonException or InvalidOperationException or IOException or KeyNotFoundException;

    public void Dispose()
    {
        if (_saveTimer?.IsEnabled == true) _settings.Save();
        _pageCts.Cancel();
        _trackCts.Cancel();
        CancelLogin();
        Audio.Dispose();
        _media?.Dispose();
        _bridge?.Dispose();
    }
}
