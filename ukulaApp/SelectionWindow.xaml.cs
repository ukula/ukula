using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using WinUIEx;
using Windows.UI;

namespace UkulaApp
{
    public sealed partial class SelectionWindow : Window
    {
        public event Action<SoftwareBitmap?>? SelectionCompleted;

        private Windows.Foundation.Point _startPoint;
        private bool _isDrawing = false;
        private int _captureOffsetX = 0;
        private int _captureOffsetY = 0;

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;

        const int GWL_EXSTYLE = -20;
        const int WS_EX_LAYERED = 0x00080000;
        const uint LWA_ALPHA = 0x00000002;

        public SelectionWindow(Windows.UI.Color accent)
        {
            this.InitializeComponent();
            this.SetIsAlwaysOnTop(true);
            this.ExtendsContentIntoTitleBar = true;
            this.Title = "Ukula Selection";
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            // 1. Tüm bağlı ekranları kapsayan sanal masaüstü metriklerini alıyoruz
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // Kırpma ofsetlerini sanal ekranın sol üst köşe başlangıcına eşitliyoruz
            // Bu sayede ikinci ekran solda veya sağda olsa bile koordinat doğru hesaplanır
            _captureOffsetX = vx;
            _captureOffsetY = vy;

            // 2. FullScreenPresenter yerine OverlappedPresenter kullanarak çerçevesiz bir kaplama yapıyoruz
            // FullScreenPresenter tek ekrana hapsolur, Overlapped ise verdiğimiz devasa boyutlara esneyebilir.
            var presenter = Microsoft.UI.Windowing.OverlappedPresenter.Create();
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false); // Çerçeveyi ve üst barı tamamen uçurur
            appWindow.SetPresenter(presenter);

            // 3. Pencereyi sanal ekran boyutlarında konumlandır ve büyüt
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(vx, vy, vw, vh));

            // Arka planı şeffaflaştırma katmanı (Orijinal kodunla birebir aynı)
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            SetLayeredWindowAttributes(hwnd, 0, 50, LWA_ALPHA);

            SelectionCanvas.PointerPressed += OnPointerPressed;
            SelectionCanvas.PointerMoved += OnPointerMoved;
            SelectionCanvas.PointerReleased += OnPointerReleased;

            var stroke = new SolidColorBrush(accent);
            var fill = new SolidColorBrush(
                Color.FromArgb(80, accent.R, accent.G, accent.B));
            SelectionRect.Stroke = stroke;
            SelectionRect.Fill = fill;
        }
        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            SelectionCanvas.CapturePointer(e.Pointer);
            _isDrawing = true;
            _startPoint = e.GetCurrentPoint(SelectionCanvas).Position;
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _startPoint.X);
            Canvas.SetTop(SelectionRect, _startPoint.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
        }
        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDrawing) return;
            var cur = e.GetCurrentPoint(SelectionCanvas).Position;
            var x = Math.Min(_startPoint.X, cur.X);
            var y = Math.Min(_startPoint.Y, cur.Y);
            var w = Math.Abs(cur.X - _startPoint.X);
            var h = Math.Abs(cur.Y - _startPoint.Y);
            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;
        }
        private async void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;
            SelectionCanvas.ReleasePointerCapture(e.Pointer);

            var cur = e.GetCurrentPoint(SelectionCanvas).Position;
            var x = Math.Min(_startPoint.X, cur.X);
            var y = Math.Min(_startPoint.Y, cur.Y);
            var w = Math.Abs(cur.X - _startPoint.X);
            var h = Math.Abs(cur.Y - _startPoint.Y);
            if (w < 10 || h < 10)
            {
                SelectionCompleted?.Invoke(null);
                this.Close();
                return;
            }
            double dpi = GetDpiScale();
            int px = _captureOffsetX + (int)Math.Round(x * dpi);
            int py = _captureOffsetY + (int)Math.Round(y * dpi);
            int pw = (int)Math.Round(w * dpi);
            int ph = (int)Math.Round(h * dpi);

            this.Hide();
            await Task.Delay(80);

            var bitmap = await ScreenCaptureHelper.CaptureRegionAsync(px, py, pw, ph);
            SelectionCompleted?.Invoke(bitmap);
            this.Close();
        }
        private double GetDpiScale()
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                uint dpi = GetDpiForWindow(hwnd);
                return dpi / 96.0;
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return 1.0; 
            }
        }
    }
}
