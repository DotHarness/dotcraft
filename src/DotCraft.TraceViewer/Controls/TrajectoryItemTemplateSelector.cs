using DotCraft.TraceViewer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DotCraft.TraceViewer.Controls;

public sealed class TrajectoryItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TurnTemplate { get; set; }

    public DataTemplate? ModelCallTemplate { get; set; }

    public DataTemplate? EventTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        TurnHeaderItem => TurnTemplate,
        ModelCallHeaderItem => ModelCallTemplate,
        EventRowItem => EventTemplate,
        _ => base.SelectTemplateCore(item),
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
