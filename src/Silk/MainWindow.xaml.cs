using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Silk.Core;
using Silk.Ui;

namespace Silk;

public partial class MainWindow : Window
{
    private const double SilkFrame = 1 / 30d;
    private const double PlayGlyphOffset = .27;

    private readonly AppModel _model;
    private readonly AppOptions _options;
    private readonly SilkEffect? _silk;
    private TimeSpan _lastRender;
    private double _silkTime = 8;
    private double _silkClock;
    private bool _playerOpen;
    private bool _queueOpen;
    private bool _exiting;
    private bool _fullscreen;
    private System.Windows.Shell.ThumbButtonInfo? _thumbPrevious;
    private System.Windows.Shell.ThumbButtonInfo? _thumbToggle;
    private System.Windows.Shell.ThumbButtonInfo? _thumbNext;
    private bool _thumbShowsPause;
    private Rect _boundsBeforeFullscreen;
    private WindowState _stateBeforeFullscreen;
    private TrayIcon? _tray;
    private bool _playerOptionsOpen;
    private DispatcherTimer? _toastTimer;
    private bool _syncing;

    public MainWindow(AppModel model, AppOptions options)
    {
        _model = model;
        _options = options;
        InitializeComponent();
        DataContext = model;
        RestoreWindowBounds();

        if (SilkEffect.IsAvailable)
        {
            _silk = new SilkEffect();
            SilkSurface.Effect = _silk;
        }

        PillWave.Seeked += model.SeekRatio;
        PlayerWave.Seeked += model.SeekRatio;
        model.PropertyChanged += OnModelChanged;
        LyricView.Attach(model.Lyrics, () => model.PlaybackPosition);
        LyricView.Seek += model.SeekTo;
        model.LyricLayoutChanged += LayoutLyrics;
        model.ToastRaised += ShowToast;
        model.SongsReset += () =>
        {
            if (SongList.Items.Count > 0) SongList.ScrollIntoView(SongList.Items[0]);
        };
        model.PlayerRequested += () => SetPlayerOpen(true);
        model.NavigationRequested += () =>
        {
            SetQueueOpen(false);
            SetPlayerOpen(false);
        };
        SongList.ItemsSource = new CompositeCollection
        {
            new CollectionContainer { Collection = model.SongsHeader },
            new CollectionContainer { Collection = model.Songs },
        };
        SearchInput.GotKeyboardFocus += (_, _) => UpdateHistoryPopup();
        SearchInput.LostKeyboardFocus += (_, _) => HistoryPopup.IsOpen = false;
        SearchInput.TextChanged += (_, _) => UpdateHistoryPopup();
        model.SearchHistory.CollectionChanged += (_, _) => UpdateHistoryPopup();
        Deactivated += (_, _) => HistoryPopup.IsOpen = false;
        LocationChanged += (_, _) => HistoryPopup.IsOpen = false;

        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        Closed += (_, _) => _tray?.Dispose();
        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => ApplyWindowState();
        PreviewKeyDown += OnPreviewKey;
        CompositionTarget.Rendering += OnRendering;
        SyncNavigation();
        SyncSettings();
        UpdatePlayIcon();
        UpdateModeIcon();
        SetupTaskbar();
    }

