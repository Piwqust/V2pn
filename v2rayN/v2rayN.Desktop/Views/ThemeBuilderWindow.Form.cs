using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

// The form of the theme builder: every field of the theme format and the pack's basics. A change of a field goes into the
// draft at once and the preview follows; a change of structure (template, colours, blocks, pictures, buttons) rebuilds the form.
public sealed partial class ThemeBuilderWindow
{
    private readonly List<(Button Button, Func<bool> IsOn)> _formMarks = [];
    private readonly ContentControl _templates = new();
    private readonly List<(string Id, TextBlock Note)> _actionNotes = [];

    private void RebuildForm()
    {
        var offset = _formScroll.Offset;
        _formMarks.Clear();
        _actionNotes.Clear();
        Detach(_signPanel);
        Detach(_requestCard);
        Detach(_templates);
        var body = new StackPanel { Margin = new Thickness(20, 16, 20, 28), Spacing = 14 };
        body.Children.Add(ProviderCard());
        body.Children.Add(ColoursCard());
        body.Children.Add(TemplateCard());
        body.Children.Add(FontCard());
        body.Children.Add(ShapeCard());
        body.Children.Add(BackgroundCard());
        body.Children.Add(BlocksCard());
        body.Children.Add(PicturesCard());
        body.Children.Add(ActionsCard());
        body.Children.Add(_signPanel);
        body.Children.Add(RequestCard());
        _formScroll.Content = body;
        RefreshFormMarks();
        if (_partner != null)
        {
            RefreshTemplates(ThemeKit.Of(_partner));
        }
        Dispatcher.UIThread.Post(() => _formScroll.Offset = offset, DispatcherPriority.Loaded);
    }

