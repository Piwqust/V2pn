using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Layout;
using DialogHostAvalonia;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Sign-in with a provider by code (V2pnPartnerLogin), in the window: with several providers a short list first, then the
/// provider's mark and name, the QR code of its bot link, six digits, «Open the bot», the term counting down and what is
/// happening. Asks every two seconds while it is open, and stops when it closes. When the person confirms in the bot,
/// <see cref="SignedIn"/> hands the subscription to the window, which adds it and closes this view.
/// </summary>
public sealed class PartnerLoginView : UserControl
{
    private const double QrSize = 188;
    private readonly IReadOnlyList<V2pnPartner> _partners;
    private readonly ContentControl _body = new();
    private readonly DispatcherTimer _clock;
    private CancellationTokenSource? _run;
    private V2pnPartner? _partner;
    private V2pnLoginSession? _session;

    // The code view's parts, kept to update them in place.
    private TextBlock? _term;
    private TextBlock? _state;
    private Control? _spinner;
    private Button? _newCode;

    /// <summary>Raised on the UI thread with the partner and its "done" answer; the window adds the subscription.</summary>
    public event Action<V2pnPartner, V2pnLoginAnswer>? SignedIn;

    public PartnerLoginView(IReadOnlyList<V2pnPartner> partners)
    {
        _partners = partners;
        Width = 460;
        Content = _body;
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        AttachedToVisualTree += (_, _) =>
        {
            if (_partners.Count == 1)
            {
                Start(_partners[0]);
            }
            else
            {
                ShowList();
            }
        };
        // Closing the view stops the questions; a code nobody confirms runs out at the provider.
        DetachedFromVisualTree += (_, _) => Stop();
    }

    private void Stop()
    {
        _run?.Cancel();
        _run = null;
        _clock.Stop();
    }

    #region The list

