using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// A subscription's banner in plain V2pn (V2pnSubDefaults.Banner): the provider's info or the reminder that the
/// subscription runs out. Colours are V2pn's own tones; the provider picks only which of them. The button opens a
/// link that passed SubPanelHeaders.ParseSecureLink (https or tg), or the provider's site for "Renew".
/// </summary>
public sealed class SubBannerView : Border
{
    public static readonly StyledProperty<SubBanner?> BannerProperty =
        AvaloniaProperty.Register<SubBannerView, SubBanner?>(nameof(Banner));

    private readonly PathIcon _icon = new() { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 18, FontSize = 13 };
    private readonly Button _button = new() { Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };

    public SubBannerView()
    {
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(12, 10);
        _button.Bind(ThemeProperty, _button.GetResourceObservable("V2Button"));
        _button.Click += (_, _) =>
        {
            // Checked again here: the value came from a subscription and may have sat in the database a while.
            if (Banner?.ButtonUrl is { } url && (SubPanelHeaders.ParseSecureLink(url) ?? SubPanelHeaders.ParseLink(url)) != null)
            {
                ProcUtils.ProcessStart(url);
            }
        };
        _text.Bind(TextBlock.ForegroundProperty, _text.GetResourceObservable("V2Ink"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        Grid.SetColumn(_text, 1);
        _text.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_button, 1);
        Grid.SetRow(_button, 1);
        _button.Margin = new Thickness(10, 8, 0, 0);
        grid.Children.Add(_icon);
        grid.Children.Add(_text);
        grid.Children.Add(_button);
        Child = grid;
        IsVisible = false;
    }

    public SubBanner? Banner
    {
        get => GetValue(BannerProperty);
        set => SetValue(BannerProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BannerProperty)
        {
            Show(Banner);
        }
    }

    private void Show(SubBanner? banner)
    {
        IsVisible = banner != null;
        if (banner == null)
        {
            return;
        }
        var (soft, strong, icon) = banner.Tone switch
        {
            SubPanelHeaders.ToneAttention => ("V2UnsafeSoft", "V2Unsafe", banner.IsExpiry ? "SemiIconClock" : "SemiIconAlertCircle"),
            SubPanelHeaders.ToneOk => ("V2SafeSoft", "V2Safe", "SemiIconTickCircle"),
            _ => ("V2ToastInfoSoft", "V2ToastInfo", "SemiIconInfoCircle"),
        };
        Bind(BackgroundProperty, this.GetResourceObservable(soft));
        _icon.Bind(TemplatedControl.ForegroundProperty, _icon.GetResourceObservable(strong));
        _icon.Bind(PathIcon.DataProperty, _icon.GetResourceObservable(icon));
        _text.Text = banner.Text;
        _button.Content = banner.ButtonText;
        _button.IsVisible = banner.ButtonText.IsNotEmpty() && banner.ButtonUrl.IsNotEmpty();
        ToolTip.SetTip(_button, banner.ButtonUrl);
        Avalonia.Automation.AutomationProperties.SetName(this, banner.Text);
    }
}
