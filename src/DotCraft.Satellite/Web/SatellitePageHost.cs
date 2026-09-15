using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace DotCraft.Satellite.Web;

internal static class SatellitePageHost
{
    private static Task<CoreWebView2Environment>? _environment;

    public static JsonSerializerOptions JsonOptions { get; } =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Page(string logicalName)
    {
        using var stream = typeof(SatellitePageHost).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"The page '{logicalName}' is not embedded in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Throws when the WebView2 Runtime is missing; the caller owns what to show instead.</summary>
    public static async Task<SatellitePage> AttachAsync(
        nint hwnd,
        string logicalName,
        bool transparent,
        Action<JsonElement> onMessage,
        Action? onProcessFailed = null)
    {
        var environment = await EnvironmentAsync();
        var controller = await environment.CreateCoreWebView2ControllerAsync(
            CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd));
        RaiseAboveXaml(hwnd);
        if (transparent)
            controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        var core = controller.CoreWebView2;
        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsWebMessageEnabled = true;

        var page = new SatellitePage(controller, onMessage, onProcessFailed);
        page.Navigate(Page(logicalName));
        return page;
    }

    public static void Post(CoreWebView2 core, object state) =>
        core.PostWebMessageAsJson(JsonSerializer.Serialize(state, JsonOptions));

    private static Task<CoreWebView2Environment> EnvironmentAsync() => _environment ??= CreateEnvironmentAsync();

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        // Plain windowed hosting inside a WinUI 3 window gets no mouse input on Windows 11
        // (microsoft-ui-xaml #10826); window-to-visual hosting is the documented way around it.
        Environment.SetEnvironmentVariable("COREWEBVIEW2_FORCED_HOSTING_MODE", "COREWEBVIEW2_HOSTING_MODE_WINDOW_TO_VISUAL");
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotCraft",
            "Satellite",
            "WebView2");
        return await CoreWebView2Environment.CreateWithOptionsAsync(null, folder, new CoreWebView2EnvironmentOptions());
    }

    /// <summary>
    /// WinUI keeps its own content window over the whole client area, above the child WebView2
    /// creates, so the page window has to be moved to the top or the window shows solid black.
    /// </summary>
    private static void RaiseAboveXaml(nint hwnd)
    {
        var page = FindWindowExW(hwnd, nint.Zero, "Chrome_WidgetWin_0", null);
        if (page != nint.Zero)
            SetWindowPos(page, nint.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(nint parent, nint after, string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}

internal sealed class SatellitePage : IDisposable
{
    private readonly CoreWebView2Controller _controller;
    private readonly CoreWebView2 _core;
    private readonly Action<JsonElement> _onMessage;
    private readonly Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs>
        _navigationStarting;
    private readonly Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NewWindowRequestedEventArgs>
        _newWindowRequested;
    private readonly Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs>
        _webMessageReceived;
    private readonly Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs>?
        _processFailed;
    private bool _landed;
    private bool _disposed;

    public SatellitePage(
        CoreWebView2Controller controller,
        Action<JsonElement> onMessage,
        Action? onProcessFailed)
    {
        _controller = controller;
        _core = controller.CoreWebView2;
        _onMessage = onMessage;
        _navigationStarting = OnNavigationStarting;
        _newWindowRequested = OnNewWindowRequested;
        _webMessageReceived = OnWebMessageReceived;
        _processFailed = onProcessFailed is null ? null : (_, _) => onProcessFailed();
        _core.NavigationStarting += _navigationStarting;
        _core.NewWindowRequested += _newWindowRequested;
        _core.WebMessageReceived += _webMessageReceived;
        if (_processFailed is not null)
            _core.ProcessFailed += _processFailed;
    }

    public CoreWebView2Controller Controller => _controller;

    public void Navigate(string html) => _core.NavigateToString(html);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _core.NavigationStarting -= _navigationStarting;
        _core.NewWindowRequested -= _newWindowRequested;
        _core.WebMessageReceived -= _webMessageReceived;
        if (_processFailed is not null)
            _core.ProcessFailed -= _processFailed;
        _controller.Close();
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        args.Cancel = _landed;
        _landed = true;
    }

    private static void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args) =>
        args.Handled = true;

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var document = JsonDocument.Parse(args.WebMessageAsJson);
        _onMessage(document.RootElement);
    }
}
