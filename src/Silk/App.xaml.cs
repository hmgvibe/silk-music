using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Silk.Core;

namespace Silk;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _showRequest;
    private RegisteredWaitHandle? _showWait;

    public static AppModel? Model { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        var options = AppOptions.Parse(e.Args);
        if (options.SingleInstance && !ClaimInstance())
        {
            Shutdown();
            return;
        }

        if (!options.Demo) DataFolder.AdoptLegacyData();
        Model = new AppModel(options);
        var window = new MainWindow(Model, options);
        MainWindow = window;
        SessionEnding += (_, _) => window.ExitApplication();
        window.Show();
        if (_showRequest is not null)
        {
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showRequest,
                (_, _) => Dispatcher.BeginInvoke(window.ShowFromBackground), null, Timeout.Infinite, false);
        }
    }

    /// <summary>A second launch of the same copy brings the running one forward instead of playing twice.</summary>
    private bool ClaimInstance()
    {
        var path = (Environment.ProcessPath ?? "Silk").ToUpperInvariant();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $"Silk.Show.{id}");
        _instance = new Mutex(true, $"Silk.Instance.{id}", out var first);
        if (first) return true;
        _showRequest.Set();
        return false;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(Bridge.DataDirectory, "error.log"),
                $"{DateTime.Now:O} {e.Exception.GetType().FullName}{Environment.NewLine}{e.Exception.StackTrace}{Environment.NewLine}");
        }
        catch (IOException) { }
        e.Handled = true;
        Model?.ShowToast("剛才的操作沒有完成");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        Model?.Dispose();
        _showRequest?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private async void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CardItem card && Model is not null)
            await Model.ActivateCardAsync(card);
    }

    private async void OnRowLikeClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SongRow row && Model is not null)
            await Model.ToggleLikeAsync(row.Song);
    }

    private async void OnSongRowClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SongRow row && Model is not null)
            await Model.PlayRowAsync(row);
    }

    private void OnQueueRowClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is SongRow row) Model?.PlayQueueRow(row);
    }

    private static SongRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as SongRow;

    private void OnMenuPlay(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Model?.PlaySong(row);
    }

    private void OnMenuPlayNext(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Model?.PlayNext(row.Song);
    }

    private void OnMenuAddToQueue(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Model?.AddToQueue(row.Song);
    }

    private async void OnMenuLike(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && Model is not null) await Model.ToggleLikeAsync(row.Song);
    }

    /// <summary>Fills "加入歌單" with the user's own playlists each time it opens, plus a way to make a new one.</summary>
    private async void OnMenuPlaylistsOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender || sender is not MenuItem menu || RowOf(sender) is not { } row || Model is not { } model) return;
        var song = row.Song;
        var playlists = await model.GetEditablePlaylistsAsync();
        // Clearing first would empty the submenu for a moment, and WPF closes an empty submenu; so add, then remove the old.
        var old = menu.Items.Cast<object>().ToList();
        foreach (var playlist in playlists)
        {
            var item = new MenuItem { Header = playlist.Title };
            item.Click += async (_, _) => await model.AddToPlaylistAsync(playlist, song);
            menu.Items.Add(item);
        }
        if (playlists.Count > 0) menu.Items.Add(new Separator());
        var create = new MenuItem { Header = "新建歌單並加入…" };
        create.Click += (_, _) => model.RequestNewPlaylist(song);
        menu.Items.Add(create);
        foreach (var item in old) menu.Items.Remove(item);
    }

    private void OnMenuAlbum(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Model?.OpenAlbumOf(row.Song);
    }

    private void OnMenuSinger(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Model?.OpenSingerOf(row.Song);
    }

    private void OnMenuRemove(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Model?.RemoveFromQueue(row);
    }

    private void OnQueueRemove(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Model?.RemoveFromQueue(row);
    }

    /// <summary>Dragging the handle carries the row; the queue list moves it as the pointer passes other rows.</summary>
    private void OnQueueGripDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        e.Handled = true;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(SongRow), row), DragDropEffects.Move);
    }

    private async void OnDetailPlayAll(object sender, RoutedEventArgs e)
    {
        if (Model is not null) await Model.PlayAllAsync();
    }

    private void OnDetailExpand(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DetailHeader header) header.ToggleExpanded();
    }

    private void OnDetailSinger(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DetailHeader header) Model?.OpenDetailSinger(header);
    }
}
