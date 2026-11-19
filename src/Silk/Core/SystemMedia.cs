using System.IO;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Silk.Core;

/// <summary>Connects media keys and the Windows media overlay to the player.</summary>
public sealed class SystemMedia : IDisposable
{
    private readonly MediaPlayer _anchor = new();
    private readonly SystemMediaTransportControls _controls;

    public event Action? Toggle;
    public event Action? Next;
    public event Action? Previous;

    public SystemMedia()
    {
        _anchor.CommandManager.IsEnabled = false;
        _controls = _anchor.SystemMediaTransportControls;
        _controls.IsEnabled = true;
        _controls.IsPlayEnabled = true;
        _controls.IsPauseEnabled = true;
        _controls.IsNextEnabled = true;
        _controls.IsPreviousEnabled = true;
        _controls.ButtonPressed += (_, e) =>
        {
            switch (e.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                case SystemMediaTransportControlsButton.Pause:
                    Toggle?.Invoke();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    Next?.Invoke();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    Previous?.Invoke();
                    break;
            }
        };
    }

    public void SetTrack(Song song)
    {
        var updater = _controls.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = song.Title;
        updater.MusicProperties.Artist = song.Artist;
        updater.MusicProperties.AlbumTitle = song.Album;
        if (Uri.TryCreate(song.Cover, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(uri);
        else
            updater.Thumbnail = null;
        updater.Update();
    }

    public void SetPlaying(bool playing) =>
        _controls.PlaybackStatus = playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

    public void Dispose() => _anchor.Dispose();
}
