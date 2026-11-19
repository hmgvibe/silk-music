using System.Windows;
using System.Windows.Controls;

namespace Silk.Ui;

/// <summary>Chooses between a song row and the headers that sit at the top of the same list.</summary>
public sealed class SongListTemplates : DataTemplateSelector
{
    public DataTemplate? Row { get; set; }
    public DataTemplate? Detail { get; set; }
    public DataTemplate? Singers { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        DetailHeader => Detail,
        SingerHeader => Singers,
        _ => Row,
    };
}
