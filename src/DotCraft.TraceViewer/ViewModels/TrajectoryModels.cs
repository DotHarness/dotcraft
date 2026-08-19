using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using DotCraft.Tracing;

namespace DotCraft.TraceViewer.ViewModels;

public enum TimelineLane
{
    Input,
    Model,
    Tools,
}

public enum TimelineScaleMode
{
    Duration,
    Sequence,
}

public abstract class TrajectoryListItem
{
    public abstract string Id { get; init; }
}

public sealed class TurnHeaderItem : TrajectoryListItem
{
    public required override string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required bool IsCollapsed { get; init; }
    public string ToggleGlyph => IsCollapsed ? "\uE76C" : "\uE70D";
}

public sealed class ModelCallHeaderItem : TrajectoryListItem
{
    public required override string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required bool IsCollapsed { get; init; }
    public string ToggleGlyph => IsCollapsed ? "\uE76C" : "\uE70D";
}

public sealed class TurnGroupItem : ObservableCollection<EventRowItem>
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

}

public sealed class EventRowItem : TrajectoryListItem
{
    public required override string Id { get; init; }

    public required IReadOnlyList<TraceEvent> SourceEvents { get; init; }

    public required EventDetailItem Detail { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Time { get; init; }

    public required string Badge { get; init; }

    public required TimelineLane Lane { get; init; }

    public required bool IsDiagnostic { get; init; }

    public required bool IsError { get; init; }

    public int? ModelCallIndex { get; internal set; }

    public string SearchText => string.Join('\n', Title, Summary, Badge, Detail.SearchText);
}

public sealed class TimelineMarkerItem
{
    public required string RowId { get; init; }

    public required string Title { get; init; }

    public required TimelineLane Lane { get; init; }

    public required DateTimeOffset Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public required bool IsPartial { get; init; }

    public required bool IsError { get; init; }

    public required string TurnKey { get; init; }

    public required string TurnTitle { get; init; }
}

public sealed class EventDetailItem
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required IReadOnlyList<DetailSectionItem> Sections { get; init; }

    public string SearchText => string.Join('\n', Sections.Select(static section => section.SearchText));

    public string CopyText
    {
        get
        {
            var builder = new StringBuilder().AppendLine(Title).AppendLine(Subtitle);
            foreach (var section in Sections)
            {
                builder.AppendLine().AppendLine(section.Label);
                foreach (var detailField in section.Fields)
                    builder.AppendLine(CultureInfo.CurrentCulture, $"{detailField.Label}: {detailField.Value}");
                foreach (var block in section.Blocks)
                    builder.AppendLine().AppendLine(block.Label).AppendLine(block.Value);
            }

            return builder.ToString().TrimEnd();
        }
    }
}

public sealed class DetailSectionItem
{
    public required string Label { get; init; }

    public required IReadOnlyList<DetailFieldItem> Fields { get; init; }

    public required IReadOnlyList<DetailBlockItem> Blocks { get; init; }

    public string SearchText => string.Join('\n',
        Fields.Select(static detailField => $"{detailField.Label} {detailField.Value}")
            .Concat(Blocks.Select(static block => $"{block.Label} {block.Value}")));
}

public sealed record DetailFieldItem(string Label, string Value);

public sealed record DetailBlockItem(string Label, string Value, bool IsCode = false);

internal sealed record TrajectoryProjectionResult(
    IReadOnlyList<TurnGroupItem> Turns,
    IReadOnlyList<EventRowItem> Rows,
    IReadOnlyList<TimelineMarkerItem> Timeline);
