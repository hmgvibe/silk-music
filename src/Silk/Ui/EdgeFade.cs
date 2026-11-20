using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Silk.Ui;

/// <summary>
/// Fades a scrolling area out toward its top and bottom edges instead of cutting rows off.
/// Each edge fades in as the content scrolls away from it, so the first and last rows show whole at the ends.
/// Set on a ScrollViewer, or on a control whose template holds one (a ListBox).
/// </summary>
public static class EdgeFade
{
    private const double Size = 30;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(EdgeFade), new PropertyMetadata(false, OnEnabledChanged));

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(EdgeFade), new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host || e.NewValue is not true) return;
        // A collapsed list has no template yet, so try again whenever it shows.
        host.Loaded += (_, _) => Hook(host);
        host.IsVisibleChanged += (_, _) => host.Dispatcher.BeginInvoke(() => Hook(host), System.Windows.Threading.DispatcherPriority.Loaded);
        if (host.IsLoaded) Hook(host);
    }

    private static void Hook(FrameworkElement host)
    {
        var viewer = host as ScrollViewer ?? Find<ScrollViewer>(host);
        if (viewer is null || (bool)viewer.GetValue(HookedProperty)) return;
        viewer.SetValue(HookedProperty, true);

        var top = new GradientStop(Colors.Black, 0);
        var topEnd = new GradientStop(Colors.Black, 0);
        var bottomStart = new GradientStop(Colors.Black, 1);
        var bottom = new GradientStop(Colors.Black, 1);
        var mask = new LinearGradientBrush(new GradientStopCollection { top, topEnd, bottomStart, bottom }, new Point(0, 0), new Point(0, 1));

        void Update()
        {
            var height = viewer.ActualHeight;
            if (height <= 0) return;
            var span = Math.Min(Size, height / 2) / height;
            // How far each edge has content beyond it, up to one fade length.
            var above = Math.Clamp(viewer.VerticalOffset / Size, 0, 1);
            var below = Math.Clamp((viewer.ScrollableHeight - viewer.VerticalOffset) / Size, 0, 1);
            top.Color = Color.FromArgb((byte)(255 * (1 - above)), 0, 0, 0);
            topEnd.Offset = span;
            bottomStart.Offset = 1 - span;
            bottom.Color = Color.FromArgb((byte)(255 * (1 - below)), 0, 0, 0);
            host.OpacityMask = above > 0 || below > 0 ? mask : null;
        }

        viewer.ScrollChanged += (_, _) => Update();
        viewer.SizeChanged += (_, _) => Update();
        Update();
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
