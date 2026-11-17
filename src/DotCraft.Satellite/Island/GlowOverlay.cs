using DotCraft.Satellite.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace DotCraft.Satellite.Island;

/// <summary>The edge glow on every display. A display change rebuilds the windows on the next show.</summary>
internal sealed class GlowOverlay(SatelliteCallbackGuard guard) : IDisposable
{
    private readonly List<GlowWindow> _windows = [];
    private string _layout = string.Empty;
    private bool _failed;

    public void Show(RectInt32 pill)
    {
        if (_failed)
            return;
        var displays = Displays();
        var layout = string.Join(';', displays.Select(rect => $"{rect.X},{rect.Y},{rect.Width},{rect.Height}"));
        if (layout != _layout)
        {
            Clear();
            _layout = layout;
            var reduceMotion = !new UISettings().AnimationsEnabled;
            foreach (var display in displays)
                _windows.Add(new GlowWindow(display, reduceMotion, guard, Fail));
        }

        var centre = new PointInt32(pill.X + (pill.Width / 2), pill.Y + (pill.Height / 2));
        foreach (var window in _windows)
            window.Show(centre);
    }

    public void Hide()
    {
        foreach (var window in _windows)
            window.Hide();
    }

    public void Dispose() => Clear();

    /// <summary>A glow that cannot load stays off, since the island and tray already show the same activity.</summary>
    private void Fail()
    {
        _failed = true;
        Clear();
    }

    private void Clear()
    {
        foreach (var window in _windows)
            window.Dispose();
        _windows.Clear();
        _layout = string.Empty;
    }

    private static List<RectInt32> Displays()
    {
        var areas = DisplayArea.FindAll();
        var displays = new List<RectInt32>(areas.Count);
        for (var index = 0; index < areas.Count; index++)
            displays.Add(areas[index].OuterBounds);
        return displays;
    }
}
