using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>
    /// Extracts high-quality icons (up to 256px) through the shell's jumbo image list on a dedicated
    /// STA thread. Transparent padding is cropped (so a 48px-only icon is saved at 48px rather than
    /// upscaled), blank/tiny icons and the generic Windows exe/shortcut icons are rejected.
    /// </summary>
    public sealed class IconExtractor : IDisposable
    {
        public const int MinUsefulSide = 24;

        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Thread _thread;
        private HashSet<int> _genericIndices;

        public IconExtractor()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "GamesHub icon extractor" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        private void Run()
        {
            foreach (Action a in _queue.GetConsumingEnumerable()) a();
        }

        /// <summary>Tries each source in order; saves a PNG to outFile. True if an icon was saved.</summary>
        public Task<bool> ExtractAsync(IEnumerable<string> sources, string outFile)
        {
            var tcs = new TaskCompletionSource<bool>();
            var list = new List<string>(sources);
            try
            {
                _queue.Add(() =>
                {
                    try { tcs.SetResult(ExtractNow(list, outFile)); }
                    // Resilience boundary: STA worker-thread entry point; nothing is swallowed, the exception is handed to the awaiting caller through the task.
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            }
            catch (InvalidOperationException ex)   // disposed
            {
                tcs.SetException(ex);
            }
            return tcs.Task;
        }

        private bool ExtractNow(List<string> sources, string outFile)
        {
            foreach (string src in sources.Where(s => !string.IsNullOrEmpty(s) && File.Exists(s)))
            {
                try
                {
                    int index = SysIconIndex(src, 0, 0);
                    if (index >= 0 && GenericIndices().Contains(index)) continue;   // default Windows icon
                    using (Bitmap bmp = (index >= 0 ? LoadJumbo(index) : null) ?? LoadAssociated(src))
                    {
                        if (bmp == null) continue;
                        Rectangle content = ImageFiles.ContentBounds(bmp);
                        if (content.IsEmpty || Math.Max(content.Width, content.Height) < MinUsefulSide) continue;
                        using (Bitmap sq = ImageFiles.CropToSquare(bmp, content))
                            ImageFiles.WriteAtomic(outFile, tmp => sq.Save(tmp, System.Drawing.Imaging.ImageFormat.Png));
                        return true;
                    }
                }
                catch (Exception ex) when (ex is COMException || ex is ExternalException || ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Warn("Art: icon extraction failed for " + src, ex);
                }
            }
            return false;
        }

        // ------------------------------------------------------------ shell interop

        private static Bitmap LoadJumbo(int index)
        {
            Guid iid = IID_IImageList;
            if (SHGetImageList(SHIL_JUMBO, ref iid, out IImageList list) != 0 || list == null) return null;
            try
            {
                IntPtr hIcon = IntPtr.Zero;
                if (list.GetIcon(index, ILD_TRANSPARENT, ref hIcon) != 0 || hIcon == IntPtr.Zero) return null;
                try
                {
                    using (Icon ico = Icon.FromHandle(hIcon))
                        return ico.ToBitmap();
                }
                finally { DestroyIcon(hIcon); }
            }
            finally { Marshal.ReleaseComObject(list); }
        }

        private static Bitmap LoadAssociated(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".exe" && ext != ".ico") return null;
            using (Icon ico = ext == ".ico" ? new Icon(path, 256, 256) : Icon.ExtractAssociatedIcon(path))
                return ico?.ToBitmap();
        }

        /// <summary>System image-list indices of the generic exe / shortcut / unknown-file icons.</summary>
        private HashSet<int> GenericIndices()
        {
            if (_genericIndices != null) return _genericIndices;
            var set = new HashSet<int>();
            foreach (string dummy in new[] { "gameshub-dummy.exe", "gameshub-dummy.url", "gameshub-dummy.gameshubunknown" })
            {
                int i = SysIconIndex(dummy, FILE_ATTRIBUTE_NORMAL, SHGFI_USEFILEATTRIBUTES);
                if (i >= 0) set.Add(i);
            }
            return _genericIndices = set;
        }

        private static int SysIconIndex(string path, uint attrs, uint extraFlags)
        {
            var info = new SHFILEINFO();
            IntPtr r = SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf(info), SHGFI_SYSICONINDEX | extraFlags);
            return r == IntPtr.Zero ? -1 : info.iIcon;
        }

        private const int SHIL_JUMBO = 4;
        private const int ILD_TRANSPARENT = 1;
        private const uint SHGFI_SYSICONINDEX = 0x4000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private static readonly Guid IID_IImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>Only the vtable prefix up to GetIcon is declared (order matters).</summary>
        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }
}
