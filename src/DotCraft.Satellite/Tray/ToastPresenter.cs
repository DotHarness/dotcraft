using System.Runtime.Versioning;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DotCraft.Satellite.Tray;

/// <summary>
/// Unpackaged notification registration can be refused, so the shell balloon stays as the
/// fallback rather than the notice being dropped.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class ToastPresenter(ITrayIcon tray) : IDisposable
{
    private bool _registered;

    public void Register()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception)
        {
            _registered = false;
        }
    }

    public void Show(string title, string body)
    {
        if (_registered)
        {
            try
            {
                AppNotificationManager.Default.Show(
                    new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification());
                return;
            }
            catch (Exception)
            {
                _registered = false;
            }
        }

        tray.ShowBalloon(title, body);
    }

    public void Dispose()
    {
        if (!_registered)
            return;
        _registered = false;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception) { }
    }
}
