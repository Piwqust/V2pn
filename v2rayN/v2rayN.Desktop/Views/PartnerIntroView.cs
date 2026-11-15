using Avalonia.Automation;
using Avalonia.Layout;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Shown once per partner, when its look first becomes available: what it is, its looks as small pictures, and the
/// choice to open it or stay in plain V2pn. Nothing changes until the person picks.
/// </summary>
public sealed class PartnerIntroView : UserControl
{
    public PartnerIntroView(V2pnPartner partner)
    {
        Width = 560;
        var light = ActualThemeVariant == ThemeVariant.Light
            || Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        Control mark = Scene.Picture(partner, light ? partner.Pack.LogoOnLight ?? partner.Pack.Logo : partner.Pack.Logo) is { } logo
            ? new Image { Source = logo, Height = 26, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left }
            : new TextBlock { Classes = { "spagetitle" }, Text = partner.Name };

        var looks = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, 4, 0, 0) };
        var accent = Color.Parse(partner.Pack.Accent ?? "#FF3D1F");
        foreach (var variant in partner.Pack.Variants)
        {
            var v = variant;
            var card = new Button
            {
                Classes = { "ghost" },
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 4, 4),
                Content = new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        new Border
                        {
                            Width = 92,
                            Height = 54,
                            ClipToBounds = true,
                            CornerRadius = new CornerRadius(9),
                            BorderThickness = new Thickness(variant == partner.Pack.DefaultVariant ? 2 : 1),
                            BorderBrush = new SolidColorBrush(variant == partner.Pack.DefaultVariant ? accent : Color.Parse("#40808080")),
                            Child = AppearanceView.LookPreview(variant, partner, accent),
                        },
                        new TextBlock { Text = L.EditionVariantShort(variant), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "muted" } },
                    },
                },
            };
            card.Bind(ThemeProperty, card.GetResourceObservable("V2Button"));
            AutomationProperties.SetName(card, string.Format(L.IntroOpenLook, L.EditionVariantShort(variant)));
            ToolTip.SetTip(card, string.Format(L.IntroOpenLook, L.EditionVariantShort(variant)));
            card.Click += (_, _) => Open(partner, v);
            looks.Children.Add(card);
        }

        var open = new Button { Content = string.Format(L.IntroOpen, partner.Name), Classes = { "accent" } };
        open.Bind(ThemeProperty, open.GetResourceObservable("V2Button"));
        open.Click += (_, _) => Open(partner, null);
        var stay = new Button { Content = L.IntroStay };
        stay.Bind(ThemeProperty, stay.GetResourceObservable("V2Button"));
        stay.Click += (_, _) => DialogHost.Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(28, 26, 28, 24),
            Spacing = 14,
            Children =
            {
                mark,
                new TextBlock { Classes = { "spagetitle" }, Text = string.Format(L.IntroTitle, partner.Name), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Classes = { "spagehint" }, Text = string.Format(L.IntroBody, partner.Name), TextWrapping = TextWrapping.Wrap },
                looks,
                new TextBlock { Classes = { "srowhint" }, Text = string.Format(L.IntroPrivacy, partner.Name), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0), Children = { open, stay } },
            },
        };
    }

    private static void Open(V2pnPartner partner, string? variant)
    {
        DialogHost.Close(null);
        Editions.Select(partner.Id);
        if (variant != null)
        {
            Editions.SetVariant(variant);
        }
    }
}
