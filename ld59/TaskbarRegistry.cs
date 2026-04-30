using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public static class TaskbarRegistry
{
    public static event Action OnChanged;

    private class AppEntry
    {
        public Texture2D Icon;
        public List<Window> Windows = new();
    }

    private static readonly Dictionary<string, AppEntry> _apps = new();

    public static void Register(string appName, Texture2D icon, Window window)
    {
        if (!_apps.TryGetValue(appName, out var entry))
        {
            entry = new AppEntry { Icon = icon };
            _apps[appName] = entry;
        }
        entry.Windows.Add(window);
        window.OnWindowClosed += w => Unregister(appName, w);
        OnChanged?.Invoke();
    }

    private static void Unregister(string appName, Window window)
    {
        if (_apps.TryGetValue(appName, out var entry))
        {
            entry.Windows.Remove(window);
            if (entry.Windows.Count == 0)
                _apps.Remove(appName);
            OnChanged?.Invoke();
        }
    }

    public static List<(string Name, Texture2D Icon)> GetApps()
        => _apps.Select(kvp => (kvp.Key, kvp.Value.Icon)).ToList();

    public static void BringToFront(string appName)
    {
        if (_apps.TryGetValue(appName, out var entry))
        {
            foreach (var window in entry.Windows)
                Core.UISystem.WindowManager.SetFocusedWindow(window);
        }
    }
}
