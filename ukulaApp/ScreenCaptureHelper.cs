using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace UkulaApp
{
    public static class ScreenCaptureHelper
    {
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int cx, int cy);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hDC, int x, int y, int cx, int cy, IntPtr hSrcDC, int x1, int y1, uint rop);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hDC, IntPtr hBitmap, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);

        const uint SRCCOPY = 0x00CC0020;
        const uint DIB_RGB_COLORS = 0;

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
            public byte[] bmiColors;
        }

        /// <summary>
        /// Ekranın belirtilen bölgesini yakalar. Tüm ekranı almaz, direkt o koordinatı keser.
        /// x, y, w, h → fiziksel piksel cinsinden (DPI ile çarpılmış)
        /// </summary>
        public static Task<SoftwareBitmap?> CaptureRegionAsync(int x, int y, int w, int h)
        {
            return Task.Run(() =>
            {
                if (w <= 0 || h <= 0) return null;

                IntPtr hDesktop = GetDesktopWindow();
                IntPtr hdcSrc = GetDC(hDesktop);
                IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
                IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, w, h);
                IntPtr hOld = SelectObject(hdcDest, hBitmap);

                // Ekrandan sadece seçilen bölgeyi al
                BitBlt(hdcDest, 0, 0, w, h, hdcSrc, x, y, SRCCOPY);
                SelectObject(hdcDest, hOld);

                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h; // top-down
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiColors = new byte[1024];

                byte[] pixels = new byte[w * h * 4];
                GetDIBits(hdcDest, hBitmap, 0, (uint)h, pixels, ref bmi, DIB_RGB_COLORS);

                DeleteObject(hBitmap);
                DeleteDC(hdcDest);
                ReleaseDC(hDesktop, hdcSrc);

                var softBitmap = new SoftwareBitmap(
                    BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
                softBitmap.CopyFromBuffer(pixels.AsBuffer());

                return softBitmap;
            });
        }
    }

    internal static class ByteArrayExtensions
    {
        public static Windows.Storage.Streams.IBuffer AsBuffer(this byte[] array)
        {
            return System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(array);
        }
    }
}
