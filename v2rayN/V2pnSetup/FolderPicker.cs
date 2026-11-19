using System;
using System.Runtime.InteropServices;

namespace V2pnSetup
{
    /// <summary>Windows' own folder dialog (IFileOpenDialog with FOS_PICKFOLDERS), not the old tree of .NET Framework.</summary>
    internal static class FolderPicker
    {
        public static string Pick(IntPtr owner, string title, string start)
        {
            IFileOpenDialog dialog = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialogRcw();
                dialog.GetOptions(out var options);
                dialog.SetOptions(options | 0x20 /* FOS_PICKFOLDERS */ | 0x40 /* FOS_FORCEFILESYSTEM */ | 0x800 /* FOS_PATHMUSTEXIST */);
                dialog.SetTitle(title);
                if (!string.IsNullOrEmpty(start) && System.IO.Directory.Exists(start)
                    && SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out var folder) == 0)
                {
                    dialog.SetFolder(folder);
                }
                if (dialog.Show(owner) != 0)
                {
                    return null;
                }
                dialog.GetResult(out var item);
                item.GetDisplayName(0x80058000 /* SIGDN_FILESYSPATH */, out var path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Write("Folder dialog: " + ex.Message);
                return null;
            }
            finally
            {
                if (dialog != null)
                {
                    Marshal.ReleaseComObject(dialog);
                }
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw
        {
        }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName();
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel();
            void SetFileNameLabel();
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes();
            void Compare();
        }
    }
}
