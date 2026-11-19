namespace Silk.Core;

public sealed record Song(
    string Mid,
    string Title,
    string Artist,
    string Album,
    string AlbumMid,
    int Seconds,
    string? CoverUrl = null,
    string? LocalPath = null,
    long NumericId = 0,
    int Kind = 0,
    string? SingerMid = null)
{
    public string Cover => CoverUrl
        ?? (string.IsNullOrEmpty(AlbumMid) ? "" : $"https://y.gtimg.cn/music/photo_new/T002R500x500M000{AlbumMid}.jpg");

    public string DurationText => Format(Seconds);

    public static string Format(double seconds)
    {
        var total = Math.Max(0, (int)seconds);
        return $"{total / 60}:{total % 60:00}";
    }
}

public enum CollectionKind { Daily, Liked, Owned, Playlist, Album, Toplist, Singer }

public sealed record Collection(
    CollectionKind Kind,
    string Id,
    string Title,
    string Subtitle,
    string Cover);

/// <summary>What a singer or album page shows above its songs.</summary>
public sealed record Detail(
    string Title,
    string Cover,
    bool RoundCover,
    string Meta,
    string Description,
    IReadOnlyList<Collection> Albums,
    Collection? Singer);

public sealed record UserProfile(string Name, string AvatarUrl);

public sealed record LyricEntry(double Time, string Text);

public sealed record QrTicket(string Key, byte[] Image);

public sealed record QrStatus(int Code, string Message);

public interface IMusicSource
{
    bool IsSignedIn { get; }
    string StreamQuality { get; set; }
    Task<IReadOnlyList<Song>> GetRecommendedSongsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Collection>> GetChartsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Collection>> GetNewAlbumsAsync(CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetLikedIdsAsync(CancellationToken ct = default);
    Task SetLikedAsync(Song song, bool liked, CancellationToken ct = default);
    Task<UserProfile?> GetProfileAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Collection>> GetHomeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Collection>> GetLibraryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Song>> GetLikedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Song>> GetSongsAsync(Collection collection, CancellationToken ct = default);
    Task<IReadOnlyList<Song>> SearchAsync(string keyword, CancellationToken ct = default);
    Task<IReadOnlyList<Collection>> SearchSingersAsync(string keyword, CancellationToken ct = default);
    /// <summary>Finds albums (<see cref="CollectionKind.Album"/>) or playlists (<see cref="CollectionKind.Playlist"/>).</summary>
    Task<IReadOnlyList<Collection>> SearchCollectionsAsync(string keyword, CollectionKind kind, CancellationToken ct = default);
    Task<Detail> GetSingerAsync(string mid, CancellationToken ct = default);
    Task<Detail> GetAlbumAsync(string mid, CancellationToken ct = default);
    Task<string?> GetStreamAsync(Song song, CancellationToken ct = default);
    Task<string?> GetAnalysisStreamAsync(Song song, CancellationToken ct = default);
    Task<IReadOnlyList<LyricEntry>> GetLyricsAsync(Song song, CancellationToken ct = default);
    Task<QrTicket> CreateQrAsync(string channel, CancellationToken ct = default);
    Task<QrStatus> CheckQrAsync(string key, CancellationToken ct = default);
    Task SignOutAsync();
}
