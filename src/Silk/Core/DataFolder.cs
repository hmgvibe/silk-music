using System.IO;

namespace Silk.Core;

/// <summary>Locates the app's private data and brings it back from the app's short-lived former name.</summary>
public static class DataFolder
{
    private const string Name = "Silk";
    private const string LegacyName = "QQmL";

    /// <summary>A folder holding any of these has someone's sign-in or settings in it and is never replaced.</summary>
    private static readonly string[] UserFiles = ["session.bin", "settings.json", Path.Combine("auth", "sessions.json")];

    private static readonly string Root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string Current { get; private set; } = Path.Combine(Root, Name);

    public static void AdoptLegacyData() => Current = AdoptLegacyData(Root);

    /// <summary>
    /// Moves the legacy folder into place and returns the folder to use. Leftovers without sign-in or
    /// settings do not block the move. If the move is blocked, the legacy folder stays in use so
    /// sign-in and settings survive, and the move is retried next launch.
    /// </summary>
    public static string AdoptLegacyData(string root)
    {
        var current = Path.Combine(root, Name);
        var legacy = Path.Combine(root, LegacyName);
        if (!Directory.Exists(legacy) || HasUserFiles(current) || !HasUserFiles(legacy)) return current;

        string? leftovers = null;
        try
        {
            if (Directory.Exists(current))
            {
                leftovers = $"{current}.leftover-{Guid.NewGuid():N}";
                Directory.Move(current, leftovers);
            }
            Directory.Move(legacy, current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (leftovers is not null && !Directory.Exists(current)) TryMove(leftovers, current);
            return legacy;
        }

        if (leftovers is not null)
        {
            try { Directory.Delete(leftovers, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return current;
    }

    private static bool HasUserFiles(string directory) =>
        UserFiles.Any(file => File.Exists(Path.Combine(directory, file)));

    private static void TryMove(string from, string to)
    {
        try { Directory.Move(from, to); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
