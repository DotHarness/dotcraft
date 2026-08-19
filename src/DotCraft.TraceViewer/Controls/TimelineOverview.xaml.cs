using DotCraft.TraceViewer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DotCraft.TraceViewer.Controls;

public sealed partial class TimelineOverview
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<TimelineMarkerItem>), typeof(TimelineOverview),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty SelectedRowIdProperty = DependencyProperty.Register(
        nameof(SelectedRowId), typeof(string), typeof(TimelineOverview),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty ScaleModeProperty = DependencyProperty.Register(
        nameof(ScaleMode), typeof(TimelineScaleMode), typeof(TimelineOverview),
        new PropertyMetadata(TimelineScaleMode.Sequence, OnVisualPropertyChanged));

    public static readonly DependencyProperty ZoomFactorProperty = DependencyProperty.Register(
        nameof(ZoomFactor), typeof(double), typeof(TimelineOverview),
        new PropertyMetadata(1d, OnVisualPropertyChanged));

    private double? _rangeStart;
    private Rectangle? _rangeVisual;
    private bool _isPanning;
    private double _panPointerStart;
    private double _panScrollStart;

    public TimelineOverview()
    {
        InitializeComponent();
        TimelineScroll.AddHandler(PointerPressedEvent, new PointerEventHandler(TimelineScroll_PointerPressed), true);
        TimelineScroll.AddHandler(PointerMovedEvent, new PointerEventHandler(TimelineScroll_PointerMoved), true);
        TimelineScroll.AddHandler(PointerReleasedEvent, new PointerEventHandler(TimelineScroll_PointerReleased), true);
        TimelineScroll.AddHandler(PointerCanceledEvent, new PointerEventHandler(TimelineScroll_PointerReleased), true);
        Loaded += (_, _) => Render();
        ActualThemeChanged += (_, _) => Render();
    }

    public IReadOnlyList<TimelineMarkerItem>? Items
    {
        get => (IReadOnlyList<TimelineMarkerItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? SelectedRowId
    {
        get => (string?)GetValue(SelectedRowIdProperty);
        set => SetValue(SelectedRowIdProperty, value);
    }

    public TimelineScaleMode ScaleMode
    {
        get => (TimelineScaleMode)GetValue(ScaleModeProperty);
        set => SetValue(ScaleModeProperty, value);
    }

    public double ZoomFactor
    {
        get => (double)GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public event EventHandler<string>? MarkerSelected;

    public event EventHandler<TimelineRangeChangedEventArgs>? RangeChanged;

    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((TimelineOverview)sender).Render();

    private void TimelineScroll_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private void Render()
    {
        Plot.Children.Clear();
        _rangeVisual = null;
        var viewportWidth = TimelineScroll.ActualWidth;
        if (viewportWidth <= 0)
            return;

        Plot.Width = Math.Max(viewportWidth, viewportWidth * Math.Clamp(ZoomFactor, 1, 3));
        DrawLaneBackgrounds();
        if (Items is not { Count: > 0 } items)
            return;

        var minimum = items.Min(static item => item.Start);
        var maximum = items.Max(static item => item.End ?? item.Start);
        var range = Math.Max(1, (maximum - minimum).TotalMilliseconds);
        DrawTurnBoundaries(items, minimum, range);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var left = Position(item.Start, index, items.Count, minimum, range);
            var end = item.End ?? item.Start;
            var width = item.End is null
                ? 3
                : Math.Max(3, Position(end, index + 1, items.Count, minimum, range) - left);
            width = Math.Min(width, Math.Max(3, Plot.Width - left));

            var selected = string.Equals(item.RowId, SelectedRowId, StringComparison.Ordinal);
            var visual = new Rectangle
            {
                Width = width,
                Height = 6,
                RadiusX = 1,
                RadiusY = 1,
                Fill = BrushFor(item),
                Opacity = item.IsPartial ? 0.45 : 0.84,
                Stroke = selected ? ResourceBrush("TimelineSelectionStrokeBrush") : null,
                StrokeThickness = selected ? 2 : 0,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(visual, left);
            Canvas.SetTop(visual, (int)item.Lane * 16 + 22);
            Plot.Children.Add(visual);

            var marker = new Button
            {
                Width = Math.Max(14, width),
                Height = 14,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = item.RowId,
            };
            ToolTipService.SetToolTip(marker, item.Title + (item.IsPartial ? " · partial trace" : string.Empty));
            AutomationProperties.SetName(marker, $"{item.Lane}: {item.Title}");
            marker.Click += Marker_Click;
            Canvas.SetLeft(marker, Math.Max(0, left - (marker.Width - width) / 2));
            Canvas.SetTop(marker, (int)item.Lane * 16 + 18);
            Plot.Children.Add(marker);
        }
    }

    private void DrawLaneBackgrounds()
    {
        for (var lane = 0; lane < 3; lane++)
        {
            var background = new Rectangle
            {
                Width = Plot.Width,
                Height = 11,
                Fill = ResourceBrush("TimelineLaneSurfaceBrush"),
                IsHitTestVisible = false,
            };
            Canvas.SetTop(background, lane * 16 + 19);
            Plot.Children.Add(background);
        }
    }

    private void DrawTurnBoundaries(
        IReadOnlyList<TimelineMarkerItem> items,
        DateTimeOffset minimum,
        double range)
    {
        foreach (var group in items.GroupBy(static item => item.TurnKey, StringComparer.Ordinal))
        {
            var first = group.First();
            var index = IndexOf(items, first);
            var left = Position(first.Start, index, items.Count, minimum, range);
            var line = new Rectangle
            {
                Width = 1,
                Height = 52,
                Fill = ResourceBrush("TimelineDividerBrush"),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(line, left);
            Canvas.SetTop(line, 14);
            Plot.Children.Add(line);

            var label = new TextBlock
            {
                Text = first.TurnTitle,
                FontSize = 10,
                Foreground = ResourceBrush("TimelineTurnTextBrush"),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, Math.Min(left + 4, Math.Max(0, Plot.Width - 70)));
            Plot.Children.Add(label);
        }
    }

    private double Position(
        DateTimeOffset timestamp,
        int index,
        int count,
        DateTimeOffset minimum,
        double range) => ScaleMode == TimelineScaleMode.Sequence
        ? Math.Min(Plot.Width - 4, (double)index / Math.Max(1, count - 1) * (Plot.Width - 8) + 4)
        : (timestamp - minimum).TotalMilliseconds / range * Math.Max(1, Plot.Width - 8) + 4;

    private void Marker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string rowId })
            MarkerSelected?.Invoke(this, rowId);
    }

    private void Plot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsMiddleButtonPress(e))
        {
            BeginPanning(e);
            return;
        }

        if (e.OriginalSource is Button)
            return;
        _rangeStart = e.GetCurrentPoint(Plot).Position.X;
        Plot.CapturePointer(e.Pointer);
        UpdateRange(_rangeStart.Value, _rangeStart.Value);
        e.Handled = true;
    }

    private void Plot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_rangeStart is null || !e.GetCurrentPoint(Plot).Properties.IsLeftButtonPressed)
            return;
        UpdateRange(_rangeStart.Value, e.GetCurrentPoint(Plot).Position.X);
        e.Handled = true;
    }

    private void Plot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_rangeStart is null)
            return;
        var end = e.GetCurrentPoint(Plot).Position.X;
        UpdateRange(_rangeStart.Value, end);
        Plot.ReleasePointerCapture(e.Pointer);
        var startRatio = Math.Clamp(Math.Min(_rangeStart.Value, end) / Math.Max(1, Plot.Width), 0, 1);
        var endRatio = Math.Clamp(Math.Max(_rangeStart.Value, end) / Math.Max(1, Plot.Width), 0, 1);
        _rangeStart = null;
        RangeChanged?.Invoke(this, new TimelineRangeChangedEventArgs(startRatio, endRatio));
        e.Handled = true;
    }

    private void TimelineScroll_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isPanning)
        {
            e.Handled = true;
            return;
        }

        if (!IsMiddleButtonPress(e))
            return;

        BeginPanning(e);
    }

    private bool IsMiddleButtonPress(PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(TimelineScroll).Properties;
        return properties.IsMiddleButtonPressed
            || properties.PointerUpdateKind.Equals(Windows.UI.Input.PointerUpdateKind.MiddleButtonPressed);
    }

    private void BeginPanning(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(TimelineScroll);
        _isPanning = true;
        _panPointerStart = point.Position.X;
        _panScrollStart = TimelineScroll.HorizontalOffset;
        TimelineScroll.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void TimelineScroll_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning)
            return;

        var point = e.GetCurrentPoint(TimelineScroll);
        var offset = Math.Clamp(
            _panScrollStart - (point.Position.X - _panPointerStart),
            0,
            TimelineScroll.ScrollableWidth);
        TimelineScroll.ChangeView(offset, null, null, disableAnimation: true);
        e.Handled = true;
    }

    private void TimelineScroll_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isPanning)
            StopPanning(e);
    }

    private void StopPanning(PointerRoutedEventArgs e)
    {
        _isPanning = false;
        TimelineScroll.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void UpdateRange(double first, double second)
    {
        var left = Math.Clamp(Math.Min(first, second), 0, Plot.Width);
        var width = Math.Clamp(Math.Abs(second - first), 1, Plot.Width - left);
        if (_rangeVisual is null)
        {
            _rangeVisual = new Rectangle
            {
                Height = 52,
                Fill = ResourceBrush("TimelineAccentBrush"),
                Opacity = 0.12,
                Stroke = ResourceBrush("TimelineAccentBrush"),
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetTop(_rangeVisual, 14);
            Plot.Children.Add(_rangeVisual);
        }
        _rangeVisual.Width = width;
        Canvas.SetLeft(_rangeVisual, left);
    }

    private Brush BrushFor(TimelineMarkerItem item)
    {
        var key = item.IsError
            ? "TimelineErrorBrush"
            : item.Lane switch
            {
                TimelineLane.Input => "TimelineInputBrush",
                TimelineLane.Model => "TimelineModelBrush",
                TimelineLane.Tools => "TimelineToolBrush",
                _ => "TimelineToolBrush",
            };
        return ResourceBrush(key);
    }

    private Brush ResourceBrush(string key)
    {
        var themeKey = ActualTheme == ElementTheme.Light ? "Light" : "Default";
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var themeValue)
            && themeValue is ResourceDictionary themeResources
            && themeResources.TryGetValue(key, out var value)
            && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private static int IndexOf(IReadOnlyList<TimelineMarkerItem> items, TimelineMarkerItem item)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
                return index;
        }
        return 0;
    }
}

public sealed class TimelineRangeChangedEventArgs(double startRatio, double endRatio) : EventArgs
{
    public double StartRatio { get; } = startRatio;

    public double EndRatio { get; } = endRatio;
}
