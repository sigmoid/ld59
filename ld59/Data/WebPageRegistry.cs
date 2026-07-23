using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Quartz.UI;

/// <summary>
/// Registry of browser pages that are built in code rather than authored as text files.
/// A factory receives the browser's content bounds and a navigate callback, and returns the
/// subtree to display. Registered URLs are resolved before <c>Content/www/</c>, so a code page
/// can shadow a text page of the same name.
/// </summary>
public static class WebPageRegistry
{
    private static readonly Dictionary<string, Func<Rectangle, Action<string>, UIElement>> _pages =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string url, Func<Rectangle, Action<string>, UIElement> factory)
    {
        if (string.IsNullOrEmpty(url)) throw new ArgumentException("Page url is required.", nameof(url));
        _pages[url] = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public static bool TryGet(string url, out Func<Rectangle, Action<string>, UIElement> factory)
        => _pages.TryGetValue(url ?? string.Empty, out factory);

    public static IEnumerable<string> Urls => _pages.Keys;
}
