using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace Silk.Core;

/// <summary>Measures a song's real loudness over time and keeps the result on disk.</summary>
public static partial class WaveAnalyzer
{
    public const int Points = 151;
    private const double MinimumCoverage = .7;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(260);

    public static Task<double[]?> AnalyzeAsync(string source, double hintSeconds, Action<double[]>? progress, CancellationToken ct)
    {
        var result = new TaskCompletionSource<double[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                result.TrySetResult(Run(source, hintSeconds, progress, ct));
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException
                or NotSupportedException or IOException)
            {
                result.TrySetResult(null);
            }
        })
        {
            IsBackground = true,
            Name = "silk-wave",
            Priority = ThreadPriority.BelowNormal,
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return result.Task;
    }

    private static double[]? Run(string source, double hintSeconds, Action<double[]>? progress, CancellationToken ct)
    {
        using var reader = new MediaFoundationReader(source, new MediaFoundationReader.MediaFoundationReaderSettings
        {
            RequestFloatOutput = true,
        });
        var samples = reader.ToSampleProvider();
        var channels = samples.WaveFormat.Channels;
        var rate = samples.WaveFormat.SampleRate;
        var seconds = reader.TotalTime.TotalSeconds > 1 ? reader.TotalTime.TotalSeconds : hintSeconds;
        if (seconds <= 1) return null;

        var totalFrames = seconds * rate;
        var power = new double[Points];
        var counts = new long[Points];
        var buffer = new float[rate / 2 * channels];
        long frame = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            var read = samples.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            for (var i = 0; i + channels <= read; i += channels)
            {
                double energy = 0;
                for (var c = 0; c < channels; c++) energy += buffer[i + c] * buffer[i + c];
                var bucket = Math.Min(Points - 1, (int)(frame / totalFrames * Points));
                power[bucket] += energy / channels;
                counts[bucket]++;
                frame++;
            }
            if (progress is not null && clock.Elapsed >= ProgressInterval)
            {
                clock.Restart();
                progress(Levels(power, counts));
            }
        }

        if (ct.IsCancellationRequested || frame < totalFrames * MinimumCoverage) return null;
        return Normalize(Levels(power, counts));
    }

    private static double[] Levels(double[] power, long[] counts)
    {
        var raw = new double[Points];
        for (var i = 0; i < Points; i++)
        {
            if (counts[i] == 0) continue;
            var db = 10 * Math.Log10(power[i] / counts[i] + 1e-9);
            raw[i] = Math.Clamp((db + 44) / 38, 0, 1);
        }

        var smooth = new double[Points];
        for (var i = 0; i < Points; i++)
        {
            var left = raw[Math.Max(0, i - 1)];
            var right = raw[Math.Min(Points - 1, i + 1)];
            smooth[i] = counts[i] == 0 ? 0 : (left + 2 * raw[i] + right) / 4;
        }
        return smooth;
    }

    /// <summary>Spreads the song's own quiet-to-loud range across the height of the bar.</summary>
    private static double[] Normalize(double[] levels)
    {
        var sorted = levels.OrderBy(v => v).ToArray();
        var low = sorted[(int)(Points * .05)];
        var high = sorted[(int)(Points * .98)];
        var range = Math.Max(.22, high - low);
        var result = new double[Points];
        for (var i = 0; i < Points; i++)
        {
            var value = Math.Clamp((levels[i] - low) / range, 0, 1);
            result[i] = .07 + .93 * Math.Pow(value, 1.2);
        }
        return result;
    }

    public static double[]? ReadCache(string directory, string id)
    {
        try
        {
            var bytes = File.ReadAllBytes(CachePath(directory, id));
            return bytes.Length == Points ? bytes.Select(b => b / 255d).ToArray() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void WriteCache(string directory, string id, double[] heights)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var bytes = heights.Select(v => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255)).ToArray();
            File.WriteAllBytes(CachePath(directory, id), bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string CachePath(string directory, string id) =>
        Path.Combine(directory, UnsafeName().Replace(id, "_") + ".wave");

    [GeneratedRegex("[^A-Za-z0-9-]")]
    private static partial Regex UnsafeName();
}
