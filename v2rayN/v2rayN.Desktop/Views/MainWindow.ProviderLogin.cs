using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Styling;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

// V2pn: sign-in with a provider by code (PartnerLoginView, V2pnPartnerLogin). Only a partner's build has it (V2pnEdition),
// with its own partner: first in the walkthrough's servers step, and in the add menu while the partner offers it. A
// confirmed sign-in adds the subscription as a confirmed v2pn://add does, updates it and selects its first server
// without connecting. Plain V2pn has neither the row nor the menu item.
public partial class MainWindow
{
    private IReadOnlyList<V2pnPartner> _loginOffers = [];
    private MenuItem? _menuProviderLogin;
    private bool _loginOpen;

    private void InitProviderLogin()
    {
        if (!V2pnEdition.IsPartner)
        {
            return;
        }
        if (btnAdd.Flyout is MenuFlyout flyout)
        {
            _menuProviderLogin = new MenuItem
            {
                Classes = { "rich" },
                IsVisible = false,
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
            // A partner may have started offering it since the last look.
            flyout.Opening += (_, _) => _ = RefreshLoginOffersAsync();
        }
        V2pnPartners.Instance.Changed += () => Dispatcher.UIThread.Post(() => _ = RefreshLoginOffersAsync());
        _ = RefreshLoginOffersAsync();
    }

    private async Task RefreshLoginOffersAsync()
    {
        try
        {
            var offers = await Task.Run(() => V2pnPartners.Instance.LoginOffersAsync());
            var changed = !offers.Select(p => p.Id).SequenceEqual(_loginOffers.Select(p => p.Id));
            _loginOffers = offers;
            _menuProviderLogin?.IsVisible = offers.Count > 0;
            // The walkthrough's servers step gains or loses its «I have a subscription from a provider».
            if (changed && OnboardingShown && _obStep == ObStep.Servers)
            {
                GoTo(ObStep.Servers);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn sign-in offers", ex);
        }
    }

    /// <summary>The sign-in: straight to the code with one provider, a short list first with several.</summary>
    internal async void OpenProviderLogin()
    {
        if (_loginOpen || !V2pnEdition.IsPartner)
        {
            return;
        }
        _loginOpen = true;
        try
        {
            if (_loginOffers.Count == 0)
            {
                await RefreshLoginOffersAsync();
            }
            if (_loginOffers.Count == 0)
            {
                ShowToast(ToastKind.Warning, V2pnPartners.Instance.Index == null ? L.LoginNoList : L.LoginNoProviders);
                return;
            }
            var view = new PartnerLoginView(_loginOffers);
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
            await ViewModel.ProfilesViewModel.RefreshSubscriptions();
            if (!existed)
            {
                await UpdateAddedSubscriptionAsync(item.Id);
            }
            var first = (await AppManager.Instance.ProfileModels(string.Empty, string.Empty) ?? []).FirstOrDefault(m => m.Subid == item.Id);
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
            lstTabs.SelectedIndex = (int)SidebarTab.Subscriptions;
            var saved = (await AppManager.Instance.SubItems() ?? []).FirstOrDefault(s => s.Id == item.Id) ?? item;
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

    /// <summary>The walkthrough's row: the provider's mark when there is one provider, a person otherwise.</summary>
    private Button ProviderLoginRow()
    {
        Control icon = _loginOffers.Count == 1
            ? EditionLooks.Scene.Mark(_loginOffers[0], _loginOffers[0].Pack.Accent ?? "#3B82F6", "#FFFFFF", 32)
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
        row.Click += (_, _) => OpenProviderLogin();
        return row;
    }
}