    private static void Detach(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
        }
    }

    private void RefreshFormMarks()
    {
        foreach (var (button, isOn) in _formMarks)
        {
            button.Classes.Set("accent", isOn());
        }
    }

    /// <summary>Buttons for a choice, the one in effect lit; picking one sets it and redraws.</summary>
    private WrapPanel Choice(IEnumerable<(string Id, string Label)> options, Func<string> current, Action<string> set, bool rebuild = false)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (id, label) in options)
        {
            var button = Action(label, () =>
            {
                set(id);
                if (rebuild)
                {
                    RebuildForm();
                }
                else
                {
                    RefreshFormMarks();
                }
                Changed();
            });
            _formMarks.Add((button, () => current() == id));
            panel.Children.Add(button);
        }
        return panel;
    }

    private static TextBlock Hint(string text) => new() { Text = text, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };

    private static TextBlock FieldLabel(string text) => new() { Text = text, FontSize = 12.5, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap };

    private static StackPanel Field(string label, Control control, TextBlock? hint = null)
    {
        var stack = new StackPanel { Spacing = 5, Children = { FieldLabel(label), control } };
        if (hint != null)
        {
            stack.Children.Add(hint);
        }
        return stack;
    }

    private static TextBlock Problem(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("V2Unsafe"));
        return block;
    }

    private static TextBlock Fine(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("V2PingGood"));
        return block;
    }

    // ------------------------------------------------------------------ provider

    private Control ProviderCard()
    {
        var name = new TextBox { Text = _draft.Name, PlaceholderText = "Kestrel" };
        AutomationProperties.SetName(name, L.BuilderName);
        var nameHint = Hint(L.BuilderNameHint);
        var id = new TextBox { Text = _draft.Id, PlaceholderText = "kestrel" };
        AutomationProperties.SetName(id, L.BuilderId);
        var idHint = Hint(L.BuilderIdHint);
        // A hint turns red while what is typed would not be taken.
        static void Mark(TextBlock hint, bool fine)
        {
            hint.Opacity = fine ? 0.65 : 1;
            if (fine)
            {
                hint.ClearValue(TextBlock.ForegroundProperty);
            }
            else
            {
                hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("V2Unsafe"));
            }
        }
        void Check()
        {
            Mark(nameHint, _draft.Name.Length == 0 || V2pnPartnerTrust.ValidName(_draft.Name) == _draft.Name.Trim());
            Mark(idHint, _draft.Id.Length == 0 || V2pnPartnerTrust.ValidId(_draft.Id));
        }
        name.TextChanged += (_, _) =>
        {
            _draft.Name = name.Text ?? string.Empty;
            Check();
            Changed();
        };
        id.TextChanged += (_, _) =>
        {
            _draft.Id = id.Text ?? string.Empty;
            Check();
            Changed();
        };
        Check();
        return Card(L.BuilderProvider, Field(L.BuilderName, name, nameHint), Field(L.BuilderId, id, idHint));
    }

    // ------------------------------------------------------------------ colours

    private Control ColoursCard()
    {
        var brand = _draft.Brand.ToList();
        if (brand.Count == 0)
        {
            brand.Add(_draft.Accent ?? V2pnThemePalette.FallbackBrand);
        }
        var rows = new StackPanel { Spacing = 8 };
        for (var i = 0; i < brand.Count; i++)
        {
            var at = i;
            var field = new ColourField(brand[i], $"{L.BuilderColours} {i + 1}");
            field.Changed += hex =>
            {
                var list = _draft.Brand.ToList();
                if (list.Count == 0)
                {
                    list.Add(hex);
                }
                list[Math.Min(at, list.Count - 1)] = hex;
                _draft.Brand = list;
                Changed();
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { field } };
            if (brand.Count > 1)
            {
                row.Children.Add(Action("✕", () =>
                {
                    var list = _draft.Brand.ToList();
                    list.RemoveAt(at);
                    _draft.Brand = list;
                    RebuildForm();
                    Changed();
                }).Also(b => AutomationProperties.SetName(b, $"{L.BuilderRemove} {at + 1}")));
            }
            rows.Children.Add(row);
        }
        if (brand.Count < V2pnThemeReader.MaxBrand)
        {
            rows.Children.Add(Action(L.BuilderAddColour, () =>
            {
                var list = _draft.Brand.ToList();
                var (h, s, l) = V2pnColor.Parse(list.LastOrDefault() ?? V2pnThemePalette.FallbackBrand).Hsl;
                list.Add(V2pnColor.FromHsl(h + 40, s, l).Hex);
                _draft.Brand = list;
                RebuildForm();
                Changed();
            }).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
        }
        var tones = Choice(V2pnThemeTones.All.Select(t => (t, L.BuilderToneName(t))), () => _draft.Tone, t => _draft.Tone = t);
        return Card(L.BuilderColours, Hint(L.BuilderBrand), rows, Field(L.BuilderTone, tones));
    }

    // ------------------------------------------------------------------ template, connect

    private Control TemplateCard()
    {
        var connect = Choice(V2pnThemeConnect.All.Select(c => (c, L.BuilderConnectName(c))), () => _draft.Connect, c => _draft.Connect = c);
        return Card(L.BuilderTemplate, _templates, Field(L.BuilderConnect, connect));
    }

    /// <summary>A thumbnail of each template in the theme's colours; the one in use lit.</summary>
    private void RefreshTemplates(V2pnTheme theme)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        foreach (var template in V2pnThemeTemplates.All)
        {
            var thumb = new Border
            {
                Width = 104,
                Height = 60,
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = ThemeKit.Preview(V2pnThemeReader.WithTemplate(theme, template), _dark),
            };
            var button = new Button
            {
                Padding = new Thickness(6),
                Content = new StackPanel
                {
                    Spacing = 5,
                    Children = { thumb, new TextBlock { Text = L.ThemeTemplate(template), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center } },
                },
            };
            button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
            button.Classes.Set("accent", _draft.Template == template);
            AutomationProperties.SetName(button, L.ThemeTemplate(template));
            var picked = template;
            button.Click += (_, _) =>
            {
                _draft.Template = picked;
                RebuildForm();
                Changed();
            };
            panel.Children.Add(button);
        }
        _templates.Content = panel;
    }

    // ------------------------------------------------------------------ font, shape

    private Control FontCard()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        var current = V2pnThemeFonts.Resolve(_draft.Font).Family;
        foreach (var font in V2pnThemeFonts.All)
        {
            var family = font.Family;
            var button = new Button
            {
                Padding = new Thickness(10, 6),
                Content = new TextBlock { Text = family, FontFamily = ThemeKit.Font(family), FontSize = 14 },
            };
            button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
            AutomationProperties.SetName(button, family);
            button.Click += (_, _) =>
            {
                _draft.Font = family;
                RebuildForm();
                Changed();
            };
            _formMarks.Add((button, () => V2pnThemeFonts.Resolve(_draft.Font).Family == family));
            panel.Children.Add(button);
        }
        var sample = new TextBlock
        {
            Text = $"{L.BuilderFontSample} · {L.Connect} · Германия 1 · 38 {L.SceneMs}",
            FontFamily = ThemeKit.Font(current),
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
        };
        return Card(L.BuilderFont, panel, sample);
    }

    private Control ShapeCard() => Card(L.BuilderShape,
        Field(L.BuilderCorners, Choice(V2pnThemeCorners.All.Select(c => (c, L.BuilderCornersName(c))), () => _draft.Corners, c => _draft.Corners = c)),
        Field(L.BuilderDensity, Choice(V2pnThemeDensity.All.Select(d => (d, L.BuilderDensityName(d))), () => _draft.Density, d => _draft.Density = d)));

    // ------------------------------------------------------------------ background

    private Control BackgroundCard()
    {
        if (!V2pnThemeTemplates.HasArea(_draft.Template))
        {
            return Card(L.BuilderBackground, Hint(L.BuilderNoArea));
        }
        var kinds = Choice(V2pnThemeBackgroundKinds.All.Select(k => (k, L.BuilderBackgroundName(k))), () => _draft.BackgroundKind, k =>
        {
            _draft.BackgroundKind = k;
            if (k == V2pnThemeBackgroundKinds.Gradient && _draft.BackgroundColors.Count < V2pnThemeReader.MinStops)
            {
                var first = V2pnColor.Parse(_draft.Brand.FirstOrDefault() ?? V2pnThemePalette.FallbackBrand);
                var (h, s, l) = first.Hsl;
                _draft.BackgroundColors = [V2pnColor.FromHsl(h, s, Math.Max(0.12, l - 0.25)).Hex, first.Hex];
            }
        }, rebuild: true);
        var rows = new List<Control> { kinds };
        if (_draft.BackgroundKind == V2pnThemeBackgroundKinds.Gradient)
        {
            var stops = _draft.BackgroundColors.ToList();
            var list = new StackPanel { Spacing = 8 };
            for (var i = 0; i < stops.Count; i++)
            {
                var at = i;
                var field = new ColourField(stops[i], $"{L.BuilderStops} {i + 1}");
                field.Changed += hex =>
                {
                    var now = _draft.BackgroundColors.ToList();
                    now[Math.Min(at, now.Count - 1)] = hex;
                    _draft.BackgroundColors = now;
                    Changed();
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { field } };
                if (stops.Count > V2pnThemeReader.MinStops)
                {
                    row.Children.Add(Action("✕", () =>
                    {
                        var now = _draft.BackgroundColors.ToList();
                        now.RemoveAt(at);
                        _draft.BackgroundColors = now;
                        RebuildForm();
                        Changed();
                    }));
                }
                list.Children.Add(row);
            }
            if (stops.Count < V2pnThemeReader.MaxStops)
            {
                list.Children.Add(Action(L.BuilderAddColour, () =>
                {
                    var now = _draft.BackgroundColors.ToList();
                    var (h, s, l) = V2pnColor.Parse(now.LastOrDefault() ?? V2pnThemePalette.FallbackBrand).Hsl;
                    now.Add(V2pnColor.FromHsl(h + 30, s, l).Hex);
                    _draft.BackgroundColors = now;
                    RebuildForm();
                    Changed();
                }).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
            }
            rows.Add(Field(L.BuilderStops, list));
        }
        else if (_draft.BackgroundKind == V2pnThemeBackgroundKinds.Image)
        {
            rows.Add(PictureRow(V2pnPackDraft.Pictures.Backdrop, allowed: true));
        }
        return Card(L.BuilderBackground, [.. rows]);
    }

    // ------------------------------------------------------------------ blocks

    private Control BlocksCard()
    {
        var supported = V2pnThemeTemplates.Blocks(_draft.Template);
        var all = _draft.Blocks.ToList();
        if (!all.Contains(V2pnThemeBlocks.Servers))
        {
            all.Add(V2pnThemeBlocks.Servers);
        }
        var shown = all.Where(supported.Contains).ToList();
        var list = new StackPanel { Spacing = 6 };
        for (var i = 0; i < shown.Count; i++)
        {
            var block = shown[i];
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = $"{i + 1}. {L.BuilderBlockName(block)}", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (i > 0)
            {
                buttons.Children.Add(Action("↑", () => MoveBlock(block, -1)).Also(b => AutomationProperties.SetName(b, $"{L.BuilderUp}: {L.BuilderBlockName(block)}")));
            }
            if (i < shown.Count - 1)
            {
                buttons.Children.Add(Action("↓", () => MoveBlock(block, +1)).Also(b => AutomationProperties.SetName(b, $"{L.BuilderDown}: {L.BuilderBlockName(block)}")));
            }
            if (block != V2pnThemeBlocks.Servers)
            {
                buttons.Children.Add(Action("✕", () =>
                {
                    _draft.Blocks = _draft.Blocks.Where(b => b != block).ToList();
                    RebuildForm();
                    Changed();
                }).Also(b => AutomationProperties.SetName(b, $"{L.BuilderRemove}: {L.BuilderBlockName(block)}")));
            }
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            list.Children.Add(row);
        }
        var missing = supported.Where(b => !shown.Contains(b)).ToList();
        var rows = new List<Control> { Hint(L.BuilderBlocksHint), list };
        if (missing.Count > 0)
        {
            var add = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
            add.Children.Add(new TextBlock { Text = L.BuilderAddBlock, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Opacity = 0.75 });
            foreach (var block in missing)
            {
                add.Children.Add(Action("+ " + L.BuilderBlockName(block), () =>
                {
                    var now = _draft.Blocks.ToList();
                    // In front of the server list, which fills what is left of the column.
                    var at = now.IndexOf(V2pnThemeBlocks.Servers);
                    now.Insert(at < 0 ? now.Count : at, block);
                    _draft.Blocks = now;
                    RebuildForm();
                    Changed();
                }));
            }
            rows.Add(add);
        }
        return Card(L.BuilderBlocks, [.. rows]);
    }

    /// <summary>Moves a block past its visible neighbour; blocks the template has no place for keep their spots.</summary>
    private void MoveBlock(string block, int step)
    {
        var supported = V2pnThemeTemplates.Blocks(_draft.Template);
        var all = _draft.Blocks.ToList();
        if (!all.Contains(V2pnThemeBlocks.Servers))
        {
            all.Add(V2pnThemeBlocks.Servers);
        }
        var from = all.IndexOf(block);
        var to = from + step;
        while (to >= 0 && to < all.Count && !supported.Contains(all[to]))
        {
            to += step;
        }
        if (from < 0 || to < 0 || to >= all.Count)
        {
            return;
        }
        (all[from], all[to]) = (all[to], all[from]);
        _draft.Blocks = all;
        RebuildForm();
        Changed();
    }

    // ------------------------------------------------------------------ pictures

    private Control PicturesCard()
    {
        var slots = V2pnThemeTemplates.ArtSlots(_draft.Template);
        var rows = new List<Control> { Hint(L.BuilderPicturesHint) };
        foreach (var place in new[] { V2pnPackDraft.Pictures.Logo, V2pnPackDraft.Pictures.LogoOnLight, V2pnPackDraft.Pictures.Mark })
        {
            rows.Add(PictureRow(place, allowed: true));
        }
        foreach (var place in new[] { V2pnPackDraft.Pictures.Hero, V2pnPackDraft.Pictures.Banner })
        {
            var allowed = slots.Contains(place);
            if (allowed || _draft.Picture(place) != null)
            {
                rows.Add(PictureRow(place, allowed));
            }
        }
        return Card(L.BuilderPictures, [.. rows]);
    }

    /// <summary>A picture place: what fits it, the picture in it, choose and remove, and at once whether it fits and why not.</summary>
    private Control PictureRow(string place, bool allowed)
    {
        var file = _draft.Picture(place);
        Control thumb;
        var path = file == null ? null : Path.Combine(_work, file);
        Bitmap? bitmap = null;
        try
        {
            bitmap = path != null && File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn theme builder", ex);
        }
        thumb = bitmap != null
            ? new Image { Source = bitmap, Stretch = Stretch.Uniform, Width = 72, Height = 44 }
            : new TextBlock { Text = L.BuilderPictureEmpty, FontSize = 11.5, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var box = new Border
        {
            Width = 80,
            Height = 52,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = thumb,
            // A logo is shown on the ground it is made for.
            Background = new SolidColorBrush(bitmap == null ? Color.FromArgb(0x14, 0x80, 0x80, 0x80)
                : place == V2pnPackDraft.Pictures.LogoOnLight ? Color.Parse("#F2F2F0")
                : place == V2pnPackDraft.Pictures.Logo ? Color.Parse("#1A1A1A")
                : Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
        };
        box.Bind(Border.BorderBrushProperty, box.GetResourceObservable("V2Border"));

        var pick = Action(L.BuilderPick, () => _ = PickPictureAsync(place));
        AutomationProperties.SetName(pick, $"{L.BuilderPick} {L.BuilderPictureName(place)}");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { pick } };
        if (file != null)
        {
            var clear = Action(L.BuilderRemove, () => ClearPicture(place));
            AutomationProperties.SetName(clear, $"{L.BuilderRemove} {L.BuilderPictureName(place)}");
            buttons.Children.Add(clear);
        }
        var text = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = L.BuilderPictureName(place), FontSize = 13, FontWeight = FontWeight.Medium },
                Hint(L.BuilderSlotSize(place)),
                buttons,
            },
        };
        if (!allowed)
        {
            text.Children.Add(Problem(string.Format(L.BuilderPictureNoPlace, L.ThemeTemplate(_draft.Template))));
        }
        else if (_pictureState.TryGetValue(place, out var state))
        {
            text.Children.Add(state.Ok ? Fine(state.Text) : Problem(state.Text));
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(box);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        box.VerticalAlignment = VerticalAlignment.Top;
        return grid;
    }

    // ------------------------------------------------------------------ buttons

    private Control ActionsCard()
    {
        var actions = _draft.Actions.ToList();
        var rows = new List<Control>();
        foreach (var action in actions)
        {
            rows.Add(ActionRow(action));
        }
        if (actions.Count < V2pnPartnerCatalog.MaxActions)
        {
            rows.Add(Action(L.BuilderAddAction, () =>
            {
                var now = _draft.Actions.Select(a => (JsonObject)a.DeepClone()).ToList();
                var n = 1;
                while (now.Any(a => a["id"]?.GetValue<string>() == $"b{n}"))
                {
                    n++;
                }
                now.Add(new JsonObject { ["id"] = $"b{n}", ["label"] = L.BuilderActionKind(V2pnActionKinds.ConnectBest), ["icon"] = "bolt", ["do"] = V2pnActionKinds.ConnectBest, ["strategy"] = V2pnStrategies.Ping });
                _draft.SetActions(now);
                RebuildForm();
                Changed();
            }).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
        }
        return Card(L.BuilderActions, [.. rows]);
    }

    private Control ActionRow(JsonObject action)
    {
        string? Get(string key) => action[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        var id = Get("id") ?? string.Empty;

        // The icon: V2pn's own glyphs only.
        var iconButton = new Button { Padding = new Thickness(8), MinWidth = 38 };
        iconButton.Bind(ThemeProperty, iconButton.GetResourceObservable("V2Button"));
        AutomationProperties.SetName(iconButton, L.BuilderNoIcon);
        void ShowIcon()
        {
            iconButton.Content = PartnerActionIcons.Of(Get("icon")) is { } glyph
                ? new PathIcon { Data = glyph, Width = 16, Height = 16 }
                : new TextBlock { Text = "—", FontSize = 13 };
        }
        ShowIcon();
        var icons = new WrapPanel { Orientation = Orientation.Horizontal, Width = 236, ItemSpacing = 6, LineSpacing = 6 };
        var flyout = new Flyout { Content = icons, Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var icon in V2pnActionIcons.All.Prepend(string.Empty))
        {
            var glyph = PartnerActionIcons.Of(icon);
            var choice = new Button
            {
                Padding = new Thickness(8),
                MinWidth = 36,
                Content = glyph != null ? new PathIcon { Data = glyph, Width = 16, Height = 16 } : new TextBlock { Text = "—" },
            };
            choice.Bind(ThemeProperty, choice.GetResourceObservable("V2Button"));
            AutomationProperties.SetName(choice, icon.Length == 0 ? L.BuilderNoIcon : icon);
            ToolTip.SetTip(choice, icon.Length == 0 ? L.BuilderNoIcon : icon);
            choice.Click += (_, _) =>
            {
                if (icon.Length == 0)
                {
                    action.Remove("icon");
                }
                else
                {
                    action["icon"] = icon;
                }
                ShowIcon();
                flyout.Hide();
                Changed();
            };
            icons.Children.Add(choice);
        }
        iconButton.Flyout = flyout;

        var label = new TextBox { Text = Get("label"), PlaceholderText = L.BuilderActionLabel, MaxLength = 24 };
        AutomationProperties.SetName(label, L.BuilderActionLabel);
        label.TextChanged += (_, _) =>
        {
            action["label"] = label.Text ?? string.Empty;
            Changed();
        };
        var remove = Action("✕", () =>
        {
            _draft.SetActions(_draft.Actions.Where(a => !ReferenceEquals(a, action)).Select(a => (JsonObject)a.DeepClone()).ToList());
            RebuildForm();
            Changed();
        });
        AutomationProperties.SetName(remove, $"{L.BuilderRemove}: {Get("label")}");

        var kind = new ComboBox { MinWidth = 170 };
        foreach (var k in V2pnActionKinds.All)
        {
            kind.Items.Add(new ComboBoxItem { Content = L.BuilderActionKind(k), Tag = k });
        }
        kind.SelectedIndex = Math.Max(0, V2pnActionKinds.All.ToList().IndexOf(Get("do") ?? V2pnActionKinds.ConnectBest));
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is not ComboBoxItem { Tag: string picked } || picked == Get("do"))
            {
                return;
            }
            action["do"] = picked;
            // Fields of the old action would only confuse the new one.
            foreach (var key in new[] { "strategy", "scope", "purpose", "server", "group", "url" })
            {
                action.Remove(key);
            }
            if (picked == V2pnActionKinds.ConnectRecommend)
            {
                action["purpose"] = V2pnPurposes.YouTube;
            }
            RebuildForm();
            Changed();
        };

        Control? extra = (Get("do") ?? string.Empty) switch
        {
            V2pnActionKinds.ConnectBest => Combo(V2pnStrategies.All.Select(s => (s, L.BuilderStrategy(s))), Get("strategy") ?? V2pnStrategies.Ping, v => action["strategy"] = v),
            V2pnActionKinds.ConnectRecommend => Combo(V2pnPurposes.All.Select(p => (p, V2pnPurposes.Label(p))), Get("purpose") ?? V2pnPurposes.YouTube, v => action["purpose"] = v),
            V2pnActionKinds.ConnectServer => TextField(Get("server"), L.BuilderActionServer, v => action["server"] = v),
            V2pnActionKinds.ConnectGroup => TextField(Get("group"), L.BuilderActionGroup, v => action["group"] = v),
            V2pnActionKinds.OpenLink => TextField(Get("url"), L.BuilderActionUrl, v => action["url"] = v),
            _ => null,
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        top.Children.Add(iconButton);
        Grid.SetColumn(label, 1);
        top.Children.Add(label);
        Grid.SetColumn(remove, 2);
        top.Children.Add(remove);
        var second = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Children = { kind } };
        if (extra != null)
        {
            second.Children.Add(extra);
        }
        var note = Problem(L.BuilderActionSkipped);
        note.IsVisible = false;
        _actionNotes.Add((id, note));
        var row = new StackPanel { Spacing = 6, Children = { top, second, note } };
        var frame = new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = row };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("V2Border"));
        return frame;
    }

    private ComboBox Combo(IEnumerable<(string Id, string Label)> options, string current, Action<string> set)
    {
        var combo = new ComboBox { MinWidth = 190 };
        foreach (var (id, label) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = id });
            if (id == current)
            {
                combo.SelectedIndex = combo.Items.Count - 1;
            }
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string picked })
            {
                set(picked);
                Changed();
            }
        };
        return combo;
    }

    private TextBox TextField(string? value, string watermark, Action<string> set)
    {
        var box = new TextBox { Text = value, PlaceholderText = watermark, MinWidth = 220 };
        AutomationProperties.SetName(box, watermark);
        box.TextChanged += (_, _) =>
        {
            set((box.Text ?? string.Empty).Trim());
            Changed();
        };
        return box;
    }

    /// <summary>A button V2pn would leave out (a field missing, an API it lacks, a link off its domains) says so under it.</summary>
    private void RefreshActionNotes()
    {
        var kept = _partner?.Pack.ActionList.Select(a => a.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var (id, note) in _actionNotes)
        {
            note.IsVisible = _partner != null && !kept.Contains(id);
        }
    }
}
