using Avalonia.Automation;
using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// The partner's card at the top of the sidebar while its server is selected: its mark and name, the plan, the days and
/// the devices, the traffic as a bar, its offer or announcement, and "Renew" and "Support" in its colour made readable on
/// the card. Everything else in the sidebar stays V2pn's.
/// </summary>
public sealed class PartnerCard : UserControl
{
    private readonly Action<string> _open;
    private readonly TextBlock _line;
    private readonly Grid _barBox;
    private readonly Border _track;
    private readonly Border _bar;
    private readonly TextBlock _barText;
    private readonly TextBlock _note;
    private readonly Button _renew;
    private readonly Button _support;
    private double _fraction;
    private string? _renewUrl;
    private string? _supportUrl;

    /// <param name="accent">The partner's colour as V2pn uses it (Editions.PartnerAccent).</param>
    /// <param name="link">That colour made readable on the card, for its links.</param>
    public PartnerCard(V2pnPartner partner, string accent, string onAccent, string link, Action<string> open)
    {
        _open = open;
        AutomationProperties.SetName(this, string.Format(L.PartnerCardTip, partner.Name));

        var name = Scene.Text(partner.Name, 13.5, weight: FontWeight.SemiBold);
        _line = Scene.Text(string.Empty, 12);
        _line.Classes.Add("muted");
        var titles = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, _line } };
        var head = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
        head.Children.Add(Scene.Mark(partner, accent, onAccent, 28));
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);

        _bar = new Border { CornerRadius = new CornerRadius(3), Background = Scene.B(accent), HorizontalAlignment = HorizontalAlignment.Left };
        _track = new Border { Height = 5, CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = _bar };
        _track.Bind(Border.BackgroundProperty, _track.GetResourceObservable("V2Neutral"));
        _track.SizeChanged += (_, _) => SizeBar();
        _barText = Scene.Text(string.Empty, 11.5);
        _barText.Classes.Add("muted");
        _barBox = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = 5 };
        _barBox.Children.Add(_track);
        Grid.SetRow(_barText, 1);
        _barBox.Children.Add(_barText);

        _note = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        _note.Bind(TextBlock.ForegroundProperty, _note.GetResourceObservable("V2InkSoft"));

        Button Link(string text, Func<string?> url)
        {
            var button = Scene.Button(Scene.Text(text, 12.5, link, FontWeight.SemiBold), null, link, 4, new Thickness(0, 2));
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) =>
            {
                if (url() is { Length: > 0 } target)
                {
                    _open(target);
                }
            };
            return button;
        }
        _renew = Link(L.SceneRenew, () => _renewUrl);
        _support = Link(L.SceneSupport, () => _supportUrl);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { _renew, _support } };

        var stack = new StackPanel { Spacing = 10, Children = { head, _barBox, _note, links } };
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = stack };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("V2CardMutedBackground"));
        Scene.AddButtonStyles(this, link);
        Content = card;
        Update(null, partner.Name);
    }

    private void SizeBar() => _bar.Width = Math.Max(_fraction > 0 ? 5 : 0, _track.Bounds.Width * _fraction);

    public void Update(SceneAccount? account, string partnerName)
    {
        var parts = new List<string?> { account?.Plan };
        if (account?.DaysLeft is { } days)
        {
            parts.Add($"{days} {L.DaysWord(days)}");
        }
        parts.Add(account?.Devices);
        _line.Text = string.Join(" · ", parts.Where(p => p.IsNotEmpty()));
        _line.IsVisible = _line.Text.Length > 0;
        ToolTip.SetTip(_line, account?.Expiry);

        _barBox.IsVisible = account?.Traffic != null || account?.Expiry != null;
        _barText.Text = string.Join(" · ", new[] { account?.Traffic, account?.Expiry }.Where(t => t.IsNotEmpty()));
        _track.IsVisible = account?.ShowBar == true;
        _fraction = account?.ShowBar == true ? Math.Clamp(account.Fraction, 0, 1) : 0;
        // Traffic or term running out speaks in V2pn's state colours, never the partner's.
        _bar.Bind(Border.BackgroundProperty, _bar.GetResourceObservable(account?.Bad == true ? "V2Unsafe" : account?.Warn == true ? "V2PingMid" : "V2Accent"));
        SizeBar();

        var note = account?.Note;
        _note.Text = note ?? string.Empty;
        _note.IsVisible = note.IsNotEmpty();
        ToolTip.SetTip(_note, note);
        var incident = account?.Banner == null && account?.Incident != null;
        _note.Bind(TextBlock.ForegroundProperty, _note.GetResourceObservable(incident ? account!.IncidentSevere ? "V2Unsafe" : "V2PingMid" : "V2InkSoft"));

        _renewUrl = account?.HasPlan == true ? account.RenewUrl : null;
        _supportUrl = account?.SupportUrl;
        _renew.IsVisible = _renewUrl != null;
        if (_renew.Content is Border { Child: TextBlock renew })
        {
            renew.Text = account?.Banner?.ButtonText ?? L.SceneRenew;
        }
        _support.IsVisible = _supportUrl != null;
    }
}
