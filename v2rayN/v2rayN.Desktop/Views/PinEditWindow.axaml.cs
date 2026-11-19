using v2rayN.Desktop.Base;

namespace v2rayN.Desktop.Views;

/// <summary>The pin being edited; the window writes into it only on Save.</summary>
public sealed class PinEditModel(V2pnPin pin)
{
    public V2pnPin Pin { get; } = pin;
}

/// <summary>One entry of "Programs on their own server": the server or group, the programs, the sites.</summary>
public partial class PinEditWindow : WindowBase<PinEditModel>
{
    private string? _serverId;
    private string? _serverName;

    public PinEditWindow()
    {
        InitializeComponent();

        btnCancel.Click += (_, _) => Close(false);
        btnSave.Click += (_, _) => Save();
        btnChoose.Click += async (_, _) => await ChooseAsync();
        btnNewGroup.Click += async (_, _) => await NewGroupAsync();
        Loaded += async (_, _) => await FillAsync();
    }

    private async Task FillAsync()
    {
        if (ViewModel?.Pin is not { } pin)
        {
            return;
        }
        txtName.Text = pin.Name;
        txtPrograms.Text = string.Join(Environment.NewLine, pin.Programs ?? []);
        txtSites.Text = string.Join(Environment.NewLine, pin.Sites ?? []);
        _serverId = pin.ServerId;
        _serverName = pin.ServerName;
        await ShowServerAsync();
        // A new Claude entry has everything but the server.
        if (_serverId.IsNullOrEmpty() && V2pnPins.HasRules(pin))
        {
            btnChoose.Focus();
        }
        else
        {
            txtName.Focus();
        }
    }

    private async Task ShowServerAsync()
    {
        var server = await V2pnPins.FindServerAsync(new V2pnPin { ServerId = _serverId, ServerName = _serverName });
        if (server is not null)
        {
            SetServerText(Chosen(server), false);
            return;
        }
        SetServerText(_serverId.IsNullOrEmpty() && _serverName.IsNullOrEmpty()
            ? L.PinNoServer
            : string.Format(L.PinServerGone, _serverName), true);
    }

    public static string Chosen(ProfileItem server) =>
        string.Format(server.ConfigType.IsGroupType() ? L.PinChosenGroup : L.PinChosenServer, server.Remarks);

    private void SetServerText(string text, bool warn)
    {
        txtServer.Text = text;
        txtServer.Classes.Set("warn", warn);
    }

    private void SetServer(ProfileItem server)
    {
        _serverId = server.IndexId;
        _serverName = server.Remarks;
        SetServerText(Chosen(server), false);
        txtError.IsVisible = false;
    }

    private async Task ChooseAsync()
    {
        var picker = new ProfilesSelectViewModel();
        picker.SetConfigTypeFilter([EConfigType.Custom], exclude: true);
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(picker) != true)
        {
            return;
        }
        if (await picker.GetProfileItem() is { } server)
        {
            SetServer(server);
        }
    }

    private async Task NewGroupAsync()
    {
        var group = new ProfileItem
        {
            Subid = ConfigTextHandler.Config.SubIndexId,
            ConfigType = EConfigType.PolicyGroup,
            IsSub = false,
        };
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(new AddGroupServerViewModel(group)) != true
            || group.IndexId.IsNullOrEmpty())
        {
            return;
        }
        SetServer(group);
        // The server list in the main window shows the new group.
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow { ViewModel: { } main })
        {
            main.ProfilesViewModel.RefreshServersRequested.Publish();
        }
    }

    private void Save()
    {
        if (ViewModel?.Pin is not { } pin)
        {
            return;
        }
        var programs = Lines(txtPrograms.Text, splitCommas: true);
        var sites = Lines(txtSites.Text, splitCommas: false);
        if (_serverId.IsNullOrEmpty() && _serverName.IsNullOrEmpty())
        {
            ShowError(L.PinNeedServer);
            return;
        }
        if (!V2pnPins.HasRules(new V2pnPin { Programs = programs, Sites = sites }))
        {
            ShowError(L.PinNeedRules);
            return;
        }

        pin.Name = txtName.Text?.Trim() is { Length: > 0 } name ? name : null;
        pin.Programs = programs;
        pin.Sites = sites;
        pin.ServerId = _serverId;
        pin.ServerName = _serverName;
        if (pin.Id.IsNullOrEmpty())
        {
            pin.Id = Utils.GetGuid(false);
        }
        Close(true);
    }

    private void ShowError(string text)
    {
        txtError.Text = text;
        txtError.IsVisible = true;
    }

    // Program lists may also be written on one line with commas, as in the rule editor; a site
    // pattern (regexp:) can hold a comma, so sites split on lines only.
    private static List<string> Lines(string? text, bool splitCommas)
    {
        char[] separators = splitCommas ? ['\r', '\n', ','] : ['\r', '\n'];
        return (text ?? string.Empty)
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
