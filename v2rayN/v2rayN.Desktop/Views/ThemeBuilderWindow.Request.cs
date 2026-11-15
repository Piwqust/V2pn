using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

// Signing the pack and asking for a certificate: the provider's key (made here or chosen), partner.json.sig through the same
// code as tools/partner.cs pack, and request.json through the same code as tools/partner.cs request.
public sealed partial class ThemeBuilderWindow
{
    private static readonly FontFamily Mono = new("Consolas, avares://V2pn/Assets/Fonts#Geist Mono, monospace");

    private string? _keyFile;
    private Border? _requestCardBorder;
    private readonly TextBox _domains = new() { PlaceholderText = "example.com, *.example.com" };
    private readonly TextBox _contact = new() { PlaceholderText = "@example_admin" };
    private readonly Dictionary<string, CheckBox> _grants = [];
    private readonly TextBlock _keyLabel = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _reviewLabel = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _requestCommand = ReadOnly();
    private readonly TextBox _signCommand = ReadOnly();
    private readonly TextBlock _requestStatus = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _signStatus = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private Border? _signCard;

    private Control _signPanel => _signCard ??= SignCard();

    private Control _requestCard => _requestCardBorder ?? (Control)new Border();

    private static TextBox ReadOnly() => new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = Mono,
        FontSize = 12,
    };

    // ------------------------------------------------------------------ signing

    private Border SignCard()
    {
        var copy = Action(L.BuilderCopy, () => _ = CopyAsync(_signCommand.Text));
        var sign = Action(L.BuilderSignNow, () => _ = SignAsync(), accent: true);
        var card = Card(L.BuilderSignTitle,
            Hint(L.BuilderSignHint),
            _signCommand,
            new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Children = { sign, copy } },
            _signStatus,
            Hint(L.BuilderPublish));
        card.IsVisible = false;
        return card;
    }

    /// <summary>After a save: the command that signs the pack, and the button that does the same here.</summary>
    private void ShowSign()
    {
        _signPanel.IsVisible = _packFolder != null;
        _signCommand.Text = $"dotnet run tools/partner.cs -- pack \"{_packFolder}\" --key \"{_keyFile ?? "provider-key.pem"}\"";
        _signStatus.IsVisible = false;
    }

    /// <summary>
    /// Signs the saved pack with the provider's key exactly as tools/partner.cs pack does: partner.json read from the folder,
    /// its "assets" filled from the folder, written, and signed into partner.json.sig.
    /// </summary>
    private async Task SignAsync()
    {
        if (_packFolder == null)
        {
            ShowLine(_signStatus, L.BuilderSaveFirst, bad: true);
            return;
        }
        if (_keyFile == null && !await PickKeyAsync())
        {
            return;
        }
        using var key = V2pnPartnerSigning.ReadPrivateKey(File.ReadAllText(_keyFile!), out var error);
        if (key == null)
        {
            ShowLine(_signStatus, string.Format(L.BuilderKeyBad, error), bad: true);
            return;
        }
        if (V2pnPackWriter.Read(_packFolder, out error) is not { } pack)
        {
            ShowLine(_signStatus, string.Format(L.BuilderSaveFailed, error), bad: true);
            return;
        }
        V2pnPackWriter.Write(_packFolder, pack, key);
        ShowSign();
        ShowLine(_signStatus, string.Format(L.BuilderSigned, _packFolder));
    }

    // ------------------------------------------------------------------ the request

    private Control RequestCard()
    {
        if (_requestCardBorder != null)
        {
            RefreshRequest();
            return _requestCardBorder;
        }
        foreach (var grant in V2pnCertRequests.Askable)
        {
            var box = new CheckBox { Content = L.BuilderGrantName(grant), IsChecked = grant != V2pnPartnerGrants.Lock };
            AutomationProperties.SetName(box, L.BuilderGrantName(grant));
            box.IsCheckedChanged += (_, _) => RefreshRequest();
            _grants[grant] = box;
        }
        AutomationProperties.SetName(_domains, L.BuilderDomains);
        AutomationProperties.SetName(_contact, L.BuilderContact);
        _domains.TextChanged += (_, _) => RefreshRequest();
        _contact.TextChanged += (_, _) => RefreshRequest();
        var grants = new StackPanel { Spacing = 4 };
        foreach (var box in _grants.Values)
        {
            grants.Children.Add(box);
        }
        var keyButtons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 6,
            Children = { Action(L.BuilderNewKey, () => _ = NewKeyAsync()), Action(L.BuilderPickKey, () => _ = PickKeyAsync()) },
        };
        var warning = new TextBlock { Text = L.BuilderKeyWarning, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        warning.Bind(TextBlock.ForegroundProperty, warning.GetResourceObservable("V2InkSoft"));
        var save = Action(L.BuilderSaveRequest, () => _ = SaveRequestAsync(), accent: true);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        _requestCardBorder = Card(L.BuilderRequest,
            Hint(L.BuilderRequestHint),
            Field(L.BuilderDomains, _domains),
            Field(L.BuilderGrants, grants),
            Field(L.BuilderContact, _contact),
            _reviewLabel,
            Field(L.BuilderKey, _keyLabel),
            keyButtons,
            warning,
            save,
            _requestStatus,
            Field(L.BuilderInTerminal, _requestCommand));
        RefreshRequest();
        return _requestCardBorder;
    }

    /// <summary>The domains the pack's own links point to, when the person has not typed any.</summary>
    private void LoadRequestDefaults()
    {
        if (!string.IsNullOrWhiteSpace(_domains.Text))
        {
            return;
        }
        var hosts = new[] { _draft.Root["links"]?["cabinet"], _draft.Root["links"]?["support"], _draft.Root["api"] }
            .Select(n => n?.ToString())
            .Select(u => Utils.TryUri(u ?? string.Empty)?.IdnHost)
            .Where(h => !string.IsNullOrEmpty(h) && h != "t.me")
            .Distinct()
            .ToList();
        if (hosts.Count > 0)
        {
            _domains.Text = string.Join(", ", hosts);
        }
    }

    private IReadOnlyList<string> Domains() =>
        (_domains.Text ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private IReadOnlyList<string> AskedGrants() => _grants.Where(g => g.Value.IsChecked == true).Select(g => g.Key).ToList();

    /// <summary>The reviewed files lying in the pack folder: their hashes go into the request.</summary>
    private Dictionary<string, string> ReviewFiles()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_packFolder == null)
        {
            return files;
        }
        foreach (var name in V2pnReviewedFiles.All)
        {
            var path = Path.Combine(_packFolder, name);
            if (File.Exists(path) && new FileInfo(path).Length <= V2pnReviewedFiles.MaxBytes)
            {
                files[name] = V2pnReviewedFiles.Hash(File.ReadAllBytes(path));
            }
        }
        return files;
    }

    private void RefreshRequest()
    {
        _keyLabel.Text = _keyFile ?? L.BuilderNoKey;
        var review = ReviewFiles();
        _reviewLabel.IsVisible = review.Count > 0;
        _reviewLabel.Text = string.Format(L.BuilderReviewFiles, string.Join(", ", review.Keys));
        var command = new StringBuilder("dotnet run tools/partner.cs -- request");
        command.Append($" --id {(_draft.Id.Length > 0 ? _draft.Id : "<id>")} --name \"{(_draft.Name.Length > 0 ? _draft.Name : "<name>")}\"");
        command.Append($" --domains {(Domains().Count > 0 ? string.Join(",", Domains()) : "<example.com>")}");
        command.Append($" --key \"{_keyFile ?? "provider-key.pem"}\" --grants {string.Join(",", AskedGrants())}");
        foreach (var name in review.Keys)
        {
            command.Append($" --review \"{Path.Combine(_packFolder!, name)}\"");
        }
        if (!string.IsNullOrWhiteSpace(_contact.Text))
        {
            command.Append($" --contact \"{_contact.Text.Trim()}\"");
        }
        command.Append(" --out request.json");
        _requestCommand.Text = command.ToString();
    }

    private async Task NewKeyAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.BuilderNewKey,
            SuggestedFileName = "provider-key.pem",
            DefaultExtension = "pem",
            ShowOverwritePrompt = false,
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }
        if (File.Exists(path))
        {
            ShowLine(_requestStatus, string.Format(L.BuilderKeyExists, path), bad: true);
            return;
        }
        var (pem, _) = V2pnPartnerSigning.NewKey();
        File.WriteAllText(path, pem);
        _keyFile = path;
        ShowLine(_requestStatus, string.Format(L.BuilderKeyCreated, path) + " " + L.BuilderKeyWarning);
        RefreshRequest();
        if (_signPanel.IsVisible)
        {
            ShowSign();
        }
    }

    private async Task<bool> PickKeyAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.BuilderPickKey,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PEM") { Patterns = ["*.pem", "*.key"] }, FilePickerFileTypes.All],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
        {
            return false;
        }
        using var key = V2pnPartnerSigning.ReadPrivateKey(File.ReadAllText(path), out var error);
        if (key == null)
        {
            ShowLine(_requestStatus, string.Format(L.BuilderKeyBad, error), bad: true);
            return false;
        }
        _keyFile = path;
        RefreshRequest();
        if (_signPanel.IsVisible)
        {
            ShowSign();
        }
        return true;
    }

    /// <summary>request.json through the code tools/partner.cs request uses: the same file either way.</summary>
    private async Task SaveRequestAsync()
    {
        if (_draft.Problems().Count > 0)
        {
            ShowLine(_requestStatus, L.BuilderFillFirst, bad: true);
            return;
        }
        if (_keyFile == null && !await PickKeyAsync())
        {
            return;
        }
        using var key = V2pnPartnerSigning.ReadPrivateKey(File.ReadAllText(_keyFile!), out var keyError);
        if (key == null)
        {
            ShowLine(_requestStatus, string.Format(L.BuilderKeyBad, keyError), bad: true);
            return;
        }
        byte[] request;
        try
        {
            request = V2pnCertRequests.Create(_draft.Id, _draft.Name, Domains(), AskedGrants(), ReviewFiles(), _contact.Text, key, DateOnly.FromDateTime(DateTime.UtcNow));
        }
        catch (ArgumentException ex)
        {
            ShowLine(_requestStatus, string.Format(L.BuilderRequestFailed, ex.Message), bad: true);
            return;
        }
        // V2PN_BUILDER_OUT: a folder for scripted checks, where the request is written without asking.
        string? path;
        if (Environment.GetEnvironmentVariable("V2PN_BUILDER_OUT") is { Length: > 0 } outDir)
        {
            Directory.CreateDirectory(outDir);
            path = Path.Combine(outDir, "request.json");
        }
        else
        {
            var start = _packFolder != null && Directory.GetParent(_packFolder) is { } parent ? await StorageProvider.TryGetFolderFromPathAsync(parent.FullName) : null;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.BuilderSaveRequest,
                SuggestedFileName = "request.json",
                DefaultExtension = "json",
                SuggestedStartLocation = start,
            });
            path = file?.TryGetLocalPath();
        }
        if (path == null)
        {
            return;
        }
        await File.WriteAllBytesAsync(path, request);
        ShowLine(_requestStatus, string.Format(L.BuilderRequestSaved, path));
    }

    private async Task CopyAsync(string? text)
    {
        if (text != null)
        {
            await Common.AvaUtils.SetClipboardData(this, text);
            ShowStatus(L.BuilderCopied);
        }
    }

    private static void ShowLine(TextBlock line, string text, bool bad = false)
    {
        line.IsVisible = true;
        line.Text = text;
        line.Bind(TextBlock.ForegroundProperty, line.GetResourceObservable(bad ? "V2Unsafe" : "V2PingGood"));
    }
}
