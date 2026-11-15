using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The theme builder: a provider assembles its theme (V2pnTheme) from V2pn's parts in a form and sees it at once, drawn by
/// the real theme look (ThemeScene) on made-up data, mini to wide, light and dark, with what V2pn changed for contrast and
/// what it left out. It opens and saves a pack folder (partner.json with its assets, and the pictures), signs it with the
/// provider's key through the code tools/partner.cs uses, writes the certificate request, and can dress this running V2pn
/// in the unsigned theme as a marked preview. Developer mode only; no certificate needed.
/// </summary>
public sealed partial class ThemeBuilderWindow : Window
{
    private enum PreviewSize { Mini, Medium, Wide }

    private readonly MainWindow? _main;
    private readonly IEditionActions _actions = new PreviewActions();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, IsVisible = false };
    private readonly ScrollViewer _formScroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly Border _frame = new() { ClipToBounds = true, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
    private readonly StackPanel _notes = new() { Spacing = 4 };
    private readonly Button _apply;
    private readonly Button _unapply;
    private readonly List<(Button Button, Func<bool> IsOn)> _marks = [];
    private readonly DispatcherTimer _debounce;

    private V2pnPackDraft _draft = V2pnPackDraft.New();
    private string _work;
    private string? _packFolder;
    private V2pnPartner? _partner;
    private IReadOnlyList<string> _slotNotes = [];
    private PreviewSize _size = PreviewSize.Medium;
    private bool _dark;
    private readonly Dictionary<string, (bool Ok, string Text)> _pictureState = new(StringComparer.Ordinal);

    public ThemeBuilderWindow(MainWindow? main, string? folder = null)
    {
        _main = main;
        Title = L.BuilderTitle;
        Width = 1360;
        Height = 900;
        MinWidth = 900;
        MinHeight = 560;
        this.Bind(BackgroundProperty, this.GetResourceObservable("V2DialogBackground"));
        OffscreenMode.Attach(this);
        _dark = ActualThemeVariant != ThemeVariant.Light;
        _work = NewWorkFolder();
        _keyFile = Environment.GetEnvironmentVariable("V2PN_BUILDER_KEY") is { Length: > 0 } key && File.Exists(key) ? key : null;

        _apply = Action(L.BuilderApply, Apply, accent: true);
        _unapply = Action(L.BuilderRemovePreview, Unapply);
        _frame.Bind(Border.BorderBrushProperty, _frame.GetResourceObservable("V2Border"));

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        toolbar.Children.Add(Action(L.BuilderNew, New));
        toolbar.Children.Add(Action(L.BuilderOpen, () => _ = OpenAsync()));
        toolbar.Children.Add(Action(L.BuilderSave, () => _ = SaveAsync(ask: false)));
        toolbar.Children.Add(new Border { Width = 16 });
        toolbar.Children.Add(_apply);
        toolbar.Children.Add(_unapply);

        var head = new StackPanel
        {
            Margin = new Thickness(20, 14, 20, 10),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = L.BuilderTitle, FontSize = 18, FontWeight = FontWeight.SemiBold },
                toolbar,
                _status,
            },
        };

        var left = new Border { Child = _formScroll, BorderThickness = new Thickness(0, 0, 1, 0) };
        left.Bind(Border.BorderBrushProperty, left.GetResourceObservable("V2Border"));
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("480,*") };
        body.Children.Add(left);
        var right = PreviewPane();
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var root = new DockPanel();
        var headBorder = new Border { Child = head, BorderThickness = new Thickness(0, 0, 0, 1) };
        headBorder.Bind(Border.BorderBrushProperty, headBorder.GetResourceObservable("V2Border"));
        DockPanel.SetDock(headBorder, Dock.Top);
        root.Children.Add(headBorder);
        root.Children.Add(body);
        Content = root;

        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(220), DispatcherPriority.Background, (_, _) =>
        {
            _debounce!.Stop();
            RefreshPreview();
        });
        Editions.Changed += RefreshPreviewButtons;
        Closed += (_, _) =>
        {
            _debounce.Stop();
            Editions.Changed -= RefreshPreviewButtons;
            DeleteQuietly(_work);
        };

        if (folder != null && Directory.Exists(folder))
        {
            Open(folder);
        }
        else
        {
            RebuildForm();
            RefreshPreview();
        }
        RefreshPreviewButtons();
    }

    // ------------------------------------------------------------------ the preview pane

    private Control PreviewPane()
    {
        var sizes = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (size, label) in new[] { (PreviewSize.Mini, L.BuilderSizeMini), (PreviewSize.Medium, L.BuilderSizeMedium), (PreviewSize.Wide, L.BuilderSizeWide) })
        {
            sizes.Children.Add(Mark(Action(label, () => { _size = size; RefreshPreview(); }), () => _size == size));
        }
        sizes.Children.Add(new Border { Width = 12 });
        sizes.Children.Add(Mark(Action(L.BuilderLight, () => { _dark = false; RefreshPreview(); }), () => !_dark));
        sizes.Children.Add(Mark(Action(L.BuilderDark, () => { _dark = true; RefreshPreview(); }), () => _dark));

        var stage = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _frame, VerticalAlignment = VerticalAlignment.Top };
        var notesCard = Card(L.BuilderNotes, _notes);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(20, 14, 20, 16), RowSpacing = 10 };
        grid.Children.Add(sizes);
        var sample = new TextBlock { Text = L.BuilderSample, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(sample, 1);
        grid.Children.Add(sample);
        Grid.SetRow(stage, 2);
        grid.Children.Add(stage);
        var notesScroll = new ScrollViewer { Content = notesCard, MaxHeight = 190 };
        Grid.SetRow(notesScroll, 3);
        grid.Children.Add(notesScroll);
        return grid;
    }

    /// <summary>Waits for the person to stop typing or dragging, then draws the theme again.</summary>
    private void Changed()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// Writes the draft into the working folder and reads it back as V2pn reads a pack (strictly, with every fallback and
    /// every picture fitted to its slot), then draws it with the real theme look.
    /// </summary>
    private void RefreshPreview()
    {
        foreach (var (button, isOn) in _marks)
        {
            button.Classes.Set("accent", isOn());
        }
        try
        {
            V2pnPackWriter.Write(_work, (JsonObject)_draft.Root.DeepClone(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(string.Format(L.BuilderNoPreview, ex.Message), bad: true);
            return;
        }
        _partner = V2pnPartners.ReadUnsigned(_work, V2pnPartners.PreviewId, DateOnly.FromDateTime(DateTime.Today), out var slotNotes, out var error);
        _slotNotes = slotNotes;
        if (_partner == null)
        {
            _frame.Child = new TextBlock { Text = string.Format(L.BuilderNoPreview, error), Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap };
            return;
        }
        var theme = ThemeKit.Of(_partner);
        var accent = V2pnThemePalette.AwayFromState(V2pnColor.TryParse(theme.Brand.FirstOrDefault()) ?? V2pnColor.Parse(V2pnThemePalette.FallbackBrand)).Hex;
        var look = new SceneLook(_dark, accent, ThemeKit.Corners(theme.Corners), "normal", ThemeKit.FontId(theme.Font));
        var colours = V2pnThemePalette.Derive(theme, _dark, accent);
        // The scene's helpers read the look being built; the main window's own look is put back at once.
        var before = Scene.Current;
        Scene.Current = look;
        try
        {
            var scene = new ThemeScene(_partner, _actions, look, theme, colours);
            scene.Update(BuilderSample.Snapshot(_partner));
            var (width, height) = _size switch
            {
                PreviewSize.Mini => (380, 640),
                PreviewSize.Medium => (820, 700),
                _ => (1280, 800),
            };
            _frame.Width = width;
            _frame.Height = height;
            _frame.Background = new SolidColorBrush(Color.Parse(colours.Ground));
            _frame.Child = new ThemeVariantScope { RequestedThemeVariant = _dark ? ThemeVariant.Dark : ThemeVariant.Light, Child = scene };
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn theme builder", ex);
            _frame.Child = new TextBlock { Text = string.Format(L.BuilderNoPreview, ex.Message), Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap };
        }
        finally
        {
            Scene.Current = before;
        }
        RefreshNotes(theme);
        RefreshTemplates(theme);
        RefreshActionNotes();
    }

    private void RefreshNotes(V2pnTheme theme)
    {
        _notes.Children.Clear();
        var lines = theme.Notes.Concat(V2pnThemePalette.Describe(theme)).Concat(_slotNotes).ToList();
        if (lines.Count == 0)
        {
            _notes.Children.Add(new TextBlock { Text = L.BuilderNoNotes, FontSize = 12.5, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
        }
        foreach (var line in lines)
        {
            _notes.Children.Add(new TextBlock { Text = "• " + L.BuilderNote(line), FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        }
    }

    // ------------------------------------------------------------------ new, open, save

    private void New()
    {
        _draft = V2pnPackDraft.New();
        _packFolder = null;
        _pictureState.Clear();
        DeleteQuietly(_work);
        _work = NewWorkFolder();
        _signPanel.IsVisible = false;
        ShowStatus(null);
        RebuildForm();
        RefreshPreview();
    }

    private async Task OpenAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.BuilderOpenFolder, AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folder)
        {
            Open(folder);
        }
    }

    /// <summary>Opens a pack folder: partner.json and its pictures. Nothing in the folder changes until Save.</summary>
    private void Open(string folder)
    {
        // A folder with no pack in it yet: a new theme that saves there.
        if (!File.Exists(Path.Combine(folder, V2pnPackWriter.PackFile)) && V2pnPackWriter.Files(folder).Count == 0)
        {
            New();
            _packFolder = Path.GetFullPath(folder);
            ShowStatus(string.Format(L.BuilderNewIn, _packFolder));
            RefreshRequest();
            return;
        }
        if (V2pnPackWriter.Read(folder, out var error) is not { } root)
        {
            ShowStatus(string.Format(L.BuilderOpenFailed, error), bad: true);
            return;
        }
        _draft = new V2pnPackDraft(root);
        _packFolder = Path.GetFullPath(folder);
        _pictureState.Clear();
        DeleteQuietly(_work);
        _work = NewWorkFolder();
        foreach (var place in V2pnPackDraft.Pictures.All)
        {
            if (_draft.Picture(place) is not { } file)
            {
                continue;
            }
            var path = Path.Combine(folder, file);
            if (!File.Exists(path) || file != Path.GetFileName(file))
            {
                _pictureState[place] = (false, string.Format(L.BuilderPictureNotImage, file));
                continue;
            }
            // Kept in the pack as it is, even when it does not fit: the person sees why, and V2pn leaves it out.
            File.Copy(path, Path.Combine(_work, file), true);
            _pictureState[place] = Verdict(place, file, File.ReadAllBytes(path));
        }
        _signPanel.IsVisible = false;
        LoadRequestDefaults();
        ShowStatus(string.Format(L.BuilderOpened, _packFolder));
        RebuildForm();
        RefreshPreview();
    }

    /// <summary>
    /// Saves the pack into its folder (asking for one the first time): the pictures it names, then partner.json with "assets"
    /// filled in as tools/partner.cs pack fills them. An old partner.json.sig is removed: it would not match any more.
    /// </summary>
    private async Task SaveAsync(bool ask)
    {
        if (_draft.Problems().Count > 0)
        {
            ShowStatus(L.BuilderFillFirst, bad: true);
            return;
        }
        var folder = _packFolder;
        if (folder == null || ask)
        {
            var start = folder != null ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.BuilderSaveFolder, AllowMultiple = false, SuggestedStartLocation = start });
            folder = picked.FirstOrDefault()?.TryGetLocalPath();
            if (folder == null)
            {
                return;
            }
        }
        try
        {
            var removed = _draft.FitTemplate();
            Directory.CreateDirectory(folder);
            var previous = V2pnPackWriter.Read(folder, out _)?["assets"] as JsonArray;
            var named = _draft.PictureFiles.ToHashSet(StringComparer.Ordinal);
            foreach (var file in named)
            {
                var source = Path.Combine(_work, file);
                var target = Path.Combine(folder, file);
                if (File.Exists(source) && !SameFile(source, target))
                {
                    File.Copy(source, target, true);
                }
            }
            // Pictures the pack listed before and names no more were its own: they go, so they do not stay in "assets".
            foreach (var old in previous?.Select(a => a?["name"]?.GetValue<string>()).OfType<string>() ?? [])
            {
                if (V2pnPackWriter.IsPicture(old) && !named.Contains(old) && old == Path.GetFileName(old) && File.Exists(Path.Combine(folder, old)))
                {
                    File.Delete(Path.Combine(folder, old));
                }
            }
            V2pnPackWriter.Write(folder, _draft.Root, null);
            _packFolder = Path.GetFullPath(folder);
            var stray = V2pnPackWriter.Files(folder).Where(n => V2pnPackWriter.IsPicture(n) && !named.Contains(n)).ToList();
            var lines = new List<string> { string.Format(L.BuilderSaved, _packFolder) };
            if (removed.Count > 0)
            {
                lines.Add(string.Format(L.BuilderLeftOut, string.Join(", ", removed)));
            }
            if (stray.Count > 0)
            {
                lines.Add(string.Format(L.BuilderStray, string.Join(", ", stray)));
            }
            ShowStatus(string.Join(" ", lines), bad: stray.Count > 0);
            ShowSign();
            RebuildForm();
            RefreshPreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(string.Format(L.BuilderSaveFailed, ex.Message), bad: true);
        }
    }

    private static bool SameFile(string a, string b) =>
        File.Exists(b) && new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    // ------------------------------------------------------------------ the preview in V2pn

    /// <summary>Dresses this V2pn in the draft, unsigned and marked as a preview; it never becomes a partner on disk.</summary>
    private void Apply()
    {
        RefreshPreview();
        if (V2pnPartners.Instance.SetPreview(_work, out var error) == null)
        {
            ShowStatus(string.Format(L.BuilderNoPreview, error), bad: true);
            return;
        }
        Editions.OpenPreview();
        ShowStatus(string.Format(L.BuilderPreviewOn, _partner?.Name ?? _draft.Name) + " " + L.BuilderPreviewHint);
        RefreshPreviewButtons();
    }

    private void Unapply()
    {
        Editions.ClosePreview();
        ShowStatus(L.BuilderPreviewOff);
        RefreshPreviewButtons();
    }

    private void RefreshPreviewButtons()
    {
        var on = V2pnPartners.Instance.Get(V2pnPartners.PreviewId) != null;
        _apply.Content = on ? L.BuilderApplyAgain : L.BuilderApply;
        AutomationProperties.SetName(_apply, _apply.Content as string);
        _unapply.IsVisible = on;
    }

    // ------------------------------------------------------------------ pictures

    /// <summary>Whether <paramref name="content"/> may go into <paramref name="place"/>, in words.</summary>
    private static (bool Ok, string Text) Verdict(string place, string file, byte[] content)
    {
        var (problem, width, height) = V2pnPartnerSlots.Check(V2pnPackDraft.Pictures.Slot(place), content);
        return problem switch
        {
            V2pnPartnerSlots.Problem.None => (true, string.Format(L.BuilderPictureOk, file, width, height)),
            V2pnPartnerSlots.Problem.TooLarge => (false, string.Format(L.BuilderPictureTooLarge, file)),
            V2pnPartnerSlots.Problem.NotPicture => (false, string.Format(L.BuilderPictureNotImage, file)),
            _ => (false, string.Format(L.BuilderPictureWrongSize, file, width, height, L.BuilderSlotSize(place))),
        };
    }

    private async Task PickPictureAsync(string place)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = L.BuilderPictureName(place),
            FileTypeFilter = [new FilePickerFileType("PNG, JPEG, WebP") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            SetPicture(place, path);
        }
    }

    /// <summary>
    /// Takes the picture at <paramref name="path"/> into a place when it fits; a picture that does not is not taken, and the
    /// place says why at once.
    /// </summary>
    internal void SetPicture(string place, string path)
    {
        var file = Path.GetFileName(path);
        if (new FileInfo(path).Length > V2pnPartnerTrust.MaxAssetBytes)
        {
            _pictureState[place] = (false, string.Format(L.BuilderPictureTooLarge, file));
            RebuildForm();
            return;
        }
        var content = File.ReadAllBytes(path);
        var verdict = Verdict(place, file, content);
        if (!verdict.Ok)
        {
            _pictureState[place] = verdict;
            RebuildForm();
            return;
        }
        // A plain file name the pack can carry; another place's different picture of the same name keeps its own.
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var name = file.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && !file.StartsWith('.') ? file : place.ToLowerInvariant() + ext;
        var taken = V2pnPackDraft.Pictures.All.Where(p => p != place).Select(_draft.Picture).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (taken.Contains(name) && !SameFile(path, Path.Combine(_work, name)))
        {
            name = place.ToLowerInvariant() + ext;
        }
        var others = _draft.PictureFiles.Where(f => f != _draft.Picture(place)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!others.Contains(name) && others.Count >= V2pnPartnerTrust.MaxAssets)
        {
            _pictureState[place] = (false, L.BuilderPictureTooMany);
            RebuildForm();
            return;
        }
        var old = _draft.Picture(place);
        File.Copy(path, Path.Combine(_work, name), true);
        _draft.SetPicture(place, name);
        // The wide picture shows in its own block: a picture without the block would never be seen.
        if (place == V2pnPackDraft.Pictures.Banner && !_draft.Blocks.Contains(V2pnThemeBlocks.Art))
        {
            var blocks = _draft.Blocks.ToList();
            var at = blocks.IndexOf(V2pnThemeBlocks.Servers);
            blocks.Insert(at < 0 ? blocks.Count : at, V2pnThemeBlocks.Art);
            _draft.Blocks = blocks;
        }
        _pictureState[place] = Verdict(place, name, content);
        DropUnused(old);
        RebuildForm();
        Changed();
    }

    private void ClearPicture(string place)
    {
        var old = _draft.Picture(place);
        _draft.SetPicture(place, null);
        _pictureState.Remove(place);
        if (place == V2pnPackDraft.Pictures.Banner && _draft.Blocks.Contains(V2pnThemeBlocks.Art))
        {
            _draft.Blocks = _draft.Blocks.Where(b => b != V2pnThemeBlocks.Art).ToList();
        }
        DropUnused(old);
        RebuildForm();
        Changed();
    }

    /// <summary>A picture no place names any more leaves the working folder, so it does not count in "assets".</summary>
    private void DropUnused(string? file)
    {
        if (file != null && !_draft.PictureFiles.Contains(file))
        {
            try
            {
                File.Delete(Path.Combine(_work, file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // ------------------------------------------------------------------ building blocks

    private void ShowStatus(string? text, bool bad = false)
    {
        _status.IsVisible = text != null;
        _status.Text = text;
        if (bad)
        {
            _status.Bind(TextBlock.ForegroundProperty, _status.GetResourceObservable("V2Unsafe"));
        }
        else
        {
            _status.Bind(TextBlock.ForegroundProperty, _status.GetResourceObservable("V2InkSoft"));
        }
    }

    private static string NewWorkFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "v2pn-builder-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Border Card(string title, params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeight.SemiBold });
        foreach (var row in rows)
        {
            stack.Children.Add(row);
        }
        var card = new Border { Padding = new Thickness(16, 14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = stack };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("V2CardBackground"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("V2Border"));
        return card;
    }

    private static Button Action(string label, Action act, bool accent = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 6) };
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        if (accent)
        {
            button.Classes.Add("accent");
        }
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            try
            {
                act();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("V2pn theme builder", ex);
            }
        };
        return button;
    }

    /// <summary>A button that lights up while its option is the one in effect.</summary>
    private Button Mark(Button button, Func<bool> isOn)
    {
        _marks.Add((button, isOn));
        return button;
    }
}
