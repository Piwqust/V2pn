using Avalonia.Markup.Xaml.Styling;
using Semi.Avalonia;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Gives the Semi controls used by the v2rayN dialogs the softer corners of the V2pn shell.
/// Semi exposes every control's radius as a resource; overriding them in Application.Resources
/// (which wins over theme resources) changes all dialogs at once without touching their layouts.
/// </summary>
internal static class SemiRounding
{
    public static void Apply(Application app)
    {
        var semi = app.Styles.OfType<SemiTheme>().FirstOrDefault();
        if (semi == null)
        {
            return;
        }

        var found = new Dictionary<object, CornerRadius>();
        Collect(semi.Resources, found, 0);
        foreach (var (key, radius) in found)
        {
            // Keys V2pn sets itself (menus, toasts) keep their value.
            if (!app.Resources.TryGetResource(key, null, out _))
            {
                app.Resources[key] = new CornerRadius(
                    Soften(radius.TopLeft), Soften(radius.TopRight), Soften(radius.BottomRight), Soften(radius.BottomLeft));
            }
        }
    }

    // Semi uses 3 / 4 / 6 / 8 / 12; V2pn controls use 8-12, panels 18. Pills and square corners stay as they are.
    private static double Soften(double r) => r switch
    {
        <= 0 => 0,
        >= 100 => r,
        <= 4 => 8,
        <= 6 => 10,
        <= 8 => 12,
        _ => Math.Min(r + 4, 20),
    };

    private static void Collect(IResourceDictionary? dictionary, Dictionary<object, CornerRadius> found, int depth)
    {
        if (dictionary == null || depth > 6)
        {
            return;
        }
        foreach (var (key, value) in dictionary)
        {
            if (value is CornerRadius radius)
            {
                found.TryAdd(key, radius);
            }
        }
        foreach (var merged in dictionary.MergedDictionaries)
        {
            Collect(merged switch
            {
                ResourceInclude include => include.Loaded as IResourceDictionary,
                IResourceDictionary d => d,
                _ => null,
            }, found, depth + 1);
        }
    }
}
