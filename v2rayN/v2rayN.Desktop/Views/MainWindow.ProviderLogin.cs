using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Styling;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

// V2pn: sign-in with a provider by code (PartnerLoginView, V2pnPartnerLogin), for everyone: in the add menu and in the
// walkthrough's servers step, where a person with an older partner build's marker gets that partner's sign-in first. It
// is one way a person comes from a partner (the other is its v2pn:// link): a confirmed sign-in adds the subscription as a confirmed
// v2pn://add does, updates it, records that the person came from that partner (V2pnEdition), opens its tab and selects
// its first server without connecting. The list of providers is asked for only when the person opens the sign-in.
public partial class MainWindow
{
    private IReadOnlyList<V2pnPartner> _loginOffers = [];
    private MenuItem? _menuProviderLogin;
    private bool _loginOpen;

    private void InitProviderLogin()
    {
        if (btnAdd.Flyout is MenuFlyout flyout)
        {
            _menuProviderLogin = new MenuItem
            {
                Classes = { "rich" },
                Icon = new PathIcon { Classes = { "mi" }, Data = this.TryFindResource("SemiIconUser", out var glyph) ? glyph as Geometry : null },
                Header = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Classes = { "mititle" }, Text = L.MenuProviderLogin },
                        new TextBlock { Classes = { "midesc" }, Text = L.MenuProviderLoginDesc },
                    },
                },
            };
            AutomationProperties.SetName(_menuProviderLogin, L.MenuProviderLogin);
            _menuProviderLogin.Click += (_, _) => OpenProviderLogin();
            flyout.Items.Insert(flyout.Items.IndexOf(menuAddSubscription) + 1, _menuProviderLogin);
        }
        // The walkthrough's row wears the marker partner's mark once its pack is here.
        V2pnPartners.Instance.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            if (OnboardingShown && _obStep == ObStep.Servers && MarkerLoginPartner()?.Id != _obLoginMark)
            {
                GoTo(ObStep.Servers);
            }
        });
    }

    // The partner whose mark the walkthrough's row showed last.
    private string? _obLoginMark;

    /// <summary>The partner of an older build's marker (V2pnEdition.Marker) when its pack is here and offers the sign-in; the walkthrough leads with it.</summary>
    private static V2pnPartner? MarkerLoginPartner() =>
        V2pnEdition.Instance.Marker is { } id && V2pnPartners.Instance.Get(id) is { } partner && V2pnPartnerLogin.Offers(partner) ? partner : null;

    /// <summary>Asks for the providers that offer the sign-in: the list of partners is downloaded here, when the person asks for it.</summary>
    private async Task RefreshLoginOffersAsync()
    {
        try
        {
            _loginOffers = await Task.Run(() => V2pnPartners.Instance.LoginOffersAsync());
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn sign-in offers", ex);
        }
    }

    /// <summary>
    /// The sign-in: straight to the code with one provider, a short list first with several. <paramref name="partnerId"/>
    /// goes straight to that provider (the walkthrough of a copy with its marker) when it offers the sign-in.
    /// </summary>
    internal async void OpenProviderLogin(string? partnerId = null)
    {
        if (_loginOpen)
        {
            return;
        }
        _loginOpen = true;
        try
        {
            await RefreshLoginOffersAsync();
            if (_loginOffers.Count == 0)
            {
                ShowToast(ToastKind.Warning, V2pnPartners.Instance.Index == null ? L.LoginNoList : L.LoginNoProviders);
                return;
            }
            var offers = _loginOffers.Where(p => p.Id == partnerId).ToList() is { Count: > 0 } only ? only : _loginOffers;
            var view = new PartnerLoginView(offers);
            view.SignedIn += (partner, answer) => _ = FinishProviderLoginAsync(view, partner, answer);
            // Over the walkthrough too: a dialog of its own sits above the walkthrough's scrim.
            if (DialogHost.IsDialogOpen(null))
            {
                DialogHost.Close(null);
            }
            await ShowRoomyDialogAsync(view);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn sign-in", ex);
        }
        finally
        {
            _loginOpen = false;
        }
    }

    /// <summary>
    /// What the provider handed over: added (or found), updated, its first server selected while V2pn is not connected,
    /// the view closed and a toast. A failure to save stays in the view with «New code».
    /// </summary>
    private async Task FinishProviderLoginAsync(PartnerLoginView view, V2pnPartner partner, V2pnLoginAnswer answer)
    {
        try
        {
            if (ViewModel == null || answer.Subscription is not { } url || await V2pnSubs.AddSubscriptionAsync(_config, url) is not (var item, var existed))
            {
                view.ShowFailure(L.LoginAddFailed);
                return;
            }
            Logging.SaveLog($"V2pn: sign-in with {partner.Id} {(existed ? "found" : "added")} subscription {item.Id}");
            // After the subscription is in: from now on the person came from this partner, and its tab opens (as soon as
            // its pack is here, which the refresh brings).
            V2pnEdition.Instance.RecordSignIn(partner.Id);
            V2pnEdition.Instance.Open(partner.Id);
            _ = Task.Run(() => V2pnPartners.Instance.RefreshAsync());
            await ViewModel.ProfilesViewModel.RefreshSubscriptions();
            if (!existed)
            {
                await UpdateAddedSubscriptionAsync(item.Id);
            }
            var first = (await ConfigDbQuery.ProfileModels(string.Empty, string.Empty) ?? []).FirstOrDefault(m => m.Subid == item.Id);
            // Selected only: connecting is the person's own step. While connected elsewhere the connection stays as it is.
            if (first != null && CurrentState() == ProtectionState.Unprotected)
            {
                await ViewModel.ProfilesViewModel.SetDefaultServer(first.IndexId);
            }
            if (DialogHost.IsDialogOpen(null))
            {
                DialogHost.Close(null);
            }
            // Where the subscription is, as after a pasted link.
            txtSearch.Text = string.Empty;
            if (_sidebar != null)
            {
                _sidebar.OpenedSub = null;
            }
            SelectTab(SidebarTab.Subscriptions);
            var saved = (await ConfigDbQuery.SubItems() ?? []).FirstOrDefault(s => s.Id == item.Id) ?? item;
            var name = ViewModels.SideSubRow.NameOf(saved);
            if (name.IsNullOrEmpty())
            {
                name = answer.Name ?? partner.Name;
            }
            ShowToast(existed ? ToastKind.Info : ToastKind.Success, string.Format(existed ? L.LoginExists : L.LoginAdded, name));
            if (OnboardingShown && _obStep == ObStep.Servers)
            {
                RefreshServersStatus();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn sign-in", ex);
            view.ShowFailure(L.LoginAddFailed);
        }
    }

    /// <summary>The walkthrough's row: the marker partner's mark in a copy with its marker, a person otherwise.</summary>
    private Button ProviderLoginRow()
    {
        var marker = MarkerLoginPartner();
        _obLoginMark = marker?.Id;
        Control icon = marker != null
            ? EditionLooks.Scene.Mark(marker, marker.Pack.Accent ?? "#3B82F6", "#FFFFFF", 32)
            : new Border { Classes = { "routeicon" }, Child = Icon("SemiIconUser", "i16") };
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Classes = { "routetitle" }, Text = L.ObProviderLogin },
                new TextBlock { Classes = { "routehint" }, Text = L.ObProviderLoginHint, TextWrapping = TextWrapping.Wrap },
            },
        };
        var chevron = Icon("SemiIconChevronRight", "i14");
        chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        Grid.SetColumn(chevron, 2);
        var row = new Button
        {
            Theme = this.FindResource("V2SideRow") as ControlTheme,
            Classes = { "routeoption" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12, Children = { icon, text, chevron } },
        };
        AutomationProperties.SetName(row, L.ObProviderLogin);
        row.Click += (_, _) => OpenProviderLogin(V2pnEdition.Instance.Marker);
        return row;
    }
}