    private void ShowList()
    {
        Stop();
        _partner = null;
        var rows = new StackPanel { Spacing = 2 };
        foreach (var partner in _partners)
        {
            var row = new Button
            {
                Classes = { "routeoption" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new Grid
                {
                    ColumnDefinitions = new("Auto,*,Auto"),
                    ColumnSpacing = 12,
                    Children =
                    {
                        Mark(partner, 32),
                        Cell(new TextBlock { Classes = { "routetitle" }, Text = partner.Name, VerticalAlignment = VerticalAlignment.Center }, 1),
                        Cell(Glyph("SemiIconChevronRight", 14), 2),
                    },
                },
            };
            row.Bind(ThemeProperty, row.GetResourceObservable("V2SideRow"));
            AutomationProperties.SetName(row, partner.Name);
            var p = partner;
            row.Click += (_, _) => Start(p);
            rows.Children.Add(row);
        }
        var cancel = FooterButton(L.LoginCancel, accent: false);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogHost.Close(null);
        _body.Content = Frame(
            new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Classes = { "spagetitle" }, Text = L.LoginPickTitle, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Classes = { "muted" }, Text = L.LoginPickLead, TextWrapping = TextWrapping.Wrap },
                },
            },
            new Border { Classes = { "routegroup" }, Margin = new Thickness(-4, 6, -4, 0), Child = rows },
            Footer(cancel));
        Focus(rows.Children.OfType<Button>().FirstOrDefault());
    }

    #endregion The list

    #region The code

    private void Start(V2pnPartner partner)
    {
        Stop();
        _partner = partner;
        _session = null;
        var run = _run = new CancellationTokenSource();
        _body.Content = Frame(Header(partner), Busy(string.Format(L.LoginGetting, partner.Name)), Footer(CancelButton()));
        _ = RunAsync(partner, run);
    }

    private async Task RunAsync(V2pnPartner partner, CancellationTokenSource run)
    {
        try
        {
            var (code, problem) = await Task.Run(() => V2pnPartnerLogin.StartAsync(partner, run.Token), run.Token);
            if (run.IsCancellationRequested)
            {
                return;
            }
            if (code == null)
            {
                Logging.SaveLog($"V2pn: sign-in with {partner.Id}: login/start failed ({problem})");
                ShowEnd(partner, string.Format(problem == V2pnLoginProblem.NoAnswer ? L.LoginNoAnswer : L.LoginMalformed, partner.Name));
                return;
            }
            var session = _session = new V2pnLoginSession(partner, code);
            ShowCode(partner, session);
            var answer = await Task.Run(() => session.RunAsync(() => Dispatcher.UIThread.Post(() =>
            {
                if (_session == session)
                {
                    SetState(session.Unanswered ? string.Format(L.LoginSilent, partner.Name) : L.LoginWaiting, busy: true);
                }
            }), run.Token), run.Token);
            if (run.IsCancellationRequested || _session != session)
            {
                return;
            }
            _clock.Stop();
            Logging.SaveLog($"V2pn: sign-in with {partner.Id}: {answer.State} {answer.Problem}");
            switch (answer.State)
            {
                case V2pnLoginState.Done:
                    SetState(L.LoginAdding, busy: true);
                    SignedIn?.Invoke(partner, answer);
                    break;
                case V2pnLoginState.Expired:
                    ShowEnd(partner, L.LoginExpired);
                    break;
                default:
                    ShowEnd(partner, answer.Problem switch
                    {
                        V2pnLoginProblem.NoAnswer => string.Format(L.LoginNoAnswer, partner.Name),
                        V2pnLoginProblem.WrongDomain => string.Format(L.LoginWrongDomain, partner.Name),
                        _ => string.Format(L.LoginMalformed, partner.Name),
                    });
                    break;
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(PartnerLoginView), ex);
            ShowEnd(partner, string.Format(L.LoginMalformed, partner.Name));
        }
    }

    private void ShowCode(V2pnPartner partner, V2pnLoginSession session)
    {
        var code = session.Code;
        var parts = new List<Control> { Header(partner) };

        if (code.Url != null && Qr(code.Url) is { } qr)
        {
            var image = new Image { Source = qr, Width = QrSize, Height = QrSize, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
            AutomationProperties.SetName(image, string.Format(L.LoginQrName, partner.Name));
            parts.Add(new Border
            {
                // A QR code reads only dark on light, whatever the theme.
                Background = Brushes.White,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                Child = image,
            });
            parts.Add(Centered(new TextBlock { Classes = { "muted", "small" }, Text = L.LoginScan, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }));
        }
        else
        {
            parts.Add(Centered(new TextBlock { Classes = { "muted" }, Text = string.Format(L.LoginTypeCode, partner.Name), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) }));
        }

        // Six digits, large and spaced, in two groups of three; a screen reader hears them one by one.
        var digits = new SelectableTextBlock
        {
            Text = $"{code.Code[..3]} {code.Code[3..]}",
            FontSize = 36,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 6,
            FontFeatures = [FontFeature.Parse("+tnum")],
        };
        // A text block reads out its own text ("735 570", a number); the frame around it reads the digits one by one.
        AutomationProperties.SetAccessibilityView(digits, AccessibilityView.Raw);
        var spoken = new Border { Child = digits, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(spoken, string.Format(L.LoginCodeName, string.Join(' ', code.Code.ToCharArray())));
        AutomationProperties.SetAccessibilityView(spoken, AccessibilityView.Content);
        AutomationProperties.SetControlTypeOverride(spoken, AutomationControlType.Text);
        parts.Add(spoken);

        Button? open = null;
        if (code.Url != null)
        {
            open = FooterButton(L.LoginOpenBot, accent: true);
            open.HorizontalAlignment = HorizontalAlignment.Center;
            open.MinWidth = 200;
            var url = code.Url;
            open.Click += (_, _) => ProcUtils.ProcessStart(url);
            parts.Add(open);
        }

        _term = new TextBlock { Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        parts.Add(Centered(_term));
        parts.Add(StateLine());
        SetState(L.LoginWaiting, busy: true);

        _newCode = null;
        parts.Add(Footer(CancelButton(), _partners.Count > 1 ? OtherButton() : null));
        _body.Content = Frame([.. parts]);
        Tick();
        _clock.Start();
        Focus(open ?? FindCancel());
    }

    /// <summary>How the sign-in ended without a subscription: what happened, and «New code».</summary>
    private void ShowEnd(V2pnPartner partner, string text)
    {
        _clock.Stop();
        _session = null;
        _newCode = FooterButton(L.LoginNewCode, accent: true);
        _newCode.Click += (_, _) => Start(partner);
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) };
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Assertive);
        _body.Content = Frame(Header(partner), message, Footer(CancelButton(), _partners.Count > 1 ? OtherButton() : null, _newCode));
        Focus(_newCode);
    }

    /// <summary>The window could not add what the provider handed over: said here, with a way to try again.</summary>
    public void ShowFailure(string text)
    {
        if (_partner is { } partner)
        {
            ShowEnd(partner, text);
        }
    }

    private void Tick()
    {
        if (_session is not { } session || _partner is not { } partner || _term == null)
        {
            return;
        }
        var left = session.Left;
        _term.Text = left >= TimeSpan.FromMinutes(1)
            ? string.Format(L.LoginConfirmIn, partner.Name, (int)Math.Ceiling(left.TotalMinutes))
            : string.Format(L.LoginConfirmInSeconds, partner.Name, (int)Math.Ceiling(left.TotalSeconds));
    }

    private Control StateLine()
    {
        _spinner = new Avalonia.Controls.Shapes.Path { Classes = { "spinner" }, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        _state = new TextBlock { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        // A screen reader hears the state change by itself (waiting, confirmed).
        AutomationProperties.SetLiveSetting(_state, AutomationLiveSetting.Polite);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _spinner, _state },
        };
    }

    private void SetState(string text, bool busy)
    {
        if (_state == null)
        {
            return;
        }
        _state.Text = text;
        _spinner?.IsVisible = busy;
    }

    #endregion The code

    #region Pieces

    private static Control Frame(params Control[] parts)
    {
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(28, 22, 28, 20) };
        foreach (var part in parts)
        {
            stack.Children.Add(part);
        }
        return stack;
    }

    private static Control Header(V2pnPartner partner)
    {
        var title = new TextBlock { Classes = { "spagetitle" }, Text = string.Format(L.LoginTitle, partner.Name), TextWrapping = TextWrapping.Wrap };
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title, new TextBlock { Classes = { "srowhint" }, Text = L.LoginFrom, Margin = new Thickness(0) } },
        };
        return new Grid
        {
            ColumnDefinitions = new("Auto,*"),
            ColumnSpacing = 12,
            Children = { Mark(partner, 40), Cell(text, 1) },
        };
    }

    /// <summary>The partner's mark on its tile, or the first letter of its name in its colour.</summary>
    private static Control Mark(V2pnPartner partner, double size)
    {
        return Scene.Mark(partner, partner.Pack.Accent ?? "#3B82F6", "#FFFFFF", size);
    }

    private static Control Busy(string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Margin = new Thickness(0, 24, 0, 24),
        HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new Avalonia.Controls.Shapes.Path { Classes = { "spinner" }, Width = 16, Height = 16 },
            new TextBlock { Classes = { "muted" }, Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
        },
    };

    private Button CancelButton()
    {
        var cancel = FooterButton(L.LoginCancel, accent: false);
        // Esc presses it, as in V2pn's other dialogs.
        cancel.IsCancel = true;
        cancel.Tag = "cancel";
        cancel.Click += (_, _) =>
        {
            Stop();
            DialogHost.Close(null);
        };
        return cancel;
    }

    private Button OtherButton()
    {
        var other = FooterButton(L.LoginOtherProvider, accent: false);
        other.Click += (_, _) => ShowList();
        return other;
    }

    private Button? FindCancel() =>
        _body.Content is Panel panel ? panel.Children.OfType<Panel>().LastOrDefault()?.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, "cancel")) : null;

    private static Control Footer(params Button?[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var button in buttons.Where(b => b != null))
        {
            row.Children.Add(button!);
        }
        return row;
    }

    private static Button FooterButton(string text, bool accent)
    {
        var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (accent)
        {
            button.Classes.Add("accent");
        }
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static PathIcon Glyph(string key, double size)
    {
        var icon = new PathIcon { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(key));
        return icon;
    }

    private static Control Cell(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static Control Centered(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Center;
        return control;
    }

    private static void Focus(Control? control)
    {
        if (control != null)
        {
            Dispatcher.UIThread.Post(() => control.Focus(), DispatcherPriority.Background);
        }
    }

    private static Bitmap? Qr(string url)
    {
        try
        {
            return QRCodeUtils.GenQRCode(url) is { } png ? new Bitmap(new MemoryStream(png)) : null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(PartnerLoginView), ex);
            return null;
        }
    }

    #endregion Pieces
}
