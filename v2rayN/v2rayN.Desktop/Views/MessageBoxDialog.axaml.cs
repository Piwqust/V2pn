using Avalonia.Media.Transformation;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>V2pn: what a question is about, shown as a card between the question and its explanation.</summary>
public sealed record DialogCard(string Title, string? Subtitle = null, string Icon = "SemiIconLink");

/// <summary>V2pn: the kind of question, shown as the badge beside it.</summary>
public enum DialogTone
{
    Question,
    Info,
    Danger,
}

public partial class MessageBoxDialog : Window
{
    public MessageBoxDialog()
        : this(string.Empty, string.Empty)
    {
    }

    public MessageBoxDialog(string caption, string message, string? yes = null, bool danger = false, string? no = null,
        DialogTone? tone = null, string? icon = null, DialogCard? card = null)
    {
        InitializeComponent();
        Classes.Add("dialog");
        DialogChrome.Attach(this);
        OffscreenMode.Attach(this);

        if (Design.IsDesignMode)
        {
            caption = "Design Caption";
            message = "Design question?\n\nDesign explanation.";
        }

        Title = caption;
        // V2pn: the question reads as the headline and the rest as its quieter explanation. A choice dialog whose
        // text asks nothing (the question is its caption) shows the caption as the headline.
        var (head, body) = Split(message);
        if (head == null && no.IsNotEmpty() && caption.IsNotEmpty())
        {
            head = caption;
        }
        txtHead.Text = head;
        txtHead.IsVisible = head.IsNotEmpty();
        txtMessage.Text = body;
        txtMessage.IsVisible = body.IsNotEmpty();
        if (head == null)
        {
            // Nothing to headline: the whole text is the message and reads in ink.
            txtMessage.FontSize = 14;
            txtMessage.LineHeight = 22;
            txtMessage.Foreground = txtHead.Foreground;
        }

        var kind = tone ?? (danger ? DialogTone.Danger : DialogTone.Question);
        badge.Classes.Set("info", kind == DialogTone.Info);
        badge.Classes.Set("danger", kind == DialogTone.Danger);
        var iconKey = icon ?? kind switch
        {
            DialogTone.Danger => "SemiIconDeleteStroked",
            DialogTone.Info => "SemiIconInfoCircle",
            _ => "SemiIconHelpCircleStroked",
        };
        if (this.TryFindResource(iconKey, out var geometry) && geometry is Geometry g)
        {
            badgeIcon.Data = g;
        }

        if (card != null)
        {
            cardBox.IsVisible = true;
            cardTitle.Text = card.Title;
            cardSubtitle.Text = card.Subtitle;
            cardSubtitle.IsVisible = card.Subtitle.IsNotEmpty();
            if (this.TryFindResource(card.Icon, out var cardGeometry) && cardGeometry is Geometry cg)
            {
                cardIcon.Data = cg;
            }
        }

        // V2pn: the button says what happens ("Delete", "Quit"), not a generic "Confirm".
        if (yes.IsNotEmpty())
        {
            btnYes.Content = yes;
        }
        btnYes.Classes.Set("danger", danger);
        // A destructive answer is never the default: Enter or Space out of habit must not delete.
        if (danger)
        {
            btnYes.IsDefault = false;
        }

        // A named second answer is a choice, not a cancel: Esc and the close button answer neither (None).
        if (no.IsNotEmpty())
        {
            btnNo.Content = no;
            btnNo.IsCancel = false;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close(ButtonResult.None);
                }
            };
        }

        btnYes.Click += BtnYes_Click;
        btnNo.Click += BtnNo_Click;

        // The content settles in with a short rise, so the dialog arrives rather than blinks in.
        root.Opacity = 0;
        root.RenderTransform = TransformOperations.Parse("translateY(6px)");
        Opened += MessageBoxDialog_Opened;

        CanMinimize = false;
    }

    /// <summary>
    /// Splits a question into its headline and explanation: the text before a blank line, or else the sentence
    /// that asks (ends with "?"), with the other sentences as the explanation. No question: no headline.
    /// </summary>
    internal static (string? Head, string Body) Split(string message)
    {
        var text = (message ?? string.Empty).Trim();
        var gap = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (gap > 0)
        {
            return (text[..gap].Trim(), text[(gap + 2)..].Trim());
        }
        var ask = text.IndexOf('?');
        if (ask < 0 || text.Contains('\n'))
        {
            return (null, text);
        }
        // The asking sentence starts after the last sentence end before its "?".
        var start = 0;
        for (var i = ask - 1; i > 0; i--)
        {
            if (text[i] == ' ' && text[i - 1] is '.' or '!' or ':')
            {
                start = i + 1;
                break;
            }
        }
        var head = text[start..(ask + 1)].Trim();
        var body = (text[..start] + " " + text[(ask + 1)..]).Trim();
        return (head, body);
    }

    private void MessageBoxDialog_Opened(object? sender, EventArgs e)
    {
        root.Opacity = 1;
        root.RenderTransform = TransformOperations.Parse("none");

        // Cancel takes focus but not IsDefault, so it is not painted as the primary button.
        if (btnYes.Classes.Contains("danger"))
        {
            btnNo.Focus(NavigationMethod.Tab);
        }
        else
        {
            btnYes.Focus();
        }
    }

    private void BtnYes_Click(object? sender, RoutedEventArgs e)
    {
        Close(ButtonResult.Yes);
    }

    private void BtnNo_Click(object? sender, RoutedEventArgs e)
    {
        Close(ButtonResult.No);
    }
}