    public void ShowFromBackground()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (IsVisible) RememberWindow();
        if (_exiting || _tray is null || !_model.CloseToTray) return;
        e.Cancel = true;
        Hide();
        if (_model.TakeTrayHint()) _tray.ShowBackgroundHint();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_options.SingleInstance) _tray = new TrayIcon(_model, ShowFromBackground, ExitApplication);
        await _model.StartAsync();
        ApplyStartView();
    }

    /// <summary>Lets a launch argument open a specific screen, used for visual checks.</summary>
    private void ApplyStartView()
    {
        if (_options.StartView?.StartsWith("search:", StringComparison.Ordinal) == true)
        {
            _ = _model.SearchAsync(_options.StartView["search:".Length..]);
            return;
        }
        switch (_options.StartView)
        {
            case "player":
                SetPlayerOpen(true);
                break;
            case "settings":
                NavSettings.IsChecked = true;
                break;
            case "track":
                PillTrack.Focus();
                break;
            case "queue":
                SetPlayerOpen(true);
                SetQueueOpen(true);
                break;
            case "library":
                NavLibrary.IsChecked = true;
                break;
            case "liked":
                NavLiked.IsChecked = true;
                break;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastRender) return;
        var dt = Math.Min(.1, (now - _lastRender).TotalSeconds);
        _lastRender = now;
        if (WindowState == WindowState.Minimized || !IsVisible) return;

        Theme.Tick(dt);
        _model.Tick(dt);

        var speed = _model.IsPlaying ? 1 : .35;
        // Each silk update redraws the whole window, so the silk holds still while something slides or fades;
        // it moves slowly enough that the pause is not visible, and it resumes from where it stopped.
        if (Tween.Busy) return;
        _silkTime += dt * speed * .62;
        _silkClock += dt;
        if (_silk is not null && _silkClock >= (Theme.Glow > 0 ? SilkFrame : .25))
        {
            _silkClock = 0;
            _silk.Update(_silkTime, ActualWidth, ActualHeight);
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppModel.IsPlaying):
                UpdatePlayIcon();
                UpdateTaskbar();
                break;
            case nameof(AppModel.HasTrack):
            case nameof(AppModel.Progress):
                UpdateTaskbar();
                break;

            case nameof(AppModel.Page):
                SyncNavigation();
                break;
            case nameof(AppModel.CoverScale):
                UpdatePlayerCover();
                break;
            case nameof(AppModel.SearchTab):
                _syncing = true;
                foreach (var tab in SearchTabs.Children.OfType<RadioButton>())
                    tab.IsChecked = (string)tab.Tag == _model.SearchTab.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _syncing = false;
                break;
            case nameof(AppModel.PlayMode):
                UpdateModeIcon();
                SyncSettings();
                break;
            case nameof(AppModel.Channel):
                _syncing = true;
                (_model.Channel switch { 1 => ChannelWechat, 2 => ChannelApp, _ => ChannelQq }).IsChecked = true;
                _syncing = false;
                break;
        }
    }

    private void SyncNavigation()
    {
        _syncing = true;
        (_model.NavIndex switch { 1 => NavLibrary, 2 => NavLiked, 3 => NavSettings, _ => NavHome }).IsChecked = true;
        _syncing = false;
    }

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var cover = Math.Round(Math.Clamp(MainPanel.ActualWidth * .27, 170, 290));
        _model.SetCardMetrics(cover, cover, Math.Round(cover * .67));
        HeroShelf.ArrowTop = cover / 2 - 20;
    }

    private void OnPlayerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePlayerCover();
    }

    private void UpdatePlayerCover()
    {
        var size = Math.Clamp(Math.Min(ActualHeight - 340, ActualWidth * .3), 200, 520) * _model.CoverScale;
        PlayerCover.Width = size;
        PlayerCover.Height = size;
    }

    private void OnLyricAreaChanged(object sender, SizeChangedEventArgs e) => LayoutLyrics();

    private void LayoutLyrics()
    {
        var line = _model.LyricLineHeight;
        var height = Math.Min(LyricArea.ActualHeight, _model.LyricLines * line);
        LyricViewport.Height = Math.Max(line, height);
        var fade = Math.Min(.16, .9 / Math.Max(1, height / line));
        LyricFadeTop.Offset = fade;
        LyricFadeBottom.Offset = 1 - fade;
        LyricView.ApplyStyle(_model.LyricStyle);
    }

    private void SetPlayerOpen(bool open)
    {
        if (_playerOpen == open || (open && !_model.HasTrack)) return;
        _playerOpen = open;
        LyricView.Calm = true;
        UpdateLayout();
        // The layer that is about to appear is laid out before the animation clock starts,
        // so its layout pass cannot land in the middle of the motion.
        var arrivingLayer = open ? PlayerLayer : HomeLayer;
        if (arrivingLayer.Visibility != Visibility.Visible)
        {
            arrivingLayer.Opacity = 0;
            arrivingLayer.Visibility = Visibility.Visible;
            UpdateLayout();
        }

        var from = RectOf(open ? PillCover : PlayerCover);
        var to = RectOf(open ? PlayerCover : PillCover);
        var fromRadius = open ? PillCover.CornerRadius.TopLeft : PlayerCover.CornerRadius.TopLeft;
        var toRadius = open ? PlayerCover.CornerRadius.TopLeft : PillCover.CornerRadius.TopLeft;
        PillCover.Opacity = 0;
        PlayerCover.Opacity = 0;

        // The flying cover keeps one size and is only scaled and moved, so no frame of the flight needs a layout pass.
        var size = Math.Max(from.Width, to.Width);
        var scale = new ScaleTransform();
        var move = new TranslateTransform();
        FlyCover.Width = size;
        FlyCover.Height = size;
        Canvas.SetLeft(FlyCover, 0);
        Canvas.SetTop(FlyCover, 0);
        FlyCover.RenderTransform = new TransformGroup { Children = { scale, move } };
        FlyCover.Visibility = Visibility.Visible;

        var duration = open ? 400 : 280;
        Tween.Run(FlyCover, duration, t =>
        {
            var w = from.Width + (to.Width - from.Width) * t;
            var s = w / size;
            scale.ScaleX = s;
            scale.ScaleY = s;
            move.X = from.X + (to.X - from.X) * t;
            move.Y = from.Y + (to.Y - from.Y) * t;
            FlyCover.CornerRadius = new CornerRadius((fromRadius + (toRadius - fromRadius) * t) / s);
            // The layers hand over one after the other rather than cross-fading: a half-transparent layer
            // the size of the window is costly to draw, and only one of them is half-transparent at a time.
            var leaving = 1 - Math.Clamp(t / .38, 0, 1);
            var arriving = Math.Clamp((t - .38) / .62, 0, 1);
            (open ? HomeLayer : PlayerLayer).Opacity = leaving;
            (open ? PlayerLayer : HomeLayer).Opacity = arriving;
        }, Tween.EaseInOut, () =>
        {
            FlyCover.Visibility = Visibility.Collapsed;
            PillCover.Opacity = 1;
            PlayerCover.Opacity = 1;
            HomeLayer.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            if (!open) PlayerLayer.Visibility = Visibility.Collapsed;
            if (open) LyricView.Calm = false;
        });
        HomeLayer.Visibility = Visibility.Visible;
    }

    private Rect RectOf(FrameworkElement element)
    {
        var origin = element.TransformToAncestor(Root).Transform(new Point());
        return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
    }

    private void SetQueueOpen(bool open)
    {
        if (_queueOpen == open) return;
        _queueOpen = open;
        if (open)
        {
            QueueLayer.Visibility = Visibility.Visible;
            if (_model.QueueIndex >= 0 && _model.QueueIndex < _model.Queue.Count)
                QueueList.ScrollIntoView(_model.Queue[_model.QueueIndex]);
        }
        SlideDrawer(QueueLayer, QueueDrawer, QueueShift, open, QueueDrawer.Width + 16, () => _queueOpen);
    }

    /// <summary>
    /// Slides a drawer in or out. It is laid out before the clock starts and held as one picture while it moves,
    /// so each frame only moves a bitmap instead of redrawing its text and controls.
    /// </summary>
    private void SlideDrawer(FrameworkElement layer, FrameworkElement drawer, TranslateTransform shift, bool open, double offscreen, Func<bool> isOpen)
    {
        layer.Visibility = Visibility.Visible;
        UpdateLayout();
        drawer.CacheMode = new BitmapCache
        {
            EnableClearType = true,
            SnapsToDevicePixels = true,
            RenderAtScale = VisualTreeHelper.GetDpi(this).DpiScaleX,
        };
        var from = open ? offscreen : shift.X;
        var to = open ? 0 : offscreen;
        Tween.Run(shift, open ? 300 : 200, t => shift.X = from + (to - from) * t,
            open ? Tween.EaseOut : Tween.EaseIn,
            () =>
            {
                drawer.CacheMode = null;
                if (!isOpen()) layer.Visibility = Visibility.Collapsed;
            });
    }

    private void SetPlayerOptionsOpen(bool open)
    {
        if (_playerOptionsOpen == open) return;
        _playerOptionsOpen = open;
        SlideDrawer(PlayerOptionsLayer, PlayerOptionsDrawer, PlayerOptionsShift, open, -(PlayerOptionsDrawer.Width + 16), () => _playerOptionsOpen);
    }

    private void OnPlayerOptionsOpen(object sender, RoutedEventArgs e) => SetPlayerOptionsOpen(true);
    private void OnPlayerOptionsClose(object sender, RoutedEventArgs e) => SetPlayerOptionsOpen(false);
    private void OnPlayerOptionsScrim(object sender, MouseButtonEventArgs e) => SetPlayerOptionsOpen(false);

    private void OnPlayerOptionsReset(object sender, RoutedEventArgs e)
    {
        _model.ResetPlayerOptions();
        SyncSettings();
    }

    private void ShowToast()
    {
        var from = ToastBox.Opacity;
        Tween.Run(ToastBox, 160, t => ToastBox.Opacity = from + (1 - from) * t);
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer?.Stop();
            Tween.Run(ToastBox, 220, t => ToastBox.Opacity = 1 - t);
        };
        _toastTimer.Start();
    }

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_playerOpen) SetPlayerOpen(false);
            SearchInput.Focus();
            SearchInput.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F11)
        {
            SetFullscreen(!_fullscreen);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (_playerOptionsOpen) SetPlayerOptionsOpen(false);
            else if (_fullscreen) SetFullscreen(false);
            else if (_queueOpen) SetQueueOpen(false);
            else if (_playerOpen) SetPlayerOpen(false);
            else if (SearchInput.IsKeyboardFocused) Keyboard.ClearFocus();
            else return;
            e.Handled = true;
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return;
        switch (e.Key)
        {
            case Key.Space when Keyboard.FocusedElement is not ButtonBase:
                _model.Toggle();
                e.Handled = true;
                break;
            case Key.Left:
                _model.SeekBy(-5);
                e.Handled = true;
                break;
            case Key.Right:
                _model.SeekBy(5);
                e.Handled = true;
                break;
        }
    }

    private void OnToggle(object sender, RoutedEventArgs e) => _model.Toggle();
    private void OnNext(object sender, RoutedEventArgs e) => _model.Next();
    private void OnPrevious(object sender, RoutedEventArgs e) => _model.Previous();
    private void OnOpenPlayer(object sender, RoutedEventArgs e) => SetPlayerOpen(true);
    private void OnClosePlayer(object sender, RoutedEventArgs e) => SetPlayerOpen(false);
    private void OnQueueOpen(object sender, RoutedEventArgs e) => SetQueueOpen(true);
    private void OnQueueClose(object sender, RoutedEventArgs e) => SetQueueOpen(false);
    private void OnQueueScrimClick(object sender, MouseButtonEventArgs e) => SetQueueOpen(false);
    private async void OnBackClick(object sender, RoutedEventArgs e) => await _model.GoBackAsync();
    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (_model.IsLoginView) await _model.ShowLoginAsync(_model.Channel);
        else await _model.RetryAsync();
    }

    private async void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsLoaded) return;
        if (ReferenceEquals(sender, NavSettings)) _model.ShowSettings();
        else if (ReferenceEquals(sender, NavLibrary)) await _model.ShowLibraryAsync();
        else if (ReferenceEquals(sender, NavLiked)) await _model.ShowLikedAsync();
        else await _model.ShowHomeAsync();
        SyncNavigation();
    }

    private async void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CardItem card) await _model.ActivateCardAsync(card);
    }



    private async void OnChannelChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsLoaded) return;
        if (sender is RadioButton { Tag: string tag }) await _model.ShowLoginAsync(int.Parse(tag));
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        HistoryPopup.IsOpen = false;
        await _model.SearchAsync(SearchInput.Text);
    }

    private void UpdateHistoryPopup() =>
        HistoryPopup.IsOpen = SearchInput.IsKeyboardFocused && SearchInput.Text.Length == 0 && _model.SearchHistory.Count > 0;

    private async void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not string keyword) return;
        HistoryPopup.IsOpen = false;
        SearchInput.Text = keyword;
        SearchInput.CaretIndex = keyword.Length;
        await _model.SearchAsync(keyword);
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        _model.ClearSearchHistory();
        HistoryPopup.IsOpen = false;
    }

    private void OnSearchTabChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string tag }) return;
        _model.SetSearchTab(int.Parse(tag, System.Globalization.CultureInfo.InvariantCulture));
    }

    private void OnQueueClear(object sender, RoutedEventArgs e) => _model.ClearQueue();

    /// <summary>Moves the dragged row to wherever the pointer is, so the list shows the new order while dragging.</summary>
    private void OnQueueDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(SongRow)) is not SongRow dragged)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        if (RowUnder(e.OriginalSource) is { } target && !ReferenceEquals(target, dragged))
            _model.MoveInQueue(dragged, _model.Queue.IndexOf(target));

        // Near the top or bottom edge the list scrolls, so a row can travel past what is visible.
        if (FindChild<ScrollViewer>(QueueList) is { } scroller)
        {
            var y = e.GetPosition(QueueList).Y;
            if (y < 36) scroller.ScrollToVerticalOffset(scroller.VerticalOffset - 8);
            else if (y > QueueList.ActualHeight - 36) scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 8);
        }
    }

    private void OnQueueDrop(object sender, DragEventArgs e) => e.Handled = true;

    private static SongRow? RowUnder(object source)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { DataContext: SongRow row }) return row;
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void OnLikeCurrent(object sender, RoutedEventArgs e) => _model.ToggleCurrentLike();
    private void OnCycleMode(object sender, RoutedEventArgs e) => _model.CycleMode();
    private async void OnPlayRecommended(object sender, RoutedEventArgs e) => await _model.PlayRecommendedAsync();

    private void OnSettingChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string tag }) return;
        var parts = tag.Split(':');
        _model.ApplySetting(parts[0], int.Parse(parts[1]));
        SyncSettings();
    }

    private void OnSectionChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not CheckBox { Tag: string tag } box) return;
        _model.SetSection(tag, box.IsChecked == true);
        SyncSettings();
    }

    private void SyncSettings()
    {
        _syncing = true;
        Visit(SettingsPage);
        Visit(PlayerOptionsPanel);
        _syncing = false;

        void Visit(object node)
        {
            if (node is RadioButton { Tag: string tag } radio && tag.Contains(':'))
            {
                var parts = tag.Split(':');
                radio.IsChecked = _model.GetSetting(parts[0]) == int.Parse(parts[1]);
            }
            else if (node is CheckBox { Tag: string section } box)
            {
                box.IsChecked = _model.GetSection(section);
            }
            if (node is DependencyObject parent)
                foreach (var child in LogicalTreeHelper.GetChildren(parent)) Visit(child);
        }
    }

    private void UpdatePlayIcon()
    {
        var icon = (Geometry)FindResource(_model.IsPlaying ? "I.Pause" : "I.Play");
        foreach (var button in new[] { PillPlay, PlayerPlay })
        {
            button.Tag = icon;
            // A triangle's visual centre sits left of its box centre, so the play glyph is nudged right.
            button.Padding = new Thickness(_model.IsPlaying ? 0 : button.FontSize * PlayGlyphOffset, 0, 0, 0);
        }
    }

    /// <summary>Previous, play and next buttons under the taskbar thumbnail, and the song's progress on the taskbar icon.</summary>
    private void SetupTaskbar()
    {
        _thumbPrevious = Thumb("I.Prev", "上一首", () => _model.Previous());
        _thumbToggle = Thumb("I.Play", "播放", () => _model.Toggle());
        _thumbNext = Thumb("I.Next", "下一首", () => _model.Next());
        TaskbarItemInfo = new System.Windows.Shell.TaskbarItemInfo
        {
            ThumbButtonInfos = { _thumbPrevious, _thumbToggle, _thumbNext },
        };
        UpdateTaskbar();

        System.Windows.Shell.ThumbButtonInfo Thumb(string icon, string label, Action click)
        {
            var button = new System.Windows.Shell.ThumbButtonInfo { ImageSource = ThumbIcon(icon), Description = label };
            button.Click += (_, _) => click();
            return button;
        }
    }

    private ImageSource ThumbIcon(string key)
    {
        var pen = new Pen(Brushes.White, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var drawing = new DrawingGroup();
        // A transparent square keeps every icon on the same 24-unit grid, so they line up.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        drawing.Children.Add(new GeometryDrawing(key == "I.Play" ? Brushes.White : null, pen, (Geometry)FindResource(key)));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private void UpdateTaskbar()
    {
        if (TaskbarItemInfo is not { } info || _thumbToggle is null || _thumbPrevious is null || _thumbNext is null) return;
        var playing = _model.IsPlaying;
        if (_thumbShowsPause != playing || _thumbToggle.ImageSource is null)
        {
            _thumbShowsPause = playing;
            _thumbToggle.ImageSource = ThumbIcon(playing ? "I.Pause" : "I.Play");
            _thumbToggle.Description = playing ? "暫停" : "播放";
        }
        _thumbPrevious.IsEnabled = _thumbToggle.IsEnabled = _thumbNext.IsEnabled = _model.HasTrack;

        var state = !_model.HasTrack || _model.Duration <= 0
            ? System.Windows.Shell.TaskbarItemProgressState.None
            : playing ? System.Windows.Shell.TaskbarItemProgressState.Normal : System.Windows.Shell.TaskbarItemProgressState.Paused;
        if (info.ProgressState != state) info.ProgressState = state;
        // The bar is a few hundred pixels at most, so finer updates would not show.
        if (Math.Abs(info.ProgressValue - _model.Progress) > .002) info.ProgressValue = _model.Progress;
    }

    private void UpdateModeIcon() => ModeButton.Tag = FindResource(_model.PlayMode switch
    {
        1 => "I.RepeatOne",
        2 => "I.Shuffle",
        _ => "I.Repeat",
    });

    private void OnProfileClick(object sender, RoutedEventArgs e)
    {
        if (!_model.IsSignedIn) return;
        ProfileMenu.PlacementTarget = ProfileButton;
        ProfileMenu.Placement = PlacementMode.Bottom;
        ProfileMenu.IsOpen = true;
    }

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        SetPlayerOpen(false);
        await _model.SignOutAsync();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximized();
    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized()
    {
        if (_fullscreen)
        {
            SetFullscreen(false);
            return;
        }
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ApplyWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;
        var edge = maximized && !_fullscreen ? SystemParameters.WindowNonClientFrameThickness.Left : 0;
        Root.Margin = new Thickness(edge);
        MaxButton.Tag = FindResource(maximized || _fullscreen ? "I.Restore" : "I.Max");
    }

    /// <summary>Puts the window back where it was last time, if that place is still on a screen.</summary>
    private void RestoreWindowBounds()
    {
        if (_model.SavedWindowBounds is not { } saved) return;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screen, saved);
        if (visible.IsEmpty || visible.Width < 160 || visible.Height < 120) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = saved.Left;
        Top = saved.Top;
        Width = Math.Max(MinWidth, saved.Width);
        Height = Math.Max(MinHeight, saved.Height);
        if (_model.SavedWindowMaximized) WindowState = WindowState.Maximized;
    }

    private void RememberWindow()
    {
        if (_fullscreen)
        {
            _model.RememberWindow(_boundsBeforeFullscreen, _stateBeforeFullscreen == WindowState.Maximized);
            return;
        }
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _model.RememberWindow(bounds, WindowState == WindowState.Maximized);
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => SetFullscreen(!_fullscreen);

    /// <summary>
    /// Fills the whole monitor, over the taskbar. A borderless window exactly the size of the monitor
    /// is what Windows treats as full screen, so the window style is dropped for the duration.
    /// </summary>
    private void SetFullscreen(bool on)
    {
        if (on == _fullscreen) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (on)
        {
            _stateBeforeFullscreen = WindowState;
            _boundsBeforeFullscreen = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var monitor = System.Windows.Forms.Screen.FromHandle(handle).Bounds;
            var dpi = VisualTreeHelper.GetDpi(this);
            _fullscreen = true;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Left = monitor.Left / dpi.DpiScaleX;
            Top = monitor.Top / dpi.DpiScaleY;
            Width = monitor.Width / dpi.DpiScaleX;
            Height = monitor.Height / dpi.DpiScaleY;
            SetCorners(handle, round: false);
        }
        else
        {
            _fullscreen = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Left = _boundsBeforeFullscreen.Left;
            Top = _boundsBeforeFullscreen.Top;
            Width = _boundsBeforeFullscreen.Width;
            Height = _boundsBeforeFullscreen.Height;
            WindowState = _stateBeforeFullscreen;
            SetCorners(handle, round: true);
        }
        FullscreenButton.Tag = FindResource(on ? "I.Shrink" : "I.Expand");
        System.Windows.Automation.AutomationProperties.SetName(FullscreenButton, on ? "結束全屏" : "全屏");
        ApplyWindowState();
    }

    private static void SetCorners(nint handle, bool round)
    {
        var preference = round ? 2 : 1;
        _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
    }

    // Windows 11 snap layouts appear only if the maximize button reports itself as HTMAXBUTTON.
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetCorners(handle, round: true);
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WmNcHitTest = 0x84, WmNcMouseMove = 0xA0, WmNcLButtonDown = 0xA1, WmNcLButtonUp = 0xA2, WmNcMouseLeave = 0x2A2, HtMaxButton = 9;
        switch (msg)
        {
            case WmNcHitTest when IsOverMaxButton(lParam):
                handled = true;
                return HtMaxButton;
            // The button's disabled look doubles as its hover state while Windows owns the pointer.
            case WmNcMouseMove:
                MaxButton.IsEnabled = wParam != HtMaxButton;
                break;
            case WmNcMouseLeave:
                MaxButton.IsEnabled = true;
                break;
            case WmNcLButtonDown when wParam == HtMaxButton:
                handled = true;
                break;
            case WmNcLButtonUp when wParam == HtMaxButton:
                handled = true;
                ToggleMaximized();
                break;
        }
        return 0;
    }

    private bool IsOverMaxButton(nint lParam)
    {
        if (!MaxButton.IsLoaded) return false;
        var x = (short)(lParam & 0xFFFF);
        var y = (short)((lParam >> 16) & 0xFFFF);
        var point = MaxButton.PointFromScreen(new Point(x, y));
        return point.X >= 0 && point.Y >= 0 && point.X < MaxButton.ActualWidth && point.Y < MaxButton.ActualHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
