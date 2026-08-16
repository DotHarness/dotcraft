using DotCraft.TraceViewer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DotCraft.TraceViewer.Controls;

public sealed class DetailBlockTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ProseTemplate { get; set; }

    public DataTemplate? CodeTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is DetailBlockItem { IsCode: true } ? CodeTemplate : ProseTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
