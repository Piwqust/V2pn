using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>The partner's background where V2pn's map would be (V2pnThemePalette.Stage): a 135° gradient or a picture, under its scrim.</summary>
internal static class PartnerStage
{
    public static Control Build(V2pnPartner partner, V2pnPartnerStage stage)
    {
        var panel = new Panel { IsHitTestVisible = false };
        var stops = stage.Stops.Select(Color.Parse).ToList();
        var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        for (var i = 0; i < stops.Count; i++)
        {
            gradient.GradientStops.Add(new GradientStop(stops[i], stops.Count == 1 ? 0 : (double)i / (stops.Count - 1)));
        }
        panel.Children.Add(new Border { Background = gradient });
        if (stage.Kind == V2pnThemeBackgroundKinds.Image && Scene.Picture(partner, stage.Art) is { } art)
        {
            panel.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
        }
        else
        {
            // A soft light from the top right, as on the prototype; far from the plan's text at the bottom left.
            panel.Children.Add(new Border
            {
                Background = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.85, 0.1, RelativeUnit.Relative),
                    GradientOrigin = new RelativePoint(0.85, 0.1, RelativeUnit.Relative),
                    RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
                    RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(0x2E, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
                },
            });
        }
        if (stage.Scrim != null)
        {
            panel.Children.Add(new Border { Background = Scene.B(stage.Scrim) });
        }
        return panel;
    }

    /// <summary>The partner's logo made for the stage's ink, or its mark and name.</summary>
    public static Control Logo(V2pnPartner partner, V2pnPartnerStage stage, double height)
    {
        var onLight = stage.Ink != "#FFFFFF";
        if (Scene.Logo(partner, onLight) is { } logo)
        {
            var image = new Image { Source = logo, Height = height, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(image, partner.Name);
            return image;
        }
        var name = Scene.Text(partner.Name, Math.Round(height * 0.62), stage.Ink, FontWeight.SemiBold);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { Scene.Mark(partner, stage.Solid, stage.OnSolid, height), name },
        };
    }
}

/// <summary>
/// The partner's plan on its stage, at the bottom left over the status card: its logo, the days left in big type (or its
/// name when the provider sends no term or traffic), the plan and devices, the traffic as a bar, its offer, "Renew" and
/// "Support". Every text is the stage's ink, which reads at 4.5:1 at the stage's worst point.
/// </summary>
public sealed class PlanBlock : UserControl
{
    private readonly Action<string> _open;
    private readonly V2pnPartnerStage _stage;
    private readonly StackPanel _stack = new();
    private readonly ContentControl _logo = new();
    private readonly V2pnPartner _partner;
    private readonly TextBlock _line;
    private readonly TextBlock _days;
    private readonly TextBlock _daysWord;
    private readonly TextBlock _until;
    private readonly Grid _daysBox;
    private readonly TextBlock _name;
    private readonly StackPanel _barBox;
    // The days (or the name) and the traffic: one above the other in the big block, side by side in the compact one.
    private readonly Grid _main = new();
    private readonly Border _track;
    private readonly Border _bar;
    private readonly TextBlock _traffic;
    private readonly Border _note;
    private readonly TextBlock _noteText;
    private readonly Button _renew;
    private readonly TextBlock _renewText;
    private readonly Button _support;
    private double _fraction;
    private string? _renewUrl;
    private string? _supportUrl;
    private int? _level;
    private bool _hasDays;
    // Without a logo picture the logo is the partner's mark and name.
    private readonly bool _logoIsName;
    private bool _noteWanted;

    /// <summary>How much of the block fits: the roomy block of a wide stage, then ever more compact ones.</summary>
    public const int Big = 0, BigNoNote = 1, Compact = 2, NoLogo = 3, Bare = 4;

