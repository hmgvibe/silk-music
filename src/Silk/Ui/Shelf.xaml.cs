using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Silk.Ui;

/// <summary>A horizontal row of cards. The wheel keeps scrolling the page; arrows or Shift+wheel move the row.</summary>
public partial class Shelf : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Shelf), new PropertyMetadata("", (d, e) =>
        {
            var shelf = (Shelf)d;
            shelf.Header.Text = (string)e.NewValue;
            shelf.Header.Visibility = string.IsNullOrEmpty((string)e.NewValue) ? Visibility.Collapsed : Visibility.Visible;
        }));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(Shelf),
        new PropertyMetadata(null, (d, e) => ((Shelf)d).Items.ItemsSource = (IEnumerable?)e.NewValue));

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(Shelf),
        new PropertyMetadata(null, (d, e) => ((Shelf)d).Items.ItemTemplate = (DataTemplate?)e.NewValue));

    public static readonly DependencyProperty ArrowTopProperty = DependencyProperty.Register(
        nameof(ArrowTop), typeof(double), typeof(Shelf), new PropertyMetadata(48d, (d, e) =>
        {
            var shelf = (Shelf)d;
            var top = (double)e.NewValue;
            shelf.Back.Margin = new Thickness(-10, top, 0, 0);
            shelf.Forward.Margin = new Thickness(0, top, 14, 0);
        }));

    public Shelf() => InitializeComponent();

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public DataTemplate? ItemTemplate { get => (DataTemplate?)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public double ArrowTop { get => (double)GetValue(ArrowTopProperty); set => SetValue(ArrowTopProperty, value); }

    private void OnBack(object sender, RoutedEventArgs e) => MoveTo(Scroller.HorizontalOffset - Scroller.ViewportWidth * .75);
    private void OnForward(object sender, RoutedEventArgs e) => MoveTo(Scroller.HorizontalOffset + Scroller.ViewportWidth * .75);
    private void OnRowHover(object sender, MouseEventArgs e) => UpdateArrows();
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateArrows();

    private void MoveTo(double offset)
    {
        var from = Scroller.HorizontalOffset;
        var to = Math.Clamp(offset, 0, Scroller.ScrollableWidth);
        Tween.Run(Scroller, 280, t => Scroller.ScrollToHorizontalOffset(from + (to - from) * t));
    }

    private void UpdateArrows()
    {
        var active = Row.IsMouseOver;
        Back.Visibility = active && Scroller.HorizontalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;
        Forward.Visibility = active && Scroller.HorizontalOffset < Scroller.ScrollableWidth - 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            MoveTo(Scroller.HorizontalOffset - e.Delta * 1.4);
            return;
        }
        // A horizontal scroller would otherwise swallow the wheel and stop the page from moving.
        RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this });
    }
}
