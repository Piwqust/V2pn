using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

/// <summary>
/// V2pn: the icon of a Windows program, for the list of running programs (AppRulesView). The small icon of the file's
/// first icon group becomes an Avalonia bitmap; null when the file has none or Windows refuses. Other systems: null.
/// </summary>
internal static class ProgramIcons
{
    public static Bitmap? Load(string? path)
    {
        if (!OperatingSystem.IsWindows() || path.IsNullOrEmpty())
        {
            return null;
        }
        var small = IntPtr.Zero;
        try
        {
            if (ExtractIconExW(path, 0, IntPtr.Zero, out small, 1) == 0 || small == IntPtr.Zero)
            {
                return null;
            }
            return FromIcon(small);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (small != IntPtr.Zero)
            {
                DestroyIcon(small);
            }
        }
    }

    private static Bitmap? FromIcon(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var info))
        {
            return null;
        }
        try
        {
            if (info.hbmColor == IntPtr.Zero || GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), out var bm) == 0
                || bm.bmWidth is <= 0 or > 256 || bm.bmHeight is <= 0 or > 256)
            {
                return null;
            }
            var (width, height) = (bm.bmWidth, bm.bmHeight);
            var pixels = Bits(info.hbmColor, width, height);
            if (pixels == null)
            {
                return null;
            }
            // An icon without an alpha channel carries its shape in the mask: white there is transparent.
            var hasAlpha = false;
            for (var i = 3; i < pixels.Length && !hasAlpha; i += 4)
            {
                hasAlpha = pixels[i] != 0;
            }
            if (!hasAlpha)
            {
                var mask = info.hbmMask == IntPtr.Zero ? null : Bits(info.hbmMask, width, height);
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i + 3] = mask == null || mask[i] == 0 ? (byte)255 : (byte)0;
                }
            }

            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using var frame = bitmap.Lock();
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(pixels, row * width * 4, frame.Address + row * frame.RowBytes, width * 4);
            }
            return bitmap;
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero)
            {
                DeleteObject(info.hbmColor);
            }
            if (info.hbmMask != IntPtr.Zero)
            {
                DeleteObject(info.hbmMask);
            }
        }
    }

    // The bitmap's pixels as 32-bit BGRA rows, top row first.
    private static byte[]? Bits(IntPtr bitmap, int width, int height)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = 40, // the header alone, without the room after it
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) == height ? pixels : null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
        // Room for the three masks or a short color table GetDIBits may write after the header.
        public long pad0, pad1, pad2, pad3;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr gdiObject, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