    public PlanBlock(V2pnPartner partner, V2pnPartnerStage stage, Action<string> open)
    {
        _partner = partner;
        _stage = stage;
        _open = open;
        _logoIsName = Scene.Logo(partner, stage.Ink != "#FFFFFF") == null;
        AutomationProperties.SetName(this, string.Format(L.PartnerCardTip, partner.Name));
        var ink = stage.Ink;
        var soft = stage.Soft;

        _line = Scene.Text(string.Empty, 12.5, soft);
        _days = Scene.Text(string.Empty, 64, ink, FontWeight.SemiBold);
        _days.LetterSpacing = -1.5;
        _days.VerticalAlignment = VerticalAlignment.Bottom;
        _daysWord = Scene.Text(string.Empty, 15, ink);
        _until = Scene.Text(string.Empty, 13, soft);
        var daysText = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6), Children = { _daysWord, _until } };
        _daysBox = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
        _daysBox.Children.Add(_days);
        Grid.SetColumn(daysText, 1);
        _daysBox.Children.Add(daysText);
        _name = Scene.Text(partner.Name, 15, ink, FontWeight.SemiBold);

        _bar = new Border { CornerRadius = new CornerRadius(3), Background = Scene.B(ink), HorizontalAlignment = HorizontalAlignment.Left };
        _track = new Border { Height = 5, CornerRadius = new CornerRadius(3), ClipToBounds = true, Background = Scene.B(stage.Ghost), Child = _bar };
        _track.SizeChanged += (_, _) => SizeBar();
        _traffic = Scene.Text(string.Empty, 12, soft);
        _barBox = new StackPanel { Spacing = 5, Children = { _track, _traffic } };

        _noteText = Scene.Text(string.Empty, 12.5, ink);
        _note = new Border
        {
            Background = Scene.B(stage.Ghost),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _noteText,
        };

        Button Chip(string icon, TextBlock label, string? fill, string fore, Func<string?> url)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { Scene.Icon(icon, fore), label } };
            var button = Scene.Button(content, fill, fore, 999, new Thickness(13, 0), height: 32);
            button.Click += (_, _) =>
            {
                if (url() is { Length: > 0 } target)
                {
                    _open(target);
                }
            };
            return button;
        }
        _renewText = Scene.Text(L.SceneRenew, 13, stage.OnSolid, FontWeight.SemiBold);
        _renew = Chip("V2pnIconCard", _renewText, stage.Solid, stage.OnSolid, () => _renewUrl);
        AutomationProperties.SetName(_renew, L.SceneRenew);
        _support = Chip("SemiIconComment", Scene.Text(L.SceneSupport, 13, ink, FontWeight.SemiBold), stage.Ghost, ink, () => _supportUrl);
        AutomationProperties.SetName(_support, L.SceneSupport);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { _renew, _support } };

        _stack.Children.Add(_logo);
        _stack.Children.Add(_line);
        _main.Children.Add(_daysBox);
        _main.Children.Add(_name);
        _main.Children.Add(_barBox);
        _stack.Children.Add(_main);
        _stack.Children.Add(_note);
        _stack.Children.Add(buttons);
        Scene.AddButtonStyles(this, ink);
        Content = _stack;
        Arrange(Big);
        Update(null, partner.Name);
    }

    /// <summary>
    /// <see cref="Big"/> for a wide stage, without the offer if need be; <see cref="Compact"/> for a narrower or lower one,
    /// then without the logo (the title bar has it) and without the offer, so the days and the buttons keep their place
    /// as long as they can.
    /// </summary>
    public void Arrange(int level)
    {
        if (_level == level)
        {
            return;
        }
        _level = level;
        var big = level <= BigNoNote;
        _stack.Spacing = big ? 12 : 9;
        MaxWidth = big ? 340 : 420;
        _logo.Content ??= PartnerStage.Logo(_partner, _stage, 30);
        if (_logo.Content is Control logo)
        {
            logo.Height = big ? 30 : 22;
        }
        _logo.IsVisible = level < NoLogo;
        _logo.Margin = new Thickness(0, 0, 0, big ? 10 : 4);
        _days.FontSize = big ? 64 : 40;
        _daysWord.FontSize = big ? 15 : 13;
        _until.FontSize = big ? 13 : 12;
        _name.FontSize = big ? 17 : 15;
        _note.IsVisible = _noteWanted && level != BigNoNote && level < Bare;
        ShowName();
        _barBox.MinWidth = big ? 0 : 170;
        _main.RowDefinitions = big ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        _main.ColumnDefinitions = big ? new ColumnDefinitions("*") : new ColumnDefinitions("Auto,*");
        _main.RowSpacing = big ? 12 : 0;
        _main.ColumnSpacing = big ? 0 : 18;
        Grid.SetRow(_barBox, big ? 1 : 0);
        Grid.SetColumn(_barBox, big ? 0 : 1);
        _barBox.VerticalAlignment = big ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _barBox.Margin = new Thickness(0, 0, 0, big ? 0 : 6);
    }

    // Where the days would be, the partner's name; not twice when the logo above already is its name.
    private void ShowName() => _name.IsVisible = !_hasDays && !(_logo.IsVisible && _logoIsName);

    private void SizeBar() => _bar.Width = Math.Max(_fraction > 0 ? 5 : 0, _track.Bounds.Width * _fraction);

    public void Update(SceneAccount? account, string partnerName)
    {
        _line.Text = string.Join(" · ", new[] { account?.Plan, account?.Devices }.Where(t => t.IsNotEmpty()));
        _line.IsVisible = _line.Text.Length > 0;

        // No term from the provider: its name stands where the days would.
        _hasDays = account?.DaysLeft != null;
        _daysBox.IsVisible = _hasDays;
        _name.Text = partnerName;
        ShowName();
        if (account?.DaysLeft is { } days)
        {
            _days.Text = days.ToString(CultureInfo.CurrentCulture);
            _daysWord.Text = L.DaysLeftWord(days);
            // The relative part ("in 23 days") says what the big number says.
            _until.Text = account.Expiry?.Split(" · ")[0] ?? string.Empty;
            _until.IsVisible = _until.Text.Length > 0;
        }

        _barBox.IsVisible = account?.Traffic != null;
        _traffic.Text = account?.Traffic ?? string.Empty;
        _track.IsVisible = account?.ShowBar == true;
        _fraction = account?.ShowBar == true ? Math.Clamp(account.Fraction, 0, 1) : 0;
        SizeBar();

        var note = account?.Note;
        _noteText.Text = note ?? string.Empty;
        _noteWanted = note.IsNotEmpty();
        _note.IsVisible = _noteWanted && _level != BigNoNote && _level < Bare;
        ToolTip.SetTip(_note, note);

        _renewUrl = account?.HasPlan == true ? account.RenewUrl : null;
        _supportUrl = account?.SupportUrl;
        _renew.IsVisible = _renewUrl != null;
        _renewText.Text = account?.Banner?.ButtonText ?? L.SceneRenew;
        _support.IsVisible = _supportUrl != null;
    }

    /// <summary>The block's height at <paramref name="width"/>, for deciding whether it fits on the stage.</summary>
    public double HeightAt(double width)
    {
        // Measured outside a layout pass, right after Arrange changed the parts: nothing of an earlier measure may be reused.
        foreach (var part in this.GetVisualDescendants().OfType<Layoutable>().Prepend(this))
        {
            part.InvalidateMeasure();
        }
        Measure(new Size(width, double.PositiveInfinity));
        return DesiredSize.Height;
    }
}
