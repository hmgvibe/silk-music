using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Silk.Core;

/// <summary>
/// Decodes on its own thread into a short buffer, so a slow network never stalls the audio device.
/// The output stream stays open between tracks.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const int LatencyMs = 70;
    private static readonly TimeSpan BufferTarget = TimeSpan.FromSeconds(2.5);

    private readonly bool _muted;
    private readonly object _gate = new();
    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;
    private OutputTap? _tap;
    private WaveFormat? _format;
    private int _generation;
    private long _seekTicks = -1;
    private volatile bool _decodeFinished;
    private volatile bool _hasTrack;
    private double _durationSeconds;
    private int _pauseToken;

    public event Action? Ended;
    public event Action<string>? Failed;
    public event Action<double>? DurationKnown;
    /// <summary>The output stream opened on a device, possibly a different one than before.</summary>
    public event Action? DeviceOpened;

    /// <summary>The device the output stream plays on, or null before the first track.</summary>
    public OutputDevice? Device { get; private set; }

    /// <summary>The device music plays on now, or would play on if a track started.</summary>
    public OutputDevice CurrentDevice()
    {
        if (Device is { } open) return open;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return OutputDevice.From(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia));
        }
        catch (Exception)
        {
            return OutputDevice.Unknown;
        }
    }

    public bool IsPlaying { get; private set; }
    public double Duration => Volatile.Read(ref _durationSeconds);
    public double Position => _tap?.PositionSeconds ?? 0;
    public bool IsBuffering => _hasTrack && IsPlaying && !_decodeFinished && (_buffer?.BufferedBytes ?? 0) == 0;

    public AudioEngine(bool muted) => _muted = muted;

    public void Open(string source, bool autoplay)
    {
        var generation = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _seekTicks, -1);
        _decodeFinished = false;
        _hasTrack = true;
        Volatile.Write(ref _durationSeconds, 0);
        var thread = new Thread(() => Decode(source, generation, autoplay))
        {
            IsBackground = true,
            Name = "silk-decoder",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        _hasTrack = false;
        IsPlaying = false;
        lock (_gate)
        {
            _buffer?.ClearBuffer();
            _tap?.Reset(0);
            if (_tap is not null) _tap.Active = false;
        }
    }

    public void Play()
    {
        if (!_hasTrack) return;
        IsPlaying = true;
        Interlocked.Increment(ref _pauseToken);
        lock (_gate)
        {
            if (_tap is null) return;
            _tap.Active = true;
            _tap.TargetGain = 1;
            if (_output?.PlaybackState != PlaybackState.Playing) _output?.Play();
        }
    }

    public void Pause()
    {
        IsPlaying = false;
        var token = Interlocked.Increment(ref _pauseToken);
        lock (_gate)
        {
            if (_tap is not null) _tap.TargetGain = 0;
        }
        _ = Task.Delay(120).ContinueWith(_ =>
        {
            if (Volatile.Read(ref _pauseToken) != token) return;
            lock (_gate)
            {
                if (_tap is not null) _tap.Active = false;
                _output?.Pause();
            }
        }, TaskScheduler.Default);
    }

    public void Seek(double seconds)
    {
        var duration = Duration;
        var target = duration > 0 ? Math.Clamp(seconds, 0, Math.Max(0, duration - 0.25)) : Math.Max(0, seconds);
        Volatile.Write(ref _seekTicks, TimeSpan.FromSeconds(target).Ticks);
        _decodeFinished = false;
        _tap?.Reset(target);
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            _output?.Dispose();
            _output = null;
        }
    }

    private void EnsureOutput()
    {
        lock (_gate)
        {
            if (_output is not null) return;
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var rate = device.AudioClient.MixFormat.SampleRate;
            _format = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            _buffer = new BufferedWaveProvider(_format)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                ReadFully = true,
                DiscardOnBufferOverflow = true,
            };
            _tap = new OutputTap(_buffer, _muted, OnBufferDrained);
            var output = new WasapiOut(device, AudioClientShareMode.Shared, true, LatencyMs);
            output.Init(new SampleToWaveProvider(_tap));
            output.PlaybackStopped += OnPlaybackStopped;
            _output = output;
            Device = OutputDevice.From(device);
        }
        DeviceOpened?.Invoke();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null) return;
        // The default device went away; rebuild on the next track or play request.
        lock (_gate)
        {
            _output?.Dispose();
            _output = null;
            Device = null;
        }
        IsPlaying = false;
        Failed?.Invoke("音訊裝置已變更");
    }

    private void OnBufferDrained()
    {
        if (!_hasTrack || !_decodeFinished || !IsPlaying) return;
        _hasTrack = false;
        ThreadPool.QueueUserWorkItem(_ => Ended?.Invoke());
    }

    private void Decode(string source, int generation, bool autoplay)
    {
        try
        {
            EnsureOutput();
            using var reader = new MediaFoundationReader(source, new MediaFoundationReader.MediaFoundationReaderSettings
            {
                RequestFloatOutput = true,
                RepositionInRead = true,
            });
            if (generation != Volatile.Read(ref _generation)) return;

            var format = _format!;
            ISampleProvider samples = reader.ToSampleProvider();
            samples = samples.WaveFormat.Channels switch
            {
                1 => new MonoToStereoSampleProvider(samples),
                2 => samples,
                _ => new MultiplexingSampleProvider([samples], 2),
            };
            if (samples.WaveFormat.SampleRate != format.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, format.SampleRate);

            lock (_gate)
            {
                _buffer!.ClearBuffer();
                _tap!.Reset(0);
            }
            Volatile.Write(ref _durationSeconds, reader.TotalTime.TotalSeconds);
            DurationKnown?.Invoke(reader.TotalTime.TotalSeconds);
            if (autoplay) Play();

            var floats = new float[format.SampleRate / 10 * 2];
            var bytes = new byte[floats.Length * sizeof(float)];
            while (generation == Volatile.Read(ref _generation))
            {
                var seek = Interlocked.Exchange(ref _seekTicks, -1);
                if (seek >= 0)
                {
                    reader.CurrentTime = TimeSpan.FromTicks(seek);
                    _buffer!.ClearBuffer();
                    _decodeFinished = false;
                }

                if (_decodeFinished || _buffer!.BufferedDuration > BufferTarget)
                {
                    Thread.Sleep(25);
                    continue;
                }

                var read = samples.Read(floats, 0, floats.Length);
                if (read == 0)
                {
                    _decodeFinished = true;
                    continue;
                }
                Buffer.BlockCopy(floats, 0, bytes, 0, read * sizeof(float));
                _buffer.AddSamples(bytes, 0, read * sizeof(float));
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException
            or ArgumentException or NotSupportedException or System.IO.IOException)
        {
            if (generation == Volatile.Read(ref _generation)) Failed?.Invoke(e.Message);
        }
    }

    private sealed class OutputTap(BufferedWaveProvider buffer, bool muted, Action drained)
        : ISampleProvider
    {
        private readonly ISampleProvider _source = buffer.ToSampleProvider();
        private readonly double _sampleRate = buffer.WaveFormat.SampleRate;
        private readonly float _gainStep = 1f / (buffer.WaveFormat.SampleRate * 0.045f);
        private long _frames;
        private double _baseSeconds;
        private float _gain;

        public WaveFormat WaveFormat => _source.WaveFormat;
        public volatile bool Active;
        public volatile float TargetGain = 1;

        public double PositionSeconds => Volatile.Read(ref _baseSeconds) + Interlocked.Read(ref _frames) / _sampleRate;

        public void Reset(double seconds)
        {
            Volatile.Write(ref _baseSeconds, seconds);
            Interlocked.Exchange(ref _frames, 0);
        }

        public int Read(float[] target, int offset, int count)
        {
            if (!Active)
            {
                Silence(target, offset, count);
                return count;
            }

            var available = Math.Min(count, buffer.BufferedBytes / sizeof(float));
            _source.Read(target, offset, count);
            Interlocked.Add(ref _frames, available / 2);

            for (var i = offset; i < offset + count; i += 2)
            {
                _gain = _gain < TargetGain ? Math.Min(TargetGain, _gain + _gainStep) : Math.Max(TargetGain, _gain - _gainStep);
                target[i] *= _gain;
                target[i + 1] *= _gain;
            }
            if (muted) Silence(target, offset, count);
            if (available == 0) drained();
            return count;
        }

        // NAudio passes a byte[] viewed as float[] here, so Array.Clear would clear bytes and leave
        // three quarters of the buffer holding old audio, which the device then replays as noise.
        private static void Silence(float[] target, int offset, int count)
        {
            for (var i = offset; i < offset + count; i++) target[i] = 0;
        }
    }
}

/// <summary>A playback device: <see cref="Id"/> keys saved settings, <see cref="Name"/> is shown to the user.</summary>
public sealed record OutputDevice(string Id, string Name)
{
    public static readonly OutputDevice Unknown = new("", "預設輸出");

    public static OutputDevice From(MMDevice device)
    {
        var name = device.FriendlyName;
        return new OutputDevice(device.ID, string.IsNullOrWhiteSpace(name) ? "預設輸出" : name);
    }
}
