using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GamesHub
{
    /// <summary>Native file/folder pickers (UI thread only). Return null when cancelled.</summary>
    internal static class NativeDialogs
    {
        public static string PickGameFile(IWin32Window owner)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Adicionar jogo",
                Filter = "Jogos e atalhos (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Todos os arquivos (*.*)|*.*",
                DereferenceLinks = false, // keep the .lnk itself instead of its target
                CheckFileExists = true,
                Multiselect = false,
            })
            {
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
            }
        }

        public static string PickImage(IWin32Window owner)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Escolher imagem",
                Filter = "Imagens (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.ico",
                CheckFileExists = true,
                Multiselect = false,
            })
            {
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
            }
        }

        /// <summary>Modern folder picker (IFileOpenDialog), falling back to FolderBrowserDialog.</summary>
        public static string PickFolder(IWin32Window owner, string initialDir, string title)
        {
            try
            {
                return PickFolderModern(owner, initialDir, title);
            }
            catch (COMException ex)
            {
                Log.Warn("IFileOpenDialog unavailable, using FolderBrowserDialog", ex);
            }
            using (var dlg = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true })
            {
                if (Directory.Exists(initialDir)) dlg.SelectedPath = initialDir;
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        private static string PickFolderModern(IWin32Window owner, string initialDir, string title)
        {
            const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
            const uint SIGDN_FILESYSPATH = 0x80058000;
            const int ERROR_CANCELLED = unchecked((int)0x800704C7);

            var dialog = (IFileDialog)new FileOpenDialogCo();
            try
            {
                dialog.GetOptions(out uint opts);
                dialog.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dialog.SetTitle(title);
                if (Directory.Exists(initialDir))
                {
                    Guid iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initialDir, IntPtr.Zero, ref iid, out IShellItem folder) == 0)
                    {
                        dialog.SetFolder(folder);
                        Marshal.ReleaseComObject(folder);
                    }
                }
                int hr = dialog.Show(owner?.Handle ?? IntPtr.Zero);
                if (hr == ERROR_CANCELLED) return null;
                Marshal.ThrowExceptionForHR(hr);
                dialog.GetResult(out IShellItem item);
                try
                {
                    item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                    return path;
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCo { }

        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attribs);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
