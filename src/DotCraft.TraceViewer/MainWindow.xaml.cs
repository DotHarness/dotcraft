using DotCraft.TraceViewer.Services;
using DotCraft.TraceViewer.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace DotCraft.TraceViewer;

public sealed partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly TraceViewerSettingsStore _settingsStore;
    private bool? _isWideLayout;
    private bool? _isCompactLayout;
    private double _detailPaneWidth = 440;

    public MainWindow()
    {
        var app = (App)Application.Current;
        _viewModel = app.ViewModel;
        _settingsStore = app.SettingsStore;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyAppearance(_settingsStore.Load().Appearance, persist: false);
        AppWindow.Resize(new SizeInt32(1280, 800));
        AppWindow.Closing += AppWindow_Closing;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateAdaptiveLayout(1280);
    }

    public MainViewModel ViewModel => _viewModel;

    private void AppearanceOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string value }
            || !Enum.TryParse<AppearancePreference>(value, out var appearance))
        {
            return;
        }

        ApplyAppearance(appearance, persist: true);
        WorkspaceAppearanceFlyout.Hide();
        PaneAppearanceFlyout.Hide();
    }

    private void ApplyAppearance(AppearancePreference appearance, bool persist)
    {
        RootGrid.RequestedTheme = appearance switch
        {
            AppearancePreference.Light => ElementTheme.Light,
            AppearancePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        SystemAppearanceCheck.Visibility = appearance == AppearancePreference.System ? Visibility.Visible : Visibility.Collapsed;
        LightAppearanceCheck.Visibility = appearance == AppearancePreference.Light ? Visibility.Visible : Visibility.Collapsed;
        DarkAppearanceCheck.Visibility = appearance == AppearancePreference.Dark ? Visibility.Visible : Visibility.Collapsed;
        PaneSystemAppearanceCheck.Visibility = appearance == AppearancePreference.System ? Visibility.Visible : Visibility.Collapsed;
        PaneLightAppearanceCheck.Visibility = appearance == AppearancePreference.Light ? Visibility.Visible : Visibility.Collapsed;
        PaneDarkAppearanceCheck.Visibility = appearance == AppearancePreference.Dark ? Visibility.Visible : Visibility.Collapsed;
        UpdateCaptionButtonColors();
        if (persist)
            _settingsStore.SaveAppearance(appearance);
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args) =>
        UpdateCaptionButtonColors();

    private void UpdateCaptionButtonColors()
    {
        var isLight = RootGrid.ActualTheme == ElementTheme.Light;
        var foreground = isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        var inactiveForeground = isLight
            ? Microsoft.UI.ColorHelper.FromArgb(0x99, 0, 0, 0)
            : Microsoft.UI.ColorHelper.FromArgb(0x99, 255, 255, 255);
        var hoverBackground = isLight
            ? Microsoft.UI.ColorHelper.FromArgb(0x12, 0, 0, 0)
            : Microsoft.UI.ColorHelper.FromArgb(0x18, 255, 255, 255);
        var pressedBackground = isLight
            ? Microsoft.UI.ColorHelper.FromArgb(0x1F, 0, 0, 0)
            : Microsoft.UI.ColorHelper.FromArgb(0x26, 255, 255, 255);

        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBackground;
        AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = pressedBackground;
        AppWindow.TitleBar.ButtonPressedForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = inactiveForeground;
    }

    private async void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var workspacePath = await PickerService.PickWorkspaceAsync();
        if (workspacePath is not null)
            await ViewModel.OpenWorkspaceAsync(workspacePath);
    }

    private async void OpenRecent_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.OpenRecentWorkspaceAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();

    private void Session_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SessionListItem session)
            return;

        ViewModel.OpenSession(session);
        if (_isWideLayout != true)
            ViewModel.IsSessionPaneOpen = false;
    }

    private void Session_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsSessionsPage && sender is ListView { SelectedItem: SessionListItem session })
            ViewModel.OpenSession(session);
    }

    private void ShowSessions_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ShowSessions();

    private void ToggleSessionPane_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleSessionPane();

    private void SessionMode_Click(object sender, RoutedEventArgs e)
    {
        var timeline = ReferenceEquals(sender, TimelineViewButton);
        if (timeline) ViewModel.ShowTimeline(); else ViewModel.ShowReview();
    }

    private async void AnalyzeTrace_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.AnalyzeTraceAsync();

    private async void AskReview_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.AskReviewAsync();

    private void StopAnalysis_Click(object sender, RoutedEventArgs e) => ViewModel.CancelAnalysis();

    private void AttachFinding_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReviewFindingItem finding })
            ViewModel.AttachFinding(finding);
    }

    private void Evidence_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReviewEvidenceItem evidence })
        {
            ViewModel.ShowEvidence(evidence, switchToTimeline: true);
            TimelineViewButton.IsChecked = true;
            ReviewViewButton.IsChecked = false;
            EventList.SelectedItem = ViewModel.SelectedEvent;
            EventList.ScrollIntoView(ViewModel.SelectedEvent);
            OpenEventDetail();
        }
    }

    private void EventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: EventRowItem row })
            ViewModel.SelectedEvent = row;
        UpdateDetailSelector();
    }

    private void EventList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EventRowItem row)
        {
            SelectAndOpenEvent(row);
            return;
        }

        if (e.ClickedItem is TrajectoryListItem group)
        {
            ViewModel.ToggleTrajectoryGroup(group);
            EventList.SelectedItem = ViewModel.SelectedEvent;
        }
    }

    private void EventList_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: EventRowItem row })
            SelectAndOpenEvent(row);
    }

    private void Timeline_MarkerSelected(object? sender, string rowId)
    {
        ViewModel.SelectEvent(rowId);
        EventList.SelectedItem = ViewModel.SelectedEvent;
        EventList.ScrollIntoView(ViewModel.SelectedEvent);
        OpenEventDetail();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedEvent))
        {
            EventList.SelectedItem = ViewModel.SelectedEvent;
            UpdateDetailSelector();
        }
    }

    private void UpdateDetailSelector()
    {
        DetailSelector.Items.Clear();
        foreach (var section in ViewModel.SelectedEvent?.Detail.Sections ?? [])
            DetailSelector.Items.Add(new SelectorBarItem { Text = section.Label, Tag = section });
        DetailSelector.SelectedItem = DetailSelector.Items.FirstOrDefault();
    }

    private void DetailSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is DetailSectionItem section)
            ViewModel.SelectedDetailSection = section;
    }

    private void ToggleEventDetail_Click(object sender, RoutedEventArgs e) =>
        EventDetailSplitView.IsPaneOpen = !EventDetailSplitView.IsPaneOpen;

    private void OpenEventDetail()
    {
        if (ViewModel.SelectedEvent is not null)
            EventDetailSplitView.IsPaneOpen = true;
    }

    private void SelectAndOpenEvent(EventRowItem row)
    {
        ViewModel.SelectedEvent = row;
        OpenEventDetail();
    }

    private void TimelineMode_Click(object sender, RoutedEventArgs e)
    {
        var duration = ReferenceEquals(sender, DurationModeButton);
        ViewModel.TimelineScaleMode = duration ? TimelineScaleMode.Duration : TimelineScaleMode.Sequence;
    }

    private void TimelineZoomOut_Click(object sender, RoutedEventArgs e) =>
        ViewModel.TimelineZoomFactor = Math.Max(1, ViewModel.TimelineZoomFactor - 0.25);

    private void TimelineZoomIn_Click(object sender, RoutedEventArgs e) =>
        ViewModel.TimelineZoomFactor = Math.Min(3, ViewModel.TimelineZoomFactor + 0.25);

    private void Timeline_RangeChanged(object? sender, Controls.TimelineRangeChangedEventArgs e)
    {
        ViewModel.AttachTimelineRange(e.StartRatio, e.EndRatio);
    }

    private void DetailResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isWideLayout != true)
            return;
        _detailPaneWidth = Math.Clamp(_detailPaneWidth - e.HorizontalChange, 340, 620);
        EventDetailSplitView.OpenPaneLength = _detailPaneWidth;
    }

    private async void LoadOlder_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadOlderEventsAsync();

    private void CopyEvent_Click(object sender, RoutedEventArgs e)
        => CopySelectedEvent();

    private void CopySelectedEvent()
    {
        if (ViewModel.SelectedEvent is null)
            return;

        var package = new DataPackage();
        package.SetText(ViewModel.SelectedEvent.Detail.CopyText);
        Clipboard.SetContent(package);
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        EventSearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private async void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.RefreshAsync();
    }

    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (EventDetailSplitView.IsPaneOpen)
            EventDetailSplitView.IsPaneOpen = false;
        else if (_isWideLayout == false && ViewModel.IsSessionPaneOpen)
            ViewModel.IsSessionPaneOpen = false;
        else
            return;
        args.Handled = true;
    }

    private void Copy_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SelectedEvent is null)
            return;
        CopySelectedEvent();
        args.Handled = true;
    }

    private void EventList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (EventList.Items.Count == 0)
            return;
        if (e.Key == VirtualKey.Home)
            EventList.SelectedItem = ViewModel.VisibleTrajectory.OfType<EventRowItem>().FirstOrDefault();
        else if (e.Key == VirtualKey.End)
            EventList.SelectedItem = ViewModel.VisibleTrajectory.OfType<EventRowItem>().LastOrDefault();
        else if (e.Key == VirtualKey.Enter && ViewModel.SelectedEvent is not null)
        {
            EventDetailSplitView.IsPaneOpen = true;
            e.Handled = true;
            return;
        }
        else
            return;
        EventList.ScrollIntoView(EventList.SelectedItem);
        e.Handled = true;
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateAdaptiveLayout(e.NewSize.Width);

    private void UpdateAdaptiveLayout(double width)
    {
        var isWide = width >= 1008;
        if (_isWideLayout != isWide)
        {
            _isWideLayout = isWide;
            WorkbenchSplitView.DisplayMode = isWide ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
            ViewModel.IsSessionPaneOpen = isWide;
            SessionPaneButton.Visibility = isWide ? Visibility.Collapsed : Visibility.Visible;
            CloseSessionPaneButton.Visibility = isWide ? Visibility.Collapsed : Visibility.Visible;
        }

        EventDetailSplitView.OpenPaneLength = width < 680
            ? Math.Max(320, width - 24)
            : Math.Min(_detailPaneWidth, Math.Max(340, width - 320));
        DetailResizeThumb.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;

        var isCompact = width < 760;
        if (_isCompactLayout == isCompact)
            return;

        _isCompactLayout = isCompact;
        Grid.SetColumn(WorkspaceActionPanel, isCompact ? 0 : 1);
        Grid.SetRow(WorkspaceActionPanel, isCompact ? 1 : 0);
        WorkspaceActionsColumn.Width = isCompact ? new GridLength(0) : GridLength.Auto;
        WorkspaceActionPanel.Margin = isCompact ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        Grid.SetColumn(SessionTitlePanel, isCompact ? 0 : 1);
        Grid.SetRow(SessionTitlePanel, isCompact ? 1 : 0);
        Grid.SetColumnSpan(SessionTitlePanel, isCompact ? 3 : 1);
        SessionTitlePanel.Margin = isCompact ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        SessionTitleText.FontSize = isCompact ? 16 : 18;
        EventWindowLabel.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        ViewModel.IsCompactLayout = isCompact;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        AppWindow.Closing -= AppWindow_Closing;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Dispose();
    }
}
