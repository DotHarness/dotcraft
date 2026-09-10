using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DotCraft.Satellite.Island;

/// <summary>A window backdrop that paints nothing, so every pixel the content leaves clear shows the desktop.</summary>
internal sealed partial class TransparentBackdrop : SystemBackdrop
{
    private const int DispatcherQueueThreadCurrent = 2;
    private const int DispatcherQueueApartmentSta = 2;

    // The backdrop target belongs to the system compositor, which needs a system dispatcher queue
    // on the thread; WinUI's own queue does not count.
    private static Windows.UI.Composition.Compositor? _compositor;
    private static nint _dispatcherQueueController;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        // Transparent black, not XAML's transparent white: a colour whose channels exceed its alpha
        // is not a premultiplied colour, and the compositor shows it as a haze.
        connectedTarget.SystemBackdrop = Compositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        disconnectedTarget.SystemBackdrop = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }

    private static Windows.UI.Composition.Compositor Compositor()
    {
        if (_compositor is not null)
            return _compositor;
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new DispatcherQueueOptions
            {
                Size = Marshal.SizeOf<DispatcherQueueOptions>(),
                ThreadType = DispatcherQueueThreadCurrent,
                ApartmentType = DispatcherQueueApartmentSta
            };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _dispatcherQueueController));
        }
        _compositor = new Windows.UI.Composition.Compositor();
        return _compositor;
    }
}
