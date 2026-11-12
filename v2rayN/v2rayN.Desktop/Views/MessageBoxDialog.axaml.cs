using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class MessageBoxDialog : Window
{
    public MessageBoxDialog()
        : this(string.Empty, string.Empty)
    {
    }

    public MessageBoxDialog(string caption, string message, string? yes = null, bool danger = false, string? no = null)
    {
        InitializeComponent();
        Classes.Add("dialog");
        DialogChrome.Attach(this);

        if (Design.IsDesignMode)
        {
            caption = "Design Caption";
            message = "Design Message";
        }

        Title = caption;
        txtMessage.Text = message;
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

        Opened += MessageBoxDialog_Opened;

        CanMinimize = false;
    }

    private void MessageBoxDialog_Opened(object? sender, EventArgs e)
    {
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
