using Avalonia.LogicalTree;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Shared behaviour of the V2pn settings layouts: pages (StackPanel.spage) made of groups
/// (StackPanel.sgroup) made of rows (Border.srow).
/// <list type="bullet">
/// <item>Rows tagged win / nonwin / linux / mac are hidden on other platforms.</item>
/// <item>Search hides rows whose texts don't match, then empty groups and pages.</item>
/// </list>
/// Visibility goes through style classes (.hidden, .nomatch) so both rules combine without
/// fighting over IsVisible.
/// </summary>
internal static class SettingsKit
{
    public static void ApplyPlatform(Control root)
    {
        foreach (var row in Rows(root))
        {
            var visible = !(row.Classes.Contains("win") && !Utils.IsWindows())
                          && !(row.Classes.Contains("nonwin") && Utils.IsWindows())
                          && !(row.Classes.Contains("linux") && !Utils.IsLinux())
                          && !(row.Classes.Contains("mac") && !Utils.IsMacOS());
            row.Classes.Set("hidden", !visible);
        }
    }

    /// <summary>
    /// Converted v2rayN forms show and hide fields from code by toggling the label and the control;
    /// the row around them follows its title so no empty row is left behind.
    /// </summary>
    public static void FollowTitles(Control root)
    {
        foreach (var row in Rows(root))
        {
            var title = row.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("srowtitle"));
            if (title == null)
            {
                continue;
            }
            title.GetObservable(Visual.IsVisibleProperty).Subscribe(visible => row.Classes.Set("collapsed", !visible));
        }
    }

    /// <summary>
    /// Simple / advanced. Rows and pages tagged "adv" get "advhidden" unless advanced is on; a group
    /// whose rows are all hidden disappears with them, so simple pages show no empty headers.
    /// </summary>
    public static void ApplyAdvanced(Control root, bool show)
    {
        foreach (var row in Rows(root))
        {
            row.Classes.Set("advhidden", !show && row.Classes.Contains("adv"));
        }
        foreach (var group in root.GetLogicalDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("sgroup")))
        {
            var anyShown = Rows(group).Any(r => !r.Classes.Contains("advhidden") && !r.Classes.Contains("hidden"));
            group.Classes.Set("advhidden", !anyShown);
        }
        // Navigation entries of expert-only pages (nobody else drives their visibility).
        foreach (var list in root.GetLogicalDescendants().OfType<ListBox>())
        {
            foreach (var item in list.Items.OfType<ListBoxItem>().Where(i => i.Classes.Contains("adv")))
            {
                item.IsVisible = show;
            }
        }
    }

    /// <summary>Filters the pages; returns true when something matched (or the query is empty).</summary>
    public static bool Search(IEnumerable<Control> pages, string? query)
    {
        var q = query?.Trim() ?? string.Empty;
        var any = false;
        foreach (var page in pages)
        {
            var pageHit = false;
            foreach (var group in page.GetLogicalDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("sgroup")))
            {
                var groupHit = false;
                foreach (var row in Rows(group))
                {
                    var hit = q.Length == 0 || Texts(row).Any(t => t.Contains(q, StringComparison.CurrentCultureIgnoreCase));
                    row.Classes.Set("nomatch", !hit);
                    groupHit |= hit && !row.Classes.Contains("hidden") && !row.Classes.Contains("advhidden");
                }
                group.Classes.Set("nomatch", !groupHit);
                pageHit |= groupHit;
            }
            page.Classes.Set("nomatch", q.Length > 0 && !pageHit);
            any |= pageHit;
        }
        return q.Length == 0 || any;
    }

    private static IEnumerable<Border> Rows(Control root) =>
        root.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("srow"));

    private static IEnumerable<string> Texts(Control row) =>
        row.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty)
            .Where(t => t.Length > 0);
}
