using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Silk.Core;

public sealed partial class QqMusicSource(Bridge bridge) : IMusicSource
{
    private const int PageSize = 100;
    private const int MaxPagedSongs = 3000;

    public bool IsSignedIn => !string.IsNullOrEmpty(bridge.Token);

    public string StreamQuality { get; set; } = "best";

    public async Task<IReadOnlyList<Song>> GetRecommendedSongsAsync(CancellationToken ct = default)
    {
        if (IsSignedIn)
        {
            var daily = await SafeAsync(() => SongsFromAsync("/user/daily-songs", ct));
            if (daily.Count > 0) return daily.Take(12).ToList();
        }
        return (await SongsFromAsync("/recommend/new-songs", ct)).Take(12).ToList();
    }

    public async Task<IReadOnlyList<Collection>> GetChartsAsync(CancellationToken ct = default)
    {
        using var doc = await bridge.GetAsync("/recommend/toplists", ct);
        var result = new List<Collection>();
        if (!doc.RootElement.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array) return result;
        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("toplists", out var lists) || lists.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in lists.EnumerateArray())
            {
                var id = Str(item, "id");
                var title = Str(item, "title");
                if (id is null || title is null) continue;
                result.Add(new Collection(CollectionKind.Toplist, id, Clean(title), $"{Int(item, "total") ?? 0} 首", Str(item, "cover") ?? ""));
                if (result.Count == 10) return result;
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<Collection>> GetNewAlbumsAsync(CancellationToken ct = default)
    {
        using var doc = await bridge.GetAsync("/recommend/new-albums?limit=12", ct);
        var result = new List<Collection>();
        if (!doc.RootElement.TryGetProperty("albums", out var albums) || albums.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in albums.EnumerateArray())
        {
            var mid = Str(item, "mid");
            var title = Str(item, "name", "title");
            if (mid is null || title is null) continue;
            result.Add(new Collection(CollectionKind.Album, mid, Clean(title), ArtistOf(item),
                $"https://y.gtimg.cn/music/photo_new/T002R500x500M000{mid}.jpg"));
        }
        return result;
    }

    public async Task<IReadOnlySet<string>> GetLikedIdsAsync(CancellationToken ct = default) =>
        (await GetLikedAsync(ct)).Select(s => s.Mid).ToHashSet();

    public async Task SetLikedAsync(Song song, bool liked, CancellationToken ct = default)
    {
        if (!IsSignedIn) throw new InvalidOperationException("登入後才能收藏");
        if (song.Kind != 0) throw new InvalidOperationException("這首歌無法收藏");
        var id = song.NumericId > 0 ? song.NumericId : await ResolveSongIdAsync(song, ct);
        var body = JsonSerializer.Serialize(new { song = new { id, mid = song.Mid, type = 0 }, liked });
        using var doc = await bridge.PostJsonAsync("/user/song/like", body, ct);
        if (Int(doc.RootElement, "code") != 200) throw new InvalidOperationException("收藏失敗，請稍後再試");
    }

    public async Task<IReadOnlyList<Collection>> GetEditablePlaylistsAsync(CancellationToken ct = default)
    {
        var result = new List<Collection>();
        if (!IsSignedIn) return result;
        using var doc = await bridge.GetAsync("/user/playlist", ct);
        if (!doc.RootElement.TryGetProperty("playlist", out var list) || list.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in list.EnumerateArray())
        {
            // Directories from 200 up are QQ Music's own (such as 我喜歡); songs cannot be written to them here.
            if (Int(item, "dirId", "dirid") is >= 200 || Int(item, "dirType", "dirtype") is > 0) continue;
            if (ParsePlaylist(item, CollectionKind.Owned) is { } playlist) result.Add(playlist);
        }
        return result;
    }

    public async Task<Collection> CreatePlaylistAsync(string name, CancellationToken ct = default)
    {
        if (!IsSignedIn) throw new InvalidOperationException("登入後才能建立歌單");
        using var doc = await bridge.PostJsonAsync("/user/playlist/create", JsonSerializer.Serialize(new { name }), ct);
        var root = doc.RootElement;
        var tid = Str(root, "tid");
        if (Int(root, "code") != 200 || string.IsNullOrEmpty(tid)) throw new InvalidOperationException("建立歌單失敗，請稍後再試");
        return new Collection(CollectionKind.Owned, tid, Clean(Str(root, "name") ?? name), "歌單", "");
    }

    public async Task AddToPlaylistAsync(Collection playlist, Song song, CancellationToken ct = default)
    {
        if (!IsSignedIn) throw new InvalidOperationException("登入後才能加入歌單");
        if (song.Kind != 0) throw new InvalidOperationException("這首歌無法加入歌單");
        var id = song.NumericId > 0 ? song.NumericId : await ResolveSongIdAsync(song, ct);
        var body = JsonSerializer.Serialize(new { tid = long.Parse(playlist.Id), op = "add", songs = new[] { new { id, type = 0 } } });
        using var doc = await bridge.PostJsonAsync("/user/playlist/tracks", body, ct);
        if (Int(doc.RootElement, "code") != 200) throw new InvalidOperationException("加入歌單失敗，請稍後再試");
    }

    private async Task<long> ResolveSongIdAsync(Song song, CancellationToken ct)
    {
        using var doc = await bridge.GetAsync($"/getSongInfo?songmid={Uri.EscapeDataString(song.Mid)}", ct);
        var id = DeepObject(doc.RootElement, "track_info") is { } track ? Long(track, "id") : null;
        return id ?? throw new InvalidOperationException("收藏失敗，請稍後再試");
    }

    public async Task<UserProfile?> GetProfileAsync(CancellationToken ct = default)
    {
        if (!IsSignedIn) return null;
        using var doc = await bridge.GetAsync("/user/detail", ct);
        var root = doc.RootElement;
        if (Int(root, "code") == 401)
        {
            bridge.ClearToken();
            return null;
        }
        var name = Deep(root, "nickname", "nick", "Nick") ?? "QQ 音樂使用者";
        var avatar = Deep(root, "avatarUrl", "logo", "headurl", "headpic", "Avatar") ?? "";
        return new UserProfile(Clean(name), avatar);
    }

    public async Task<IReadOnlyList<Collection>> GetHomeAsync(CancellationToken ct = default)
    {
        var result = new List<Collection>();
        if (IsSignedIn)
        {
            var daily = await SafeAsync(() => SongsFromAsync("/user/daily-songs", ct));
            if (daily.Count > 0)
                result.Add(new Collection(CollectionKind.Daily, "daily", "每日推薦", $"{daily.Count} 首", daily[0].Cover));

            var liked = await SafeAsync(() => SongsFromAsync("/user/liked-songs?offset=0&limit=1", ct));
            if (liked.Count > 0)
                result.Add(new Collection(CollectionKind.Liked, "liked", "我喜歡", "你收藏的歌", liked[0].Cover));

            var own = await SafeAsync(() => GetLibraryAsync(ct));
            result.AddRange(own.Where(c => c.Kind != CollectionKind.Album).Take(4));
        }

        using var doc = await bridge.GetAsync("/recommend/playlists", ct);
        if (doc.RootElement.TryGetProperty("playlists", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray().Take(8))
                if (ParsePlaylist(item, CollectionKind.Playlist) is { } playlist) result.Add(playlist);
        }
        return result;
    }

    public async Task<IReadOnlyList<Collection>> GetLibraryAsync(CancellationToken ct = default)
    {
        var result = new List<Collection>();
        using (var doc = await bridge.GetAsync("/user/playlist", ct))
        {
            if (doc.RootElement.TryGetProperty("playlist", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                    if (ParsePlaylist(item, CollectionKind.Owned) is { } playlist) result.Add(playlist);
        }

        var albums = await SafeAsync(async () =>
        {
            using var doc = await bridge.GetAsync("/user/albums?offset=0&limit=60", ct);
            var found = new List<Collection>();
            if (FirstObjectArray(doc.RootElement) is not { } array) return (IReadOnlyList<Collection>)found;
            foreach (var item in array.EnumerateArray())
            {
                var mid = Str(item, "albumMid", "albummid", "mid", "album_mid");
                var title = Str(item, "albumName", "albumname", "name", "title");
                if (string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(title)) continue;
                var artist = Str(item, "singerName", "singername") ?? ArtistOf(item);
                found.Add(new Collection(CollectionKind.Album, mid, Clean(title), artist,
                    $"https://y.gtimg.cn/music/photo_new/T002R500x500M000{mid}.jpg"));
            }
            return found;
        });
        result.AddRange(albums);
        return result;
    }

    public Task<IReadOnlyList<Song>> GetLikedAsync(CancellationToken ct = default) =>
        PagedSongsAsync("/user/liked-songs?", ct);

    private async Task<IReadOnlyList<Song>> PagedSongsAsync(string path, CancellationToken ct)
    {
        var all = new List<Song>();
        for (var offset = 0; offset < MaxPagedSongs; offset += PageSize)
        {
            using var doc = await bridge.GetAsync($"{path}offset={offset}&limit={PageSize}", ct);
            var page = FindSongs(doc.RootElement);
            all.AddRange(page);
            var more = doc.RootElement.TryGetProperty("more", out var flag) && flag.ValueKind == JsonValueKind.True;
            if (!more || page.Count == 0) break;
        }
        return all;
    }

    public Task<IReadOnlyList<Song>> GetSongsAsync(Collection c, CancellationToken ct = default) => c.Kind switch
    {
        CollectionKind.Daily => SongsFromAsync("/user/daily-songs", ct),
        CollectionKind.Liked => GetLikedAsync(ct),
        CollectionKind.Album => SongsFromAsync($"/getAlbumInfo?albummid={Uri.EscapeDataString(c.Id)}", ct),
        CollectionKind.Toplist => SongsFromAsync($"/recommend/toplist?id={Uri.EscapeDataString(c.Id)}&limit=100", ct),
        CollectionKind.Singer => SingerSongsAsync(c.Id, ct),
        CollectionKind.Owned => OwnedOrPublicAsync(c.Id, ct),
        _ => SongsFromAsync($"/getSongListDetail/{Uri.EscapeDataString(c.Id)}", ct),
    };

    public async Task<IReadOnlyList<Song>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        var q = Uri.EscapeDataString(keyword.Length > 60 ? keyword[..60] : keyword);
        using var doc = await bridge.GetAsync($"/search?keyword={q}&type=song&page=1&limit=20", ct);
        if (Int(doc.RootElement, "code") is { } code && code != 200)
            throw new InvalidOperationException(IsSignedIn ? "搜尋暫時無法使用" : "登入後才能搜尋");
        return FindSongs(doc.RootElement);
    }

    public async Task<IReadOnlyList<Collection>> SearchSingersAsync(string keyword, CancellationToken ct = default)
    {
        var query = Uri.EscapeDataString(keyword.Length > 60 ? keyword[..60] : keyword);
        var found = new List<Collection>();
        var seen = new HashSet<string>();

        void Add(JsonElement item)
        {
            var mid = Str(item, "singerMID", "singerMid", "singer_mid", "singermid", "mid");
            var name = Str(item, "singerName", "singer_name", "name", "title");
            if (string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(name) || !seen.Add(mid)) return;
            var songs = Int(item, "songNum", "song_num", "songnum");
            found.Add(new Collection(CollectionKind.Singer, mid, Clean(name), songs is > 0 ? $"{songs} 首" : "歌手",
                $"https://y.gtimg.cn/music/photo_new/T001R300x300M000{mid}.jpg"));
        }

        // The direct-match route gives the best hit first and also works before signing in.
        using (var match = await bridge.GetAsync($"/search/match?keyword={query}", ct))
        {
            if (match.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("singer", out var singer)
                && singer.TryGetProperty("itemlist", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray()) Add(item);
            }
        }

        if (IsSignedIn)
        {
            try
            {
                using var search = await bridge.GetAsync($"/search?keyword={query}&type=singer&page=1&limit=12", ct);
                if (search.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    foreach (var item in items.EnumerateArray()) Add(item);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException) { }
        }
        return found;
    }

    public async Task<IReadOnlyList<Collection>> SearchCollectionsAsync(string keyword, CollectionKind kind, CancellationToken ct = default)
    {
        var query = Uri.EscapeDataString(keyword.Length > 60 ? keyword[..60] : keyword);
        var type = kind == CollectionKind.Album ? "album" : "playlist";
        using var doc = await bridge.GetAsync($"/search?keyword={query}&type={type}&page=1&limit=20", ct);
        var found = new List<Collection>();
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return found;
        foreach (var item in items.EnumerateArray())
        {
            if (kind == CollectionKind.Album)
            {
                var mid = Str(item, "albummid", "albumMid", "mid");
                var name = Str(item, "name", "albumName", "title");
                if (string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(name)) continue;
                var date = Str(item, "publish_date", "publishDate") is { Length: >= 4 } d ? $" · {d[..4]}" : "";
                found.Add(new Collection(CollectionKind.Album, mid, Clean(name), Clean(Str(item, "singer", "singerName") ?? "") + date, AlbumCover(mid)));
            }
            else if (ParsePlaylist(item, CollectionKind.Playlist) is { } playlist)
            {
                var owner = Str(item, "nickname", "creator") is { } nick ? $" · {Clean(nick)}" : "";
                found.Add(playlist with { Subtitle = playlist.Subtitle + owner });
            }
        }
        return found;
    }

    public async Task<Detail> GetSingerAsync(string mid, CancellationToken ct = default)
    {
        var escaped = Uri.EscapeDataString(mid);
        var info = bridge.GetAsync($"/singer/detail?mid={escaped}", ct);
        var albums = SafeAsync(async () =>
        {
            using var doc = await bridge.GetAsync($"/getSingerAlbum?singermid={escaped}&limit=40&page=1", ct);
            var list = new List<Collection>();
            if (FirstObjectArray(doc.RootElement) is not { } array) return (IReadOnlyList<Collection>)list;
            foreach (var item in array.EnumerateArray())
            {
                var albumMid = Str(item, "albumMid", "albummid", "mid");
                var name = Str(item, "albumName", "albumname", "name");
                if (string.IsNullOrEmpty(albumMid) || string.IsNullOrEmpty(name)) continue;
                var date = Str(item, "publishDate", "publish_date") ?? "";
                list.Add(new Collection(CollectionKind.Album, albumMid, Clean(name), date.Length >= 4 ? date[..4] : "專輯", AlbumCover(albumMid)));
            }
            return list;
        });

        using var detail = await info;
        var singer = DeepObject(detail.RootElement, "singer") ?? detail.RootElement;
        var name = Clean(Str(singer, "name") ?? "歌手");
        var meta = string.Join(" · ", new[] { Str(singer, "foreignName"), Str(singer, "birthday") }.OfType<string>().Select(Clean));
        return new Detail(name, $"https://y.gtimg.cn/music/photo_new/T001R500x500M000{mid}.jpg", true, meta,
            Prose(Str(singer, "desc")), await albums, null);
    }

    public async Task<Detail> GetAlbumAsync(string mid, CancellationToken ct = default)
    {
        using var doc = await bridge.GetAsync($"/getAlbumInfo?albummid={Uri.EscapeDataString(mid)}", ct);
        var data = DeepObject(doc.RootElement, "data") ?? doc.RootElement;
        var title = Clean(Str(data, "name") ?? "專輯");
        var singerName = Clean(Str(data, "singername") ?? "");
        var meta = string.Join(" · ", new[] { singerName, Str(data, "aDate"), Str(data, "genre"), Str(data, "company") }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Clean(s!)));
        var singer = Str(data, "singermid") is { Length: > 0 } singerMid && singerName.Length > 0
            ? new Collection(CollectionKind.Singer, singerMid, singerName, "歌手", $"https://y.gtimg.cn/music/photo_new/T001R300x300M000{singerMid}.jpg")
            : null;
        return new Detail(title, AlbumCover(mid), false, meta, Prose(Str(data, "desc")), [], singer);
    }

    private static string AlbumCover(string mid) => $"https://y.gtimg.cn/music/photo_new/T002R500x500M000{mid}.jpg";

    /// <summary>Descriptions arrive with HTML entities and ragged blank lines.</summary>
    private static string Prose(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = WebUtility.HtmlDecode(text).Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    private async Task<IReadOnlyList<Song>> SingerSongsAsync(string mid, CancellationToken ct)
    {
        const int pageSize = 60, maxPages = 5;
        var songs = new List<Song>();
        var seen = new HashSet<string>();
        for (var page = 1; page <= maxPages; page++)
        {
            using var doc = await bridge.GetAsync(
                $"/getSingerHotsong?singermid={Uri.EscapeDataString(mid)}&limit={pageSize}&page={page}", ct);
            var batch = FindSongs(doc.RootElement);
            songs.AddRange(batch.Where(s => seen.Add(s.Mid)));
            if (batch.Count < pageSize) break;
        }
        return songs;
    }

    public async Task<string?> GetStreamAsync(Song song, CancellationToken ct = default)
    {
        string[] order = !IsSignedIn ? ["128"] : StreamQuality switch
        {
            "320" => ["320", "128"],
            "128" => ["128"],
            _ => ["best", "320", "128"],
        };
        foreach (var quality in order)
            if (await StreamUrlAsync(song, quality, ct) is { } url) return url;
        return null;
    }

    // The smallest file is enough to measure loudness, which keeps the extra download modest.
    public async Task<string?> GetAnalysisStreamAsync(Song song, CancellationToken ct = default)
    {
        foreach (var quality in new[] { "128", "m4a" })
            if (await StreamUrlAsync(song, quality, ct) is { } url) return url;
        return null;
    }

    private async Task<string?> StreamUrlAsync(Song song, string quality, CancellationToken ct)
    {
        using var doc = await bridge.GetAsync($"/getMusicPlay/{Uri.EscapeDataString(song.Mid)}?quality={quality}", ct);
        return doc.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("playUrl", out var urls)
            && urls.TryGetProperty(song.Mid, out var entry)
            && Str(entry, "url") is { Length: > 0 } url ? url : null;
    }

    public async Task<IReadOnlyList<LyricEntry>> GetLyricsAsync(Song song, CancellationToken ct = default)
    {
        using var doc = await bridge.GetAsync($"/getLyric?songmid={Uri.EscapeDataString(song.Mid)}", ct);
        var text = Deep(doc.RootElement, "lyric");
        return string.IsNullOrEmpty(text) ? [] : ParseLrc(text);
    }

    public async Task<QrTicket> CreateQrAsync(string channel, CancellationToken ct = default)
    {
        using var keyDoc = await bridge.GetAsync($"/login/qr/key?channel={channel}", ct);
        var key = Deep(keyDoc.RootElement, "unikey") ?? throw new InvalidOperationException("無法取得登入碼");
        using var qrDoc = await bridge.GetAsync($"/login/qr/create?key={Uri.EscapeDataString(key)}", ct);
        var image = Deep(qrDoc.RootElement, "qrimg") ?? throw new InvalidOperationException("無法取得登入碼");
        var comma = image.IndexOf(',');
        return new QrTicket(key, Convert.FromBase64String(comma >= 0 ? image[(comma + 1)..] : image));
    }

    public async Task<QrStatus> CheckQrAsync(string key, CancellationToken ct = default)
    {
        using var doc = await bridge.GetAsync($"/login/qr/check?key={Uri.EscapeDataString(key)}", ct);
        var root = doc.RootElement;
        var code = Int(root, "code") ?? 800;
        if (code == 803 && Str(root, "cookie") is { } cookie)
        {
            var token = cookie.Split(';').Select(p => p.Trim())
                .FirstOrDefault(p => p.StartsWith("qqmusic_session=", StringComparison.Ordinal));
            if (token is not null) bridge.SaveToken(token["qqmusic_session=".Length..]);
        }
        var raw = code == 800 ? root.GetRawText() : "";
        var hint = raw.Contains("login.device-limit", StringComparison.Ordinal) ? "登入裝置已達上限，請先到 QQ 音樂 App 移除舊裝置"
            : raw.Contains("login.rate-limited", StringComparison.Ordinal) ? "操作太頻繁，請稍後再試"
            : raw.Contains("login.wrong-scan-app", StringComparison.Ordinal) ? "請用對應的 App 掃碼"
            : "";
        return new QrStatus(code, hint);
    }

    public async Task SignOutAsync()
    {
        try { (await bridge.GetAsync("/logout")).Dispose(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { }
        bridge.ClearToken();
    }

    private async Task<IReadOnlyList<Song>> OwnedOrPublicAsync(string id, CancellationToken ct)
    {
        var owned = await SafeAsync(() => PagedSongsAsync($"/user/playlist-detail?tid={Uri.EscapeDataString(id)}&", ct));
        return owned.Count > 0 ? owned : await SongsFromAsync($"/getSongListDetail/{Uri.EscapeDataString(id)}", ct);
    }

    private async Task<IReadOnlyList<Song>> SongsFromAsync(string path, CancellationToken ct)
    {
        using var doc = await bridge.GetAsync(path, ct);
        return FindSongs(doc.RootElement);
    }

    private static async Task<IReadOnlyList<T>> SafeAsync<T>(Func<Task<IReadOnlyList<T>>> load)
    {
        try { return await load(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static Collection? ParsePlaylist(JsonElement item, CollectionKind kind)
    {
        var id = Str(item, "tid", "dissid", "id", "disstid");
        var title = Str(item, "dirName", "title", "dissname", "name");
        if (string.IsNullOrEmpty(id) || id == "0" || string.IsNullOrEmpty(title)) return null;
        var count = Int(item, "songNum", "songnum", "song_cnt", "songCnt");
        var cover = Str(item, "bigpicUrl", "picUrl", "picurl", "logo", "albumPicUrl", "cover") ?? "";
        return new Collection(kind, id, Clean(title), count is > 0 ? $"{count} 首" : "歌單", cover);
    }

    /// <summary>The API nests track arrays differently per route, so locate the first array of tracks.</summary>
    private static List<Song> FindSongs(JsonElement root)
    {
        var queue = new Queue<JsonElement>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.ValueKind == JsonValueKind.Array)
            {
                var songs = node.EnumerateArray().Select(ParseSong).OfType<Song>().ToList();
                if (songs.Count > 0) return songs;
                foreach (var child in node.EnumerateArray()) queue.Enqueue(child);
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                    if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                        queue.Enqueue(property.Value);
            }
        }
        return [];
    }

    private static Song? ParseSong(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object) return null;
        var item = raw.TryGetProperty("track", out var t) && t.ValueKind == JsonValueKind.Object ? t
            : raw.TryGetProperty("songInfo", out var s) && s.ValueKind == JsonValueKind.Object ? s
            : raw;
        var mid = Str(item, "mid", "songmid");
        var title = Str(item, "title", "name", "songname");
        if (string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(title)) return null;
        if (!item.TryGetProperty("singer", out var singer) || singer.ValueKind != JsonValueKind.Array) return null;

        var albumName = Str(item, "albumname") ?? "";
        var albumMid = Str(item, "albummid") ?? "";
        if (item.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
        {
            albumName = Str(album, "title", "name") ?? albumName;
            albumMid = Str(album, "mid", "pmid") ?? albumMid;
        }
        var singerMid = singer.GetArrayLength() > 0 ? Str(singer[0], "mid", "singermid") : null;
        return new Song(mid, Clean(title), ArtistOf(item), Clean(albumName), albumMid, Int(item, "interval") ?? 0,
            NumericId: Long(item, "id", "songid") ?? 0, Kind: Int(item, "type", "songtype") ?? 0, SingerMid: singerMid);
    }

    private static string ArtistOf(JsonElement item)
    {
        foreach (var key in new[] { "singer", "singers", "singerList" })
        {
            if (!item.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            var names = list.EnumerateArray().Select(x => Str(x, "title", "name")).OfType<string>().Select(Clean);
            return string.Join(" / ", names);
        }
        return "";
    }

    private static JsonElement? FirstObjectArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0
                && property.Value[0].ValueKind == JsonValueKind.Object) return property.Value;
            if (property.Value.ValueKind == JsonValueKind.Object && FirstObjectArray(property.Value) is { } nested) return nested;
        }
        return null;
    }

    public static List<LyricEntry> ParseLrc(string text)
    {
        var lines = new List<LyricEntry>();
        foreach (var raw in WebUtility.HtmlDecode(text).Split('\n'))
        {
            var matches = LrcTime().Matches(raw);
            if (matches.Count == 0) continue;
            var content = LrcTime().Replace(raw, "").Trim();
            // A timed empty line marks where the singing stops; it is kept so breaks can be shown.
            if (content == "//") content = "";
            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                lines.Add(new LyricEntry(minutes * 60 + seconds, content));
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        if (lines.Count > 0) return lines;

        // Lyrics without any timestamps are still worth showing, as plain text.
        return WebUtility.HtmlDecode(text).Split('\n').Select(l => l.Trim())
            .Where(l => l.Length > 0 && !LrcTag().IsMatch(l))
            .Select(l => new LyricEntry(0, l)).ToList();
    }

    private static string Clean(string value) => WebUtility.HtmlDecode(value).Trim();

    private static string? Str(JsonElement item, params string[] keys)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
        {
            if (!item.TryGetProperty(key, out var value)) continue;
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }

    private static int? Int(JsonElement item, params string[] keys)
    {
        var text = Str(item, keys);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static long? Long(JsonElement item, params string[] keys)
    {
        var text = Str(item, keys);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static JsonElement? DeepObject(JsonElement node, string key)
    {
        if (node.ValueKind != JsonValueKind.Object) return null;
        if (node.TryGetProperty(key, out var found) && found.ValueKind == JsonValueKind.Object) return found;
        foreach (var property in node.EnumerateObject())
            if (DeepObject(property.Value, key) is { } nested) return nested;
        return null;
    }

    private static string? Deep(JsonElement node, params string[] keys)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (Str(node, keys) is { } direct) return direct;
            foreach (var property in node.EnumerateObject())
                if (Deep(property.Value, keys) is { } nested) return nested;
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                if (Deep(child, keys) is { } nested) return nested;
        }
        return null;
    }

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex LrcTime();

    [GeneratedRegex(@"^\[[A-Za-z]+:.*\]$")]
    private static partial Regex LrcTag();
}
