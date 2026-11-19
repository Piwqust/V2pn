using Avalonia.Platform.Storage;
using v2rayN.Desktop.Manager;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop.Common;

internal class UI
{
    private static readonly string caption = Global.AppName;

    public static async Task<ButtonResult> ShowYesNo(string msg, string? yes = null, bool danger = false, string? icon = null)
    {
        // V2pn: v2rayN asks its delete question with one generic sentence for servers, rules and subscriptions.
        if (msg == ResUI.RemoveServer)
        {
            (msg, yes, danger) = (L.RemoveConfirm, L.MenuDelete, true);
        }
        var owner = WindowDialog.TryGetOwnerWindow();
        var box = new MessageBoxDialog(caption, msg, yes, danger, icon: icon);
        var result = await box.ShowDialog<ButtonResult>(owner);
        return result == ButtonResult.Yes ? ButtonResult.Yes : ButtonResult.No;
    }

    /// <summary>
    /// V2pn: a question with two named answers where neither is a cancel. Yes or No; None when the dialog
    /// is closed or Esc is pressed without choosing.
    /// </summary>
    public static async Task<ButtonResult> ShowChoice(string caption, string msg, string yes, string no,
        DialogTone? tone = null, string? icon = null, DialogCard? card = null)
    {
        var owner = WindowDialog.TryGetOwnerWindow();
        var box = new MessageBoxDialog(caption, msg, yes, no: no, tone: tone, icon: icon, card: card);
        return await box.ShowDialog<ButtonResult>(owner);
    }

    public static async Task<string?> OpenFileDialog(FilePickerFileType? filter)
    {
        var sp = GetStorageProvider();
        if (sp is null)
        {
            return null;
        }

        // Start async operation to open the dialog.
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = filter is null ? [FilePickerFileTypes.All, FilePickerFileTypes.ImagePng] : [filter]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> SaveFileDialog(string filter)
    {
        var sp = GetStorageProvider();
        if (sp is null)
        {
            return null;
        }

        // Start async operation to open the dialog.
        var files = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
        });

        return files?.TryGetLocalPath();
    }

    public static async Task<string?> OpenFolderDialog(string title)
    {
        var sp = GetStorageProvider();
        if (sp is null)
        {
            return null;
        }
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static IStorageProvider? GetStorageProvider()
    {
        var owner = WindowDialog.TryGetOwnerWindow();
        var topLevel = TopLevel.GetTopLevel(owner);
        return topLevel?.StorageProvider;
    }
}
