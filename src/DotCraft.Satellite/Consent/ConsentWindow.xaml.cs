using DotCraft.Satellite.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace DotCraft.Satellite.Consent;

internal sealed partial class ConsentWindow : Window
{
    private const int WidthDips = 560;
    private const int HeightDips = 500;

    public ConsentWindow(ConsentViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = viewModel.WindowTitle;
        ViewModel.Finished += (_, accepted) =>
        {
            Accepted = accepted;
            Close();
        };
        Activated += OnFirstActivation;
        Resize();
    }

    public ConsentViewModel ViewModel { get; }

    public bool Accepted { get; private set; }

    private void OnFirstActivation(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivation;
        DeclineButton.Focus(FocusState.Programmatic);
    }

    private void Resize()
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "satellite.ico"));
        var scale = appWindow.Presenter is OverlappedPresenter ? RasterizationScale(windowId) : 1.0;
        var width = (int)Math.Round(WidthDips * scale);
        var height = (int)Math.Round(HeightDips * scale);
        var work = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            work.X + ((work.Width - width) / 2),
            work.Y + ((work.Height - height) / 2),
            width,
            height));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }
    }

    private static double RasterizationScale(WindowId windowId)
    {
        var dpi = Tray.TrayNativeMethods.GetDpiForWindow(Win32Interop.GetWindowFromWindowId(windowId));
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }
}
