namespace v2rayN.Desktop.Common;

public static class AppBuilderExtension
{
    private static readonly string DefaultFontFamilyName =
        Path.Combine(Global.AvaAssets, "Fonts#Inter");

    // V2pn: in a folder of its own. Avalonia loads every font of an embedded folder the first time one of
    // them is used, so beside Inter this 10 MB font was read into memory at every start.
    public static readonly string CjkFontFamilyName =
        Path.Combine(Global.AvaAssets, "Cjk#Noto Sans SC");

    public static AppBuilder WithFontByDefault(this AppBuilder appBuilder)
    {
        var fallbacks = new List<FontFallback>();

        // V2pn: Windows draws Chinese, Japanese and Korean with its own fonts (Microsoft YaHei and others), which the
        // system fallback finds; other systems may have none.
        if (!OperatingSystem.IsWindows())
        {
            fallbacks.Add(new FontFallback
            {
                FontFamily = new FontFamily(CjkFontFamilyName)
            });
        }

        if (OperatingSystem.IsWindows())
        {
            AddFontFallback(fallbacks, "Segoe UI Emoji");
            AddFontFallback(fallbacks, "Segoe UI Symbol");
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddFontFallback(fallbacks, "Apple Color Emoji");
            AddFontFallback(fallbacks, "Apple Symbols");
        }
        else if (OperatingSystem.IsLinux())
        {
            AddFontFallback(fallbacks, "Noto Color Emoji");
            AddFontFallback(fallbacks, "Noto Sans Symbols");
        }

        return appBuilder.With(new FontManagerOptions
        {
            DefaultFamilyName = DefaultFontFamilyName,
            FontFallbacks = fallbacks.ToArray()
        });
    }

    private static void AddFontFallback(List<FontFallback> fallbacks, string fontFamilyName)
    {
        fallbacks.Add(new FontFallback
        {
            FontFamily = new FontFamily(fontFamilyName)
        });
    }
}
