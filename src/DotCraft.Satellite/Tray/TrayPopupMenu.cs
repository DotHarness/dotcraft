using System.Runtime.Versioning;
using DotCraft.Satellite.ViewModels;

namespace DotCraft.Satellite.Tray;

[SupportedOSPlatform("windows")]
internal static class TrayPopupMenu
{
    public static TrayMenuItem? Show(nint window, IReadOnlyList<TrayMenuItem> items)
    {
        var byId = new Dictionary<int, TrayMenuItem>();
        var menu = Build(items, byId);
        if (menu == 0)
            return null;

        try
        {
            if (!TrayNativeMethods.GetCursorPos(out var cursor))
                return null;
            // The shell requires the owner to be foreground, and the trailing null message is the
            // documented way to let the menu close when the user clicks elsewhere.
            TrayNativeMethods.SetForegroundWindow(window);
            var selected = TrayNativeMethods.TrackPopupMenuEx(
                menu,
                TrayNativeMethods.TPM_RIGHTBUTTON | TrayNativeMethods.TPM_RETURNCMD,
                cursor.X,
                cursor.Y,
                window,
                0);
            TrayNativeMethods.PostMessageW(window, 0, 0, 0);
            return selected != 0 && byId.TryGetValue(selected, out var item) ? item : null;
        }
        finally
        {
            TrayNativeMethods.DestroyMenu(menu);
        }
    }

    private static nint Build(IReadOnlyList<TrayMenuItem> items, Dictionary<int, TrayMenuItem> byId)
    {
        var menu = TrayNativeMethods.CreatePopupMenu();
        if (menu == 0)
            return 0;

        foreach (var item in items)
        {
            if (item.Command == TrayMenuCommand.Separator)
            {
                TrayNativeMethods.AppendMenuW(menu, TrayNativeMethods.MF_SEPARATOR, 0, null);
                continue;
            }

            if (item.Children is { Count: > 0 } children)
            {
                var submenu = Build(children, byId);
                var flags = (uint)(TrayNativeMethods.MF_POPUP
                                   | (item.Enabled ? 0 : TrayNativeMethods.MF_GRAYED));
                TrayNativeMethods.AppendMenuW(menu, flags, (nuint)submenu, item.Text);
                continue;
            }

            var id = byId.Count + 1;
            byId[id] = item;
            TrayNativeMethods.AppendMenuW(
                menu,
                (uint)(TrayNativeMethods.MF_STRING | (item.Enabled ? 0 : TrayNativeMethods.MF_GRAYED)),
                (nuint)id,
                item.Text);
        }

        return menu;
    }
}
