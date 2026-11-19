using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Silk.Core;

/// <summary>Owns the private Node process that talks to QQ Music and the credentials it needs.</summary>
public sealed class Bridge : IDisposable
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("silk.local.v1");

    private readonly Process _process;
    private readonly HttpClient _http;

    public static string DataDirectory { get; } = CreateDataDirectory();

    private static string SessionFile => Path.Combine(DataDirectory, "session.bin");

    public string? Token { get; private set; }

    private Bridge(Process process, int port, string key)
    {
        _process = process;
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.Add("X-Silk-Key", key);
        Token = ReadProtectedText(SessionFile);
    }

    public static async Task<Bridge> StartAsync(CancellationToken ct = default)
    {
        var directory = FindBridgeDirectory() ?? throw new InvalidOperationException("找不到音樂服務元件");
        var node = FindNode() ?? throw new InvalidOperationException("找不到 Node.js");
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var start = new ProcessStartInfo(node, "server.cjs")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.Environment["SILK_BRIDGE_KEY"] = key;
        start.Environment["QQ_SESSION_SECRET"] = LoadOrCreateSecret();
        start.Environment["QQ_AUTH_SESSION_PATH"] = Path.Combine(DataDirectory, "auth", "sessions.json");
        start.Environment["QQ_AUTH_STATE_PATH"] = Path.Combine(DataDirectory, "auth", "device.json");
        start.Environment["LOG_LEVEL"] = "silent";

        var process = Process.Start(start) ?? throw new InvalidOperationException("音樂服務無法啟動");
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token)
                    ?? throw new InvalidOperationException("音樂服務意外結束");
                if (!line.StartsWith("SILK_READY ", StringComparison.Ordinal)) continue;
                var port = int.Parse(line["SILK_READY ".Length..].Trim());
                _ = DrainAsync(process);
                return new Bridge(process, port, key);
            }
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(Token)) request.Headers.Add("X-QQ-Session", Token);
        using var response = await _http.SendAsync(request, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    public async Task<JsonDocument> PostJsonAsync(string path, string json, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(Token)) request.Headers.Add("X-QQ-Session", Token);
        using var response = await _http.SendAsync(request, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    public void SaveToken(string token)
    {
        Token = token;
        WriteProtectedText(SessionFile, token);
    }

    public void ClearToken()
    {
        Token = null;
        File.Delete(SessionFile);
    }

    public void Dispose()
    {
        _http.Dispose();
        try { _process.StandardInput.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
        if (!_process.WaitForExit(600)) TryKill(_process);
        _process.Dispose();
    }

    private static async Task DrainAsync(Process process)
    {
        try { while (await process.StandardOutput.ReadLineAsync() is not null) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
    }

    private static string CreateDataDirectory()
    {
        var path = DataFolder.Current;
        Directory.CreateDirectory(path);
        return path;
    }

    private static string LoadOrCreateSecret()
    {
        var file = Path.Combine(DataDirectory, "secret.bin");
        var existing = ReadProtectedText(file);
        if (!string.IsNullOrEmpty(existing)) return existing;
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        WriteProtectedText(file, secret);
        return secret;
    }

    private static string? ReadProtectedText(string file)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteProtectedText(string file, string value)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(file, bytes);
    }

    private static string? FindBridgeDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "bridge");
            if (File.Exists(Path.Combine(candidate, "server.cjs"))) return candidate;
        }
        return null;
    }

    private static string? FindNode()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtime", "node.exe");
        if (File.Exists(bundled)) return bundled;
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return paths.Select(p => Path.Combine(p.Trim(), "node.exe")).FirstOrDefault(File.Exists);
    }
}
