using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ukulaApp;
using Windows.System;
using Windows.UI;
using WinRT.Interop;
using WinUIEx;
using static System.Net.Mime.MediaTypeNames;

namespace UkulaApp
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow m_AppWindow;
        private SelectionWindow? _selectionWindow;
        private AppSettings _settings;
        private bool _isInitialized = false;
        private uint _pendingMods = 0;
        private uint _pendingVk = 0;
        private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate? _wndProcDelegate;
        private IntPtr _oldWndProc;
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;
        private bool _isExiting = false;
        private Windows.UI.Color _accentColor;
        public static MainWindow? Instance;
        private bool _recordingHotkey = false;
        private bool _recordingScreenshotHotkey = false;
        private bool _recordingToggleWindowHotkey = false;
        private Windows.UI.Color _accentLight2Color;
        private int _pinnedWindowCount = 0;
        private bool _windowHidden;
        
        [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newLong);
        [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        public MainWindow()
        {
            Logger.Log("MainWindow ctor başladı");
            //this.AppWindow.IsShownInSwitchers = false;
            Instance = this;
            this.InitializeComponent();
            Logger.Log("InitializeComponent geçti");
            if (this.Content is FrameworkElement root)
                root.Loaded += Root_Loaded;
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
            ((FrameworkElement)this.Content).RequestedTheme = ElementTheme.Dark;
            this.Title = "Ukula";
            // 1. Ayarları yükle
            _settings = AppSettings.Load();
            Logger.Log("Settings load geçti");
            ApplyLanguage();
            Logger.Log("ApplyLanguage geçti");

            if (_settings.OcrEngine == "Tesseract")
                OcrEngineCombo.SelectedIndex = 1;
            else
                OcrEngineCombo.SelectedIndex = 0;

            OcrManager.CurrentEngine =
                _settings.OcrEngine;

            string version =
        Assembly.GetExecutingAssembly()
        .GetName()
        .Version?
        .ToString(3) ?? "1.0.0";

            VersionTextBlock.Text = $"Ukula v{version}";



            TranslationService.DeepLApiKey = _settings.DeepLApiKey;
            TranslationService.GoogleCloudApiKey = _settings.GoogleCloudApiKey;
            TranslationService.AzureApiKey = _settings.AzureApiKey;
            TranslationService.AzureRegion = _settings.AzureRegion;
            TranslationService.CurrentProvider = _settings.Provider;
            TranslationService.TargetLanguageCode = _settings.TargetLanguageCode;
            SavePathLabel.Text = _settings.ScreenshotSavePath;
            // 2. Uygulama dili
            string currentLang = _settings.AppLanguage;
            Logger.Log("Uygulama dili");
            AppLangCombo.SelectedItem = currentLang.StartsWith("tr") ? LangTr : LangEn;
            
            // 3. initialized
            _isInitialized = true;
            Logger.Log("initialized");

            // 4. Pencere ayarları
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            Logger.Log("Window setup başladı");
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            m_AppWindow = AppWindow.GetFromWindowId(windowId);
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(DragArea);
            this.SetIsResizable(true);
            this.SetIsAlwaysOnTop(_settings.AlwaysOnTop);
            bool isStartup = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            if (!isStartup)
            {
                ResetWindowSize();
            }
            // ÖNEMLİ: Burada Activate() veya Hide() çağırmıyoruz, işi App.xaml.cs'e bırakıyoruz.
            // Ayrıca kesinlikle 'return' kullanmıyoruz ki aşağıdaki hotkey ve event kurulumları eksik kalmasın.
            CustomAkrilikArkaplanAyarla();
            ConfigureTitleBar();
            Logger.Log("Window setup geçti");

            // 5. UI
            ApiKeyBox.Password = _settings.DeepLApiKey; 
            GoogleCloudKeyBox.Password = _settings.GoogleCloudApiKey;
            AzureKeyBox.Password = _settings.AzureApiKey;
            AzureRegionBox.Text = _settings.AzureRegion;
            EngineCombo.SelectedItem = _settings.Provider switch
            {
                TranslationProvider.GoogleCloud => GoogleCloudItem,
                TranslationProvider.DeepL => DeepLItem,
                TranslationProvider.Azure => AzureItem,
                _ => GoogleFreeItem
            };

            DeepLKeyArea.Visibility = _settings.Provider == TranslationProvider.DeepL
                ? Visibility.Visible : Visibility.Collapsed;
            GoogleCloudKeyArea.Visibility = _settings.Provider == TranslationProvider.GoogleCloud
                ? Visibility.Visible : Visibility.Collapsed;
            AzureKeyArea.Visibility = _settings.Provider == TranslationProvider.Azure
                ? Visibility.Visible : Visibility.Collapsed;
            Logger.Log("Hotkey ve renk");
            // 6. Hotkey ve renk
            Logger.Log("Hotkey başladı");
            StartHotkey();
            Logger.Log("Hotkey geçti");
            
            AlwaysOnTopToggle.IsOn = _settings.AlwaysOnTop;
            LaunchStartupToggle.IsOn = _settings.LaunchAtStartup;
            StartupManager.SetEnabled(_settings.LaunchAtStartup);
            ScreenshotHotkeyLabel.Text = HotkeyManager.Describe(
                _settings.ScreenshotHotkeyModifiers,
                _settings.ScreenshotHotkeyVk);
            HideOnCaptureToggle.IsOn = _settings.HideOnCapture;
            ApplyAccentColor();

            // 7. Pencere olayları
            m_AppWindow.Closing += (s, e) =>
            {
                if (_isExiting) return;
                e.Cancel = true;
                this.Hide();
            };
            this.Closed += (s, e) =>
            {
                HotkeyManager.Stop();
                _acrylicController?.Dispose();
            };

            // 8. WndProc
            var hwndMain = WindowNative.GetWindowHandle(this);
            _wndProcDelegate = CustomWndProc;
            _oldWndProc = SetWindowLongPtr(hwndMain, -4, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            // 9. Target dil combo
            foreach (var lang in TranslationService.SupportedLanguages)
                TargetLangCombo.Items.Add(lang.Key);
            TargetLangCombo.SelectedItem =
                TranslationService.SupportedLanguages
                    .FirstOrDefault(x => x.Value == _settings.TargetLanguageCode).Key
                ?? "Türkçe";
            TargetLangCombo.SelectionChanged += OnTargetLangChanged;

            // 10. Versiyon
            Logger.Log("SetIcon başladı");
            string iconPath = Path.Combine(AppContext.BaseDirectory,"Assets","Icons","app.ico");

            if (File.Exists(iconPath))
            {
                this.AppWindow.SetIcon(iconPath);
                Logger.Log("SetIcon geçti");
            }

            if (!File.Exists(iconPath))
            {
                // Eğer 48'lik yoksa senin belirttiğin 24'lüğü güvenli liman olarak kullansın
                iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.targetsize-24.png");
            }

            if (File.Exists(iconPath))
            {
                this.AppWindow.SetIcon(iconPath);
            }
            this.Activated += (_, _) =>
            {
                try
                {
                    string iconPath = Path.Combine(
                        AppContext.BaseDirectory,
                        "Assets",
                        "Icons",
                        "app.ico");

                    if (File.Exists(iconPath))
                        this.AppWindow.SetIcon(iconPath);
                }
                catch (Exception ex)
                {
                    Logger.Log("SetIcon failed", ex);
                }
            };
            ClearButton.Visibility = Visibility.Collapsed;
            Logger.Log("MainWindow ctor tamamlandı");
        }

        private void OnColorValuesChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            // UI thread'de çalıştırmak gerekiyor
            DispatcherQueue.TryEnqueue(() =>
            {
                RefreshAccentColors();
                ApplyAccentColor();
                // Akrilik arka planı da güncelle
                if (_acrylicController != null)
                    _acrylicController.TintColor = _uiSettings.GetColorValue(
                        Windows.UI.ViewManagement.UIColorType.Accent);
            });
        }

        private void OnHotkeyBoxRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var name = (sender as Button)?.Name;

            if (name == "ScreenshotHotkeyBox")
            {
                _settings.ScreenshotHotkeyModifiers = 0u;
                _settings.ScreenshotHotkeyVk = 0u;
            }
            else if (name == "ToggleWindowHotkeyBox")
            {
                _settings.ToggleWindowHotkeyModifiers = 0u;
                _settings.ToggleWindowHotkeyVk = 0u;
            }
            else
            {
                _settings.HotkeyModifiers = 0u;
                _settings.HotkeyVk = 0u;
            }

            _settings.Save();
            StartHotkey();
        }

        private void Root_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshAccentColors();
        }

        private void RefreshAccentColors()
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();

            var accent = uiSettings.GetColorValue(
                Windows.UI.ViewManagement.UIColorType.Accent);

            var accentDark1 = uiSettings.GetColorValue(
                Windows.UI.ViewManagement.UIColorType.AccentDark1);

            var accentDark2 = uiSettings.GetColorValue(
                Windows.UI.ViewManagement.UIColorType.AccentDark2);

            var accentDark3 = uiSettings.GetColorValue(
                Windows.UI.ViewManagement.UIColorType.AccentDark3);

            var accentLight1 = uiSettings.GetColorValue(
                Windows.UI.ViewManagement.UIColorType.AccentLight1);

            var accentLight2 = uiSettings.GetColorValue(
                Windows.UI.ViewManagement.UIColorType.AccentLight2);

            var accentLight3 = uiSettings.GetColorValue(
                Windows.UI.ViewManagement.UIColorType.AccentLight3);

            _accentColor = accent;
            _accentLight2Color = accentLight2;

            // Brushes
            var accentBrush = ThemeHelper.AccentBrush(accent);

            var light1Brush = ThemeHelper.Light1Brush(accentLight1);
            var light2Brush = ThemeHelper.Light2Brush(accentLight2);
            var light3Brush = ThemeHelper.Light3Brush(accentLight3);

            var dark1Brush = ThemeHelper.Dark1Brush(accentDark1);
            var dark2Brush = ThemeHelper.Dark2Brush(accentDark2);
            var dark3Brush = ThemeHelper.Dark3Brush(accentDark3);

            // Resources
            if (this.Content is FrameworkElement root)
            {
                root.Resources["SystemAccentColor"] = accentBrush;

                root.Resources["SystemAccentColorDark1"] = dark1Brush;
                root.Resources["SystemAccentColorDark2"] = dark2Brush;
                root.Resources["SystemAccentColorDark3"] = dark3Brush;

                root.Resources["SystemAccentColorLight1"] = light1Brush;
                root.Resources["SystemAccentColorLight2"] = light2Brush;
                root.Resources["SystemAccentColorLight3"] = light3Brush;
            }

            // Settings panel
            SettingsBorder.Background = dark3Brush;

            // ComboBox
            EngineCombo.Background = dark1Brush;
            EngineCombo.BorderBrush = light2Brush;

            OcrEngineCombo.Background = dark1Brush;
            OcrEngineCombo.BorderBrush = light2Brush;

            AppLangCombo.Background = dark1Brush;
            AppLangCombo.BorderBrush = light2Brush;

            TargetLangCombo.Background = dark1Brush;
            TargetLangCombo.BorderBrush = light2Brush;

            // Inputs
            ApiKeyBox.BorderBrush = light2Brush;
            ApiKeyBox.Background = dark1Brush;
            GoogleCloudKeyBox.BorderBrush = light2Brush;
            GoogleCloudKeyBox.Background = dark1Brush;
            AzureKeyBox.BorderBrush = light2Brush;
            AzureKeyBox.Background = dark1Brush;
            AzureRegionBox.BorderBrush = light2Brush;
            AzureRegionBox.Background = dark1Brush;

            // Hotkey
            TranslateHotkeyBox.Background = dark1Brush;
            TranslateHotkeyBox.BorderBrush = light2Brush;

            ScreenshotHotkeyBox.Background = dark1Brush;
            ScreenshotHotkeyBox.BorderBrush = light2Brush;

            ToggleWindowHotkeyBox.Background = dark1Brush;
            ToggleWindowHotkeyBox.BorderBrush = light2Brush;
            // Main buttons
            TranslateButton.Background = dark1Brush;
            TranslateButton.BorderBrush = light2Brush;

            ScreenshotButton.Background = dark1Brush;
            ScreenshotButton.BorderBrush = light2Brush;

            // Side buttons
            ToggleMenuButton.Background = dark1Brush;
            ToggleMenuButton.BorderBrush = light2Brush;

            ClearButton.Background = dark1Brush;
            ClearButton.BorderBrush = light2Brush;

            ClearKeys.Background = dark1Brush;
            ClearKeys.BorderBrush  = light2Brush;

            ScreenshotSavePathButton.Background = dark1Brush;
            ScreenshotSavePathButton.BorderBrush = light2Brush;
            // Result
            ResultBox.BorderBrush = light2Brush;
        }
        public void ExitApplication()
        {
            _isExiting = true;

            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        private async void OnPickSavePathClick(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _settings.ScreenshotSavePath = folder.Path;
                _settings.Save();
                SavePathLabel.Text = folder.Path;
            }
        }

        private static readonly Dictionary<string, Dictionary<string, string>> _strings = new()
        {
            ["en"] = new()
            {
                ["EngineHeader"] = "Translation Engine",
                ["OcrEngineHeader"] = "OCR Engine",
                ["ApiKeyBoxPlaceholder"] = "DeepL API key...",
                ["GoogleCloudKeyBoxPlaceholder"] = "Google API key...",
                ["AzureKeyBoxPlaceholder"] = "Azure API key...",
                ["LanguageHeader"] = "App Language",
                ["LanguageButton"] = "English",
                ["GoogleFreeContent"] = "Google (Free)",
                ["FlyoutTitle"] = "Select Language - Dil Seçin",
                ["TranslateHotkeyHeader"] = "Translate Hotkey",
                ["ScreenshotHotkeyHeader"] = "Screenshot Hotkey",
                ["NoTranslationText"] = "Translation failed.",
                ["DeepLNoKey"] = "⚠ No DeepL key",
                ["DeepLError"] = "⚠ DeepL error",
                ["GoogleCloudNoKey"] = "⚠ No Google Cloud key",
                ["GoogleCloudError"] = "⚠ Google Cloud error",
                ["AzureNoKey"] = "⚠ No Azure key",
                ["AzureError"] = "⚠ Azure error",
                ["DeepLKeyHeader"] = "DeepL API Key",
                ["GoogleCloudKeyHeader"] = "Google Cloud API Key",
                ["AzureKeyHeader"] = "Azure API Key",
                ["TargetLangHeader"] = "Target Language",
                ["LaunchStartupHeader"] = "Launch at startup",
                ["AlwaysOnTopHeader"] = "Always On Top",
                ["AzureRegionHeader"] = "Azure Region",
                ["ScreenshotSavePathHeader"] = "Screenshot Path",
                ["ToggleOn"] = "Enabled",
                ["ToggleOff"] = "Disabled",
                ["NoTextFound"] = "No text found",
                ["UnknownLanguage"] = "Unknown",
                ["TrayInfoMessage"] = "Right-click the tray icon to close the app.",
                ["APIKeyNotice"] = "It is encrypted only on this device.",
                ["APIKeyNoticeLong"] = "Your API key is encrypted and stored locally on this device via Windows Credential Manager; it is never sent to external servers, nor is it saved in any file or folder on your computer.",
                ["Privacy"] = "Privacy Policy",
                ["Terms"] = "Terms",
                ["UpdateCheck"] = "Check for Updates",
                ["UpdateAvailable"] = "Update Available",
                ["Version"] = "Version",
                ["ViewChangelog"] = "View Changelog",
                ["Download"] = "Download",
                ["Later"] = "Later",
                ["Downloading"] = "Downloading...",
                ["CheckUpdates"] = "Check for Updates",
                ["NoUpdates"] = "No Updates",
                ["LatestVersion"] = "You are using the latest version.",
                ["OK"] = "OK",
                ["UpdateError"] = "Update Error",
                ["UpdateDownloadFailed"] = "The update file is not available yet. Please try again in a few minutes.",
                ["UnknownUpdateError"] = "An unexpected update error occurred.",
                ["ClearKeys"] = "Clear Keys",
                ["HideOnCaptureHeader"] = "Hide window on selection",
                ["ToggleWindowHotkeyHeader"] = "Show/Hide Window",
            },
            ["tr"] = new()
            {
                ["EngineHeader"] = "Çeviri Motoru",
                ["OcrEngineHeader"] = "OCR Motoru",
                ["ApiKeyBoxPlaceholder"] = "API Anahtarı...",
                ["GoogleCloudKeyBoxPlaceholder"] = "Google API Anahtarı...",
                ["AzureKeyBoxPlaceholder"] = "Azure API Anahtarı...",
                ["LanguageHeader"] = "Uygulama Dili",
                ["LanguageButton"] = "Türkçe",
                ["GoogleFreeContent"] = "Google (ücretsiz)",
                ["FlyoutTitle"] = "Dil Seçin - Select Language",
                ["TranslateHotkeyHeader"] = "Çeviri Kısayolu",
                ["ScreenshotHotkeyHeader"] = "Ekran Görüntüsü Kısayolu",
                ["NoTranslationText"] = "Çeviri alınamadı.",
                ["DeepLNoKey"] = "⚠ DeepL anahtarı yok",
                ["DeepLError"] = "⚠ DeepL hatası",
                ["GoogleCloudNoKey"] = "⚠ Google Cloud anahtarı yok",
                ["GoogleCloudError"] = "⚠ Google Cloud hatası",
                ["AzureNoKey"] = "⚠ Azure anahtarı yok",
                ["AzureError"] = "⚠ Azure hatası",
                ["DeepLKeyHeader"] = "DeepL API Anahtarı",
                ["GoogleCloudKeyHeader"] = "Google Cloud API Anahtarı",
                ["AzureKeyHeader"] = "Azure API Anahtarı",
                ["TargetLangHeader"] = "Hedef Dil",
                ["LaunchStartupHeader"] = "Windows ile başlat",
                ["AlwaysOnTopHeader"] = "Her Zaman Üstte",
                ["AzureRegionHeader"] = "Azure Bölgesi",
                ["ScreenshotSavePathHeader"] = "Ekran Görüntüsü Yolu",
                ["ToggleOn"] = "Açık",
                ["ToggleOff"] = "Kapalı",
                ["NoTextFound"] = "Metin bulunamadı",
                ["UnknownLanguage"] = "Bilinmiyor",
                ["TrayInfoMessage"] = "Kapatmak için tray ikonuna sağ tıklayın.",
                ["APIKeyNotice"] = "Sadece bu cihazda şifrelenir.",
                ["APIKeyNoticeLong"] = "API anahtarınız sadece bu cihazda, Windows Kimlik Bilgisi Yöneticisi kullanılarak şifrelenmiş şekilde saklanır; hiçbir harici sunucuya iletilmez ve bilgisayarınızdaki herhangi bir dosya veya klasörde tutulmaz.",
                ["Privacy"] = "Gizlilik Politikası",
                ["Terms"] = "Kullanım Şartları",
                ["UpdateCheck"] = "Güncellemeleri Denetle",
                ["UpdateAvailable"] = "Güncelleme Mevcut",
                ["Version"] = "Sürüm",
                ["ViewChangelog"] = "Değişiklikler",
                ["Download"] = "İndir",
                ["Later"] = "Daha Sonra",
                ["Downloading"] = "İndiriliyor...",
                ["CheckUpdates"] = "Güncellemeleri Kontrol Et",
                ["NoUpdates"] = "Güncelleme Yok",
                ["LatestVersion"] = "En son sürümü kullanıyorsunuz.",
                ["OK"] = "Tamam",
                ["UpdateError"] = "Güncelleme Hatası",
                ["UpdateDownloadFailed"] = "Güncelleme dosyası henüz hazır değil. Lütfen birkaç dakika sonra tekrar deneyin.",
                ["UnknownUpdateError"] = "Beklenmeyen bir güncelleme hatası oluştu.",
                ["ClearKeys"] = "Keyleri Temizle",
                ["HideOnCaptureHeader"] = "Seçimde pencereyi gizle",
                ["ToggleWindowHotkeyHeader"] = "Pencere Göster/Gizle",


            }
        };

        private void ApplyLanguage()
        {
            // TextBlock Metinleri
            EngineHeader.Text = L("EngineHeader");
            OcrEngineHeader.Text = L("OcrEngineHeader");
            LanguageHeader.Text = L("LanguageHeader");
            TranslateHotkeyHeader.Text = L("TranslateHotkeyHeader");
            ScreenshotHotkeyHeader.Text = L("ScreenshotHotkeyHeader");
            DeepLKeyHeader.Text = L("DeepLKeyHeader");
            GoogleCloudKeyHeader.Text = L("GoogleCloudKeyHeader");
            AzureKeyHeader.Text = L("AzureKeyHeader");
            TargetLangHeader.Text = L("TargetLangHeader");
            AzureRegionHeader.Text = L("AzureRegionHeader");
            AlwaysOnTopHeader.Text = L("AlwaysOnTopHeader");
            LaunchStartupHeader.Text = L("LaunchStartupHeader");
            ScreenshotSavePathHeader.Text = L("ScreenshotSavePathHeader");
            APIKeyNotice.Text = L("APIKeyNotice");
            HideOnCaptureHeader.Text = L("HideOnCaptureHeader");
            ToggleWindowHotkeyHeader.Text = L("ToggleWindowHotkeyHeader");
            // ComboBoxItem ve Buton İçerikleri (.Content)
            GoogleFreeItem.Content = L("GoogleFreeContent");
            AlwaysOnTopToggle.OnContent = L("ToggleOn");
            AlwaysOnTopToggle.OffContent = L("ToggleOff");
            LaunchStartupToggle.OnContent = L("ToggleOn");
            LaunchStartupToggle.OffContent = L("ToggleOff");
            HideOnCaptureToggle.OnContent = L("ToggleOn");
            HideOnCaptureToggle.OffContent = L("ToggleOff");
            APIKeyNoticeLong.Content = L("APIKeyNoticeLong");
            ClearKeys.Content = L("ClearKeys");
            Privacy.Content = L("Privacy");
            Terms.Content = L("Terms");
            UpdateCheck.Content = L("UpdateCheck");
            // Şifre Giriş Kutularının Placeholder Yazıları
            ApiKeyBox.PlaceholderText = L("ApiKeyBoxPlaceholder");
            GoogleCloudKeyBox.PlaceholderText = L("GoogleCloudKeyBoxPlaceholder");
            AzureKeyBox.PlaceholderText = L("AzureKeyBoxPlaceholder");
        }

        public string L(string key)
        {
            // Varsayılan dil kontrolleri ve güvenli sözlük okuması
            var lang = (_settings?.AppLanguage != null && _settings.AppLanguage.StartsWith("tr")) ? "tr" : "en";
            return _strings.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var val) ? val : key;
        }
        private void OcrEngineCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (OcrEngineCombo.SelectedItem is ComboBoxItem item)
            {
                _settings.OcrEngine =
                    item.Tag?.ToString() ?? "Windows";

                OcrManager.CurrentEngine =
                    _settings.OcrEngine;

                _settings.Save();
            }
        }
        private void CustomAkrilikArkaplanAyarla()
        {
            if (!DesktopAcrylicController.IsSupported()) return;

            _configurationSource = new SystemBackdropConfiguration();
            this.Activated += Window_Activated;
            this.Closed += Window_Closed;
            _configurationSource.IsInputActive = true;

            _acrylicController = new DesktopAcrylicController();
            _acrylicController.TintColor = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            _acrylicController.TintOpacity = 0.5f;
            _acrylicController.LuminosityOpacity = 0.5f;
            _acrylicController.FallbackColor = Color.FromArgb(255, 20, 20, 30);

            var compositionSource = WinRT.CastExtensions.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>(this);
            _acrylicController.AddSystemBackdropTarget(compositionSource);
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_configurationSource != null)
                _configurationSource.IsInputActive = true;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            _acrylicController?.Dispose();
            _acrylicController = null;
            this.Activated -= Window_Activated;
            _configurationSource = null;
        }

        private void ApplyAccentColor()
        {
            var accent = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            var bg20 = Color.FromArgb(51, accent.R, accent.G, accent.B);
            var bg40 = Color.FromArgb(102, accent.R, accent.G, accent.B);
            var bg15 = Color.FromArgb(38, accent.R, accent.G, accent.B);
            _accentColor = accent;
        }

        private void OnTargetLangChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TargetLangCombo.SelectedItem is string selected &&
                TranslationService.SupportedLanguages.TryGetValue(selected, out var code))
            {
                TranslationService.TargetLanguageCode = code;
                _settings.TargetLanguageCode = code;
                _settings.Save();
            }
        }
        private void OnHideOnCaptureToggled(object sender, RoutedEventArgs e)
        {
            if (_settings == null || sender is not ToggleSwitch toggle) return;

            _settings.HideOnCapture = toggle.IsOn;
            _settings.Save();
        }
        private void OnAppLangChanged(object sender, SelectionChangedEventArgs e)
        {
            // Uygulama ilk yüklenirken (Initialize aşamasında) buranın çalışmasını engeller, çökme yaşatmaz.
            if (!_isInitialized) return;

            // Ayarlar nesnesi henüz bellekte değilse güvenlik önlemi
            if (_settings == null) return;

            if (AppLangCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string selectedLang = item.Tag.ToString()!;
                _settings.AppLanguage = selectedLang;
                // Canlı dil değiştirme
                ApplyLanguage();
            }
        }

        private void StartHotkey()
        {
            HotkeyManager.Stop();

            if (_settings.HotkeyVk != 0 ||
                _settings.ScreenshotHotkeyVk != 0 ||
                _settings.ToggleWindowHotkeyVk != 0)
            {
                HotkeyManager.Start(

                    // Translate
                    _settings.HotkeyModifiers,
                    _settings.HotkeyVk,
                    () =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            OnHotkeyPressed();
                        });
                    },

                    // Screenshot
                    _settings.ScreenshotHotkeyModifiers,
                    _settings.ScreenshotHotkeyVk,
                    () =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            OnScreenshotHotkeyPressed();
                        });
                    },

                    // Toggle Window
                    _settings.ToggleWindowHotkeyModifiers,
                    _settings.ToggleWindowHotkeyVk,
                    () =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ToggleMainWindow();
                        });
                    });
            }

            if (TranslateHotkeyLabel != null)
            {
                TranslateHotkeyLabel.Text =
                    HotkeyManager.Describe(
                        _settings.HotkeyModifiers,
                        _settings.HotkeyVk);
            }

            if (ScreenshotHotkeyLabel != null)
            {
                ScreenshotHotkeyLabel.Text =
                    HotkeyManager.Describe(
                        _settings.ScreenshotHotkeyModifiers,
                        _settings.ScreenshotHotkeyVk);
            }

            if (ToggleWindowHotkeyLabel != null)
            {
                ToggleWindowHotkeyLabel.Text =
                    HotkeyManager.Describe(
                        _settings.ToggleWindowHotkeyModifiers,
                        _settings.ToggleWindowHotkeyVk);
            }
        }

        private void ToggleMainWindow()
        {
            if (_windowHidden)
            {
                this.Show();
                _windowHidden = false;
            }
            else
            {
                this.Hide();
                _windowHidden = true;
            }
        }

        private void OnHotkeyPressed() => DispatcherQueue.TryEnqueue(() => TriggerSelection());

        private void OnScreenshotHotkeyPressed() => DispatcherQueue.TryEnqueue(() => TriggerScreenshotSelection());

        private void ToggleMenu(object sender, RoutedEventArgs e) =>
            RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;

        private void ConfigureTitleBar()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;
            var titleBar = m_AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(60, 255, 255, 255);
        }

        private void OnSelectAreaClick(object sender, RoutedEventArgs e) => TriggerSelection();

        private void OnScreenshotClick(object sender, RoutedEventArgs e) => TriggerScreenshotSelection();
        private void BringToFront()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);

            SetForegroundWindow(hwnd);
        }

        private async void TriggerSelection()
        {
            if (_selectionWindow != null) return;

            // 1. Eğer ayar açık ise pencereyi gizle ve kaybolması için minik bir gecikme ver
            if (_settings.HideOnCapture)
            {
                this.Hide();
                await Task.Delay(150);
            }

            _selectionWindow = new SelectionWindow(_accentColor);

            _selectionWindow.Closed += (s, args) =>
            {
                _selectionWindow = null;
                DispatcherQueue.TryEnqueue(() =>
                {
                    // 2. Seçim ekranı kapandığında (gizlenmiş olsun veya olmasın) ana pencereyi geri çağırıyoruz
                    this.Show();
                    this.Activate();
                    this.BringToFront();
                });
            };

            _selectionWindow.SelectionCompleted += async (bitmap) =>
            {
                if (bitmap == null) return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    LoadingRing.IsActive = true;
                    LoadingRing.Visibility = Visibility.Visible;
                });

                try
                {
                    // OCR
                    string text = await OcrManager.RecognizeTextAsync(bitmap);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ShowResult("", new TranslationResult
                            {
                                TranslatedText = L("NoTextFound")
                            });
                        });
                        return;
                    }

                    // Çeviri
                    var result = await TranslationService.TranslateAsync(text);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ShowResult(text, result);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
                finally
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        LoadingRing.IsActive = false;
                        LoadingRing.Visibility = Visibility.Collapsed;
                    });
                }
            };

            _selectionWindow.Activate();
        }
        private async void TriggerScreenshotSelection()
        {
            if (_selectionWindow != null) return;

            // 1. Eğer ayar açık ise pencereyi gizle ve tamamen kaybolması için minik bir gecikme ver
            if (_settings.HideOnCapture)
            {
                this.Hide();
                await Task.Delay(150);
            }

            _selectionWindow = new SelectionWindow(_accentColor);

            _selectionWindow.Closed += (s, args) =>
            {
                _selectionWindow = null;
                DispatcherQueue.TryEnqueue(() =>
                {
                    // 2. Seçim ekranı kapandığında ana pencereyi kesinlikle geri çağırıyoruz
                    this.Show();
                    this.Activate();
                    this.BringToFront();
                });
            };

            _selectionWindow.SelectionCompleted += (bitmap) =>
            {
                if (bitmap == null) return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    int offset = _pinnedWindowCount * 30;
                    int w = Math.Clamp(bitmap.PixelWidth / 2, 200, 400);
                    int h = Math.Clamp(bitmap.PixelHeight / 2, 150, 350);

                    var pinnedWindow = new PinnedScreenshotWindow(bitmap, _accentColor, _accentLight2Color, offset, offset, _settings.ScreenshotSavePath);
                    _pinnedWindowCount++;
                    pinnedWindow.Closed += (s, a) => _pinnedWindowCount = Math.Max(0, _pinnedWindowCount - 1);
                    pinnedWindow.Activate();
                });
            };

            _selectionWindow.Activate();
        }
        public void ResetWindowSize()
        {
            m_AppWindow.Resize(
                new Windows.Graphics.SizeInt32(400, 200));

            this.CenterOnScreen(400, 200);
        }

        private async Task ResizeWindowToContent()
        {
            await Task.Delay(10);

            MainPanel.UpdateLayout();
            ResultBox.UpdateLayout();

            double desiredHeight =
                MainPanel.ActualHeight + 80;

            int minHeight = 260;
            int maxHeight = 900;

            int finalHeight = (int)Math.Clamp(
                desiredHeight,
                minHeight,
                maxHeight);

            m_AppWindow.Resize(
                new Windows.Graphics.SizeInt32(420, finalHeight));

            MainScrollViewer.MaxHeight =
                finalHeight >= maxHeight
                    ? maxHeight - 40
                    : double.PositiveInfinity;

            Logger.Log($"Resize -> {finalHeight}");
        }

        private async void ShowResult(string original, TranslationResult result)
        {
            ClearButton.Visibility = Visibility.Visible;
            OriginalTextDisplay.Text = original;
            LanguageLabel.Text =
                !string.IsNullOrEmpty(result.Warning)
                    ? result.Warning
                    : string.IsNullOrWhiteSpace(result.DetectedLanguage)
                        ? L("UnknownLanguage")
                        : result.DetectedLanguage.ToUpperInvariant();

            TranslatedTextDisplay.Text =
                string.IsNullOrWhiteSpace(result.TranslatedText)
                    ? "Translation failed."
                    : result.TranslatedText;

            ResultBox.Visibility = Visibility.Visible;
            
            await Task.Delay(10);

            OriginalTextDisplay.UpdateLayout();
            TranslatedTextDisplay.UpdateLayout();
            ResultBox.UpdateLayout();

            double textHeight =
                OriginalTextDisplay.ActualHeight +
                TranslatedTextDisplay.ActualHeight;

            double desiredHeight =
                textHeight + 230;

            int finalHeight = (int)Math.Clamp(
                desiredHeight,
                220,
                650);

            m_AppWindow.Resize(
                new Windows.Graphics.SizeInt32(
                    520,
                    finalHeight));

            MainScrollViewer.MaxHeight =
                finalHeight >= 850
                    ? 760
                    : double.PositiveInfinity;
        }

        private void ResetTranslation(object sender, RoutedEventArgs e)
        {
            ClearButton.Visibility = Visibility.Collapsed;
            ResultBox.Visibility = Visibility.Collapsed;
            OriginalTextDisplay.Text = string.Empty;
            TranslatedTextDisplay.Text = string.Empty;
            LanguageLabel.Text = string.Empty;
            MainScrollViewer.MaxHeight = double.PositiveInfinity;
            DispatcherQueue.TryEnqueue(() =>
                m_AppWindow.Resize(new Windows.Graphics.SizeInt32(400, 200)));
        }

        private void ProviderChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (EngineCombo.SelectedItem is not ComboBoxItem item) return;

            if (item == GoogleFreeItem) TranslationService.CurrentProvider = TranslationProvider.Google;
            else if (item == GoogleCloudItem) TranslationService.CurrentProvider = TranslationProvider.GoogleCloud;
            else if (item == DeepLItem) TranslationService.CurrentProvider = TranslationProvider.DeepL;
            else if (item == AzureItem) TranslationService.CurrentProvider = TranslationProvider.Azure;

            DeepLKeyArea.Visibility = TranslationService.CurrentProvider == TranslationProvider.DeepL
                ? Visibility.Visible : Visibility.Collapsed;
            GoogleCloudKeyArea.Visibility = TranslationService.CurrentProvider == TranslationProvider.GoogleCloud
                ? Visibility.Visible : Visibility.Collapsed;
            AzureKeyArea.Visibility = TranslationService.CurrentProvider == TranslationProvider.Azure
                ? Visibility.Visible : Visibility.Collapsed;

            _settings.Provider = TranslationService.CurrentProvider;
            _settings.Save();
        }
        private void AzureRegionChanged(object sender, TextChangedEventArgs e)
        {
            TranslationService.AzureRegion = AzureRegionBox.Text;
            _settings.AzureRegion = AzureRegionBox.Text;
        }

        private void ApiKeyChanged(object sender, RoutedEventArgs e)
        {
            TranslationService.DeepLApiKey = ApiKeyBox.Password;
            _settings.DeepLApiKey = ApiKeyBox.Password;
        }

        private void GoogleCloudKeyChanged(object sender, RoutedEventArgs e)
        {
            _settings.GoogleCloudApiKey = GoogleCloudKeyBox.Password;
            TranslationService.GoogleCloudApiKey = GoogleCloudKeyBox.Password;
        }

        private void AzureKeyChanged(object sender, RoutedEventArgs e)
        {
            _settings.AzureApiKey = AzureKeyBox.Password;
            TranslationService.AzureApiKey = AzureKeyBox.Password;
        }

        private void AlwaysOnTopToggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            _settings.AlwaysOnTop = AlwaysOnTopToggle.IsOn;
            _settings.Save();
            this.SetIsAlwaysOnTop(AlwaysOnTopToggle.IsOn);
        }

        private void StartupToggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            _settings.LaunchAtStartup = LaunchStartupToggle.IsOn;
            _settings.Save();
            StartupManager.SetEnabled(LaunchStartupToggle.IsOn);
        }

        private void OnHotkeyBoxGotFocus(object sender, RoutedEventArgs e)
        {
            _recordingHotkey = true;
            _pendingMods = 0;
            _pendingVk = 0;

            // Hangi box aktif?
            _recordingScreenshotHotkey = (sender as Button)?.Name == "ScreenshotHotkeyBox";
            _recordingToggleWindowHotkey = (sender as Button)?.Name == "ToggleWindowHotkeyBox";
            GetActiveHotkeyLabel().Text = "Press keys...";
        }

        private void OnHotkeyBoxLostFocus(object sender, RoutedEventArgs e)
        {
            _recordingHotkey = false;
            if (_recordingScreenshotHotkey)
                ScreenshotHotkeyLabel.Text = HotkeyManager.Describe(
                    _settings.ScreenshotHotkeyModifiers, _settings.ScreenshotHotkeyVk);
            else if (_recordingToggleWindowHotkey)
                ToggleWindowHotkeyLabel.Text = HotkeyManager.Describe(
                    _settings.ToggleWindowHotkeyModifiers, _settings.ToggleWindowHotkeyVk);
            else
                TranslateHotkeyLabel.Text = HotkeyManager.Describe(
                    _settings.HotkeyModifiers, _settings.HotkeyVk);
        }

        private void OnHotkeyBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_recordingHotkey) return;
            e.Handled = true;
            var key = e.Key;

            if (key == VirtualKey.Control || key == VirtualKey.LeftControl || key == VirtualKey.RightControl)
            { _pendingMods |= HotkeyManager.MOD_CTRL; UpdateHotkeyLabel(); return; }
            if (key == VirtualKey.Shift || key == VirtualKey.LeftShift || key == VirtualKey.RightShift)
            { _pendingMods |= HotkeyManager.MOD_SHIFT; UpdateHotkeyLabel(); return; }
            if (key == VirtualKey.Menu || key == VirtualKey.LeftMenu || key == VirtualKey.RightMenu)
            { _pendingMods |= HotkeyManager.MOD_ALT; UpdateHotkeyLabel(); return; }

            if (key == VirtualKey.Escape)
            {
                _recordingHotkey = false;
                if (_recordingScreenshotHotkey)
                {
                    ScreenshotHotkeyLabel.Text =
                        HotkeyManager.Describe(
                            _settings.ScreenshotHotkeyModifiers,
                            _settings.ScreenshotHotkeyVk);
                }
                else if (_recordingToggleWindowHotkey)
                {
                    ToggleWindowHotkeyLabel.Text =
                        HotkeyManager.Describe(
                            _settings.ToggleWindowHotkeyModifiers,
                            _settings.ToggleWindowHotkeyVk);
                }
                else
                {
                    TranslateHotkeyLabel.Text =
                        HotkeyManager.Describe(
                            _settings.HotkeyModifiers,
                            _settings.HotkeyVk);
                }
            }

            _pendingVk = (uint)key;

            if (_recordingScreenshotHotkey)
            {
                _settings.ScreenshotHotkeyModifiers = _pendingMods;
                _settings.ScreenshotHotkeyVk = _pendingVk;
            }
            else if (_recordingToggleWindowHotkey)
            {
                _settings.ToggleWindowHotkeyModifiers = _pendingMods;
                _settings.ToggleWindowHotkeyVk = _pendingVk;
            }
            else
            {
                _settings.HotkeyModifiers = _pendingMods;
                _settings.HotkeyVk = _pendingVk;
            }

            _settings.Save();
            _recordingHotkey = false;
            _recordingScreenshotHotkey = false;
            _recordingToggleWindowHotkey = false;
            StartHotkey(); // Gerekirse burada da hangi hotkey'in yeniden kaydedileceğini ayırt edin
            TranslateButton.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }

        private void OnHotkeyBoxKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (!_recordingHotkey) return;
            var key = e.Key;
            if (key == VirtualKey.Control || key == VirtualKey.LeftControl || key == VirtualKey.RightControl)
                _pendingMods &= ~HotkeyManager.MOD_CTRL;
            if (key == VirtualKey.Shift || key == VirtualKey.LeftShift || key == VirtualKey.RightShift)
                _pendingMods &= ~HotkeyManager.MOD_SHIFT;
            if (key == VirtualKey.Menu || key == VirtualKey.LeftMenu || key == VirtualKey.RightMenu)
                _pendingMods &= ~HotkeyManager.MOD_ALT;
            UpdateHotkeyLabel();
        }

        private void UpdateHotkeyLabel()
        {
            string s = "";
            if ((_pendingMods & HotkeyManager.MOD_CTRL) != 0) s += "Ctrl+";
            if ((_pendingMods & HotkeyManager.MOD_ALT) != 0) s += "Alt+";
            if ((_pendingMods & HotkeyManager.MOD_SHIFT) != 0) s += "Shift+";
            GetActiveHotkeyLabel().Text = string.IsNullOrEmpty(s) ? "Press keys..." : s + "?";
        }

        // Yardımcı: aktif label'ı döndür
        private TextBlock GetActiveHotkeyLabel()
        {
            if (_recordingScreenshotHotkey)
                return ScreenshotHotkeyLabel;

            if (_recordingToggleWindowHotkey)
                return ToggleWindowHotkeyLabel;

            return TranslateHotkeyLabel;
        }

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const uint WM_GETMINMAXINFO = 0x0024;

            if (msg == WM_GETMINMAXINFO)
            {
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);

                info.ptMinTrackSize.x = 400;
                info.ptMinTrackSize.y = 200;

                var displayArea = DisplayArea.GetFromWindowId(
                    m_AppWindow.Id,
                    DisplayAreaFallback.Primary);

                var area = displayArea.WorkArea;

                info.ptMaxTrackSize.x = area.Width;
                info.ptMaxTrackSize.y = area.Height;

                Marshal.StructureToPtr(info, lParam, true);

                return IntPtr.Zero;
            }

            return CallWindowProc(
                _oldWndProc,
                hWnd,
                msg,
                wParam,
                lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT2 { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO
        {
            public POINT2 ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }
        
        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                string currentVersion =
                    Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version?
                    .ToString(3) ?? "1.0.0";

                using HttpClient client = new();

                string json = await client.GetStringAsync(
                    "https://raw.githubusercontent.com/kursatkrldg/update/main/version.json");

                UpdateInfo? updateInfo =
                    JsonSerializer.Deserialize<UpdateInfo>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                if (updateInfo == null)
                    return;

                Version current = new(currentVersion);

                if (!Version.TryParse(updateInfo.Version, out Version? latest))
                    return;
                
                if (latest > current)
                {
                    ContentDialog dialog = new()
                    {
                        Title = L("UpdateAvailable"),
                        Content = new StackPanel
                        {
                            Spacing = 10,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = $"{L("Version")}: {updateInfo.Version}"
                                },
                                new HyperlinkButton
                                {
                                    Content = L("ViewChangelog"),

                                    NavigateUri =
                                        new Uri(updateInfo.ChangelogUrl)
                                }
                            }
                        },
                        PrimaryButtonText = L("Download"),
                        CloseButtonText = L("Later"),
                        XamlRoot = this.Content.XamlRoot
                    };

                    var result = await dialog.ShowAsync();


                    if (result == ContentDialogResult.Primary)
                    {
                        try
                        {
                            UpdateCheck.Content = L("Downloading");
                            UpdateCheck.IsEnabled = false;

                            using HttpClient downloadClient = new();

                            byte[] installerBytes =
                                await downloadClient.GetByteArrayAsync(
                                    updateInfo.DownloadUrl);

                            string tempPath = Path.Combine(
                                Path.GetTempPath(),
                                $"UkulaSetup_{updateInfo.Version}.exe");

                            await File.WriteAllBytesAsync(
                                tempPath,
                                installerBytes);

                            Process.Start(new ProcessStartInfo
                            {
                                FileName = tempPath,
                                UseShellExecute = true
                            });

                            Microsoft.UI.Xaml.Application.Current.Exit();
                        }
                        catch (HttpRequestException)
                        {
                            UpdateCheck.Content = L("CheckUpdates");
                            UpdateCheck.IsEnabled = true;

                            ContentDialog errorDialog = new()
                            {
                                Title = L("UpdateError"),
                                Content = L("UpdateDownloadFailed"),
                                CloseButtonText = L("OK"),
                                XamlRoot = this.Content.XamlRoot
                            };

                            await errorDialog.ShowAsync();
                        }
                        catch (Exception)
                        {
                            UpdateCheck.Content = L("CheckUpdates");
                            UpdateCheck.IsEnabled = true;

                            ContentDialog errorDialog = new()
                            {
                                Title = L("UpdateError"),
                                Content = L("UnknownUpdateError"),
                                CloseButtonText = L("OK"),
                                XamlRoot = this.Content.XamlRoot
                            };

                            await errorDialog.ShowAsync();
                        }
                    }
                }
                else
                {
                    ContentDialog dialog = new()
                    {
                        Title = L("NoUpdates"),
                        Content = L("LatestVersion"),
                        CloseButtonText = L("OK"),
                        XamlRoot = this.Content.XamlRoot
                    };

                    await dialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                UpdateCheck.Content = L("CheckUpdates");
                UpdateCheck.IsEnabled = true;

                ContentDialog dialog = new()
                {
                    Title = L("UpdateError"),
                    Content = ex.Message,
                    CloseButtonText = L("OK"),
                    XamlRoot = this.Content.XamlRoot
                };

                await dialog.ShowAsync();
            }
        }


        private void OnClearApiKeysClick(
    object sender,
    RoutedEventArgs e)
        {
            SecureStorage.DeleteAll();

            GoogleCloudKeyBox.Password = "";
            ApiKeyBox.Password = "";
            AzureKeyBox.Password = "";
        }
    }
}
