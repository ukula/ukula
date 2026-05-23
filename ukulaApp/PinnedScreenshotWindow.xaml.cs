using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;
using WinUIEx;


namespace UkulaApp
{
    public sealed partial class PinnedScreenshotWindow : Window
    {
        private readonly AppWindow _appWindow;
        private BitmapImage? _image = new();
        private SoftwareBitmap? _rawBitmap;

        private double _zoom = 1.0;
        private bool _isPanning = false;
        private Windows.Foundation.Point _lastPanPoint;
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;
        private readonly ScaleTransform _scaleT = new();
        private readonly TranslateTransform _translateT = new();
        private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
        private readonly string _savePath;
        private InMemoryRandomAccessStream? _imageStream;

        private Color _accentColor;
        private Color _accentLight1Color;
        private Color _accentLight2Color;
        private Color _accentLight3Color;
        private Color _accentDark1Color;
        private Color _accentDark2Color;
        private Color _accentDark3Color;

        [DllImport("user32.dll")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newLong);
        [DllImport("user32.dll")]
        static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate? _wndProcDelegate;
        private IntPtr _oldWndProc;
        private IntPtr _hWnd;

        public PinnedScreenshotWindow(SoftwareBitmap bitmap, Color accentColor, Color accentLight2, int offsetX, int offsetY, string savePath)
        {
            InitializeComponent();

            this.Closed += OnWindowClosed;
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
            this.Title = "Ukula Pin";
            var group = new TransformGroup();
            group.Children.Add(_scaleT);
            group.Children.Add(_translateT);
            PinnedImage.RenderTransform = group;

            _savePath = savePath;
            _hWnd = WindowNative.GetWindowHandle(this);

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonPressedBackgroundColor = Colors.Transparent;

            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "app.ico");
            if (File.Exists(iconPath))
                _appWindow.SetIcon(iconPath);

            this.SetIsAlwaysOnTop(true);
            this.SetIsResizable(true);

            RefreshThemeColors();

            int w = Math.Clamp(bitmap.PixelWidth / 2, 200, 400);
            int h = Math.Clamp(bitmap.PixelHeight / 2, 150, 350);

            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(10 + offsetX, 10 + offsetY, w, h));

            _wndProcDelegate = CustomWndProc;
            _oldWndProc = SetWindowLongPtr(_hWnd, -4, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            _accentColor = accentColor;
            RefreshThemeColors();
            SetupAcrylic();

            _ = SetImageAsync(bitmap);
        }

        private void SetupAcrylic()
        {
            if (!DesktopAcrylicController.IsSupported()) return;

            _configurationSource = new SystemBackdropConfiguration { IsInputActive = true };
            _acrylicController = new DesktopAcrylicController
            {
                TintColor = _accentColor,
                TintOpacity = 0.5f,
                LuminosityOpacity = 0.5f,
                FallbackColor = Color.FromArgb(255, 20, 20, 30)
            };

            var compositionTarget = WinRT.CastExtensions.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>(this);
            _acrylicController.AddSystemBackdropTarget(compositionTarget);
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        }

        private void RefreshThemeColors()
        {
            _accentColor = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            _accentLight1Color = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight1);
            _accentLight2Color = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight2);
            _accentLight3Color = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight3);
            _accentDark1Color = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark1);
            _accentDark2Color = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark2);
            _accentDark3Color = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark3);

            if (_acrylicController != null)
                _acrylicController.TintColor = _accentColor;

            ApplyTheme();
        }

        private void OnColorValuesChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            DispatcherQueue.TryEnqueue(RefreshThemeColors);
        }

        private void ApplyTheme()
        {
            SaveBtn.Background = ThemeHelper.Dark1Brush(_accentDark1Color);
            SaveBtn.BorderBrush = ThemeHelper.Light2Brush(_accentLight2Color);
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_rawBitmap == null) return;
            try
            {
                string fileName = $"ukula_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var storageFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(_savePath);
                var storageFile = await storageFolder.CreateFileAsync(fileName, Windows.Storage.CreationCollisionOption.GenerateUniqueName);
                var stream = await storageFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetSoftwareBitmap(_rawBitmap);
                await encoder.FlushAsync();
                stream.Dispose();

                SaveBtn.IsEnabled = false;
                await Task.Delay(500);
                SaveBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save error: {ex.Message}");
            }
        }

        private async void OnCopyClick(object sender, RoutedEventArgs e)
        {
            if (_rawBitmap == null) return;
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetSoftwareBitmap(_rawBitmap);
                await encoder.FlushAsync();
                stream.Seek(0);

                var dataPackage = new DataPackage();
                dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(dataPackage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy error: {ex.Message}");
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => this.Close();

        private void OnWheel(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(RootGrid).Properties.MouseWheelDelta;
            var oldZoom = _zoom;
            _zoom = Math.Clamp(_zoom + (delta > 0 ? 0.12 : -0.12), 0.2, 5.0);
            var pos = e.GetCurrentPoint(RootGrid).Position;
            var scaleRatio = _zoom / oldZoom;
            _translateT.X = pos.X - scaleRatio * (pos.X - _translateT.X);
            _translateT.Y = pos.Y - scaleRatio * (pos.Y - _translateT.Y);
            _scaleT.ScaleX = _zoom;
            _scaleT.ScaleY = _zoom;
            e.Handled = true;
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(RootGrid).Properties;
            if (props.IsLeftButtonPressed)
            {
                _isPanning = true;
                _lastPanPoint = e.GetCurrentPoint(RootGrid).Position;
                RootGrid.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPanning) return;
            var pos = e.GetCurrentPoint(RootGrid).Position;
            var dx = pos.X - _lastPanPoint.X;
            var dy = pos.Y - _lastPanPoint.Y;
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return;
            _translateT.X += dx;
            _translateT.Y += dy;
            _lastPanPoint = pos;
            e.Handled = true;
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                RootGrid.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        private async Task SetImageAsync(SoftwareBitmap bitmap)
        {
            try
            {
                _rawBitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                _imageStream?.Dispose();
                _imageStream = new InMemoryRandomAccessStream();

                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, _imageStream);
                encoder.SetSoftwareBitmap(_rawBitmap);
                await encoder.FlushAsync();
                _imageStream.Seek(0);

                _image = new BitmapImage();
                await _image.SetSourceAsync(_imageStream);
                PinnedImage.Source = _image;
            }
            finally
            {
                bitmap.Dispose();
            }
        }

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == 0x0024)
            {
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.x = 180;
                info.ptMinTrackSize.y = 120;
                Marshal.StructureToPtr(info, lParam, true);
                return IntPtr.Zero;
            }
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT2 { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO
        {
            public POINT2 ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            try
            {
                _uiSettings.ColorValuesChanged -= OnColorValuesChanged;

                if (_oldWndProc != IntPtr.Zero)
                    SetWindowLongPtr(_hWnd, -4, _oldWndProc);

                PinnedImage.Source = null;
                _image = null;

                _imageStream?.Dispose();
                _imageStream = null;

                _rawBitmap?.Dispose();
                _rawBitmap = null;

                _acrylicController?.Dispose();
                _acrylicController = null;
                _configurationSource = null;

                _wndProcDelegate = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dispose error: {ex.Message}");
            }
        }
    }
}