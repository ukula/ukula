using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Linq;
using WinUIEx;
using Microsoft.Win32;

namespace UkulaApp
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private TaskbarIcon? _trayIcon;
        private AppInstance? _mainInstance;

        public App()
        {
            Logger.Log("App ctor started");

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.Log("Unhandled Exception:", LogLevel.Error);
                Logger.Log(
                    e.ExceptionObject?.ToString()
                    ?? "Unknown Error",
                    LogLevel.Error);
            };

            this.InitializeComponent();

            Logger.Log("InitializeComponent completed");
        }

        protected override void OnLaunched(
            LaunchActivatedEventArgs args)
        {
            Logger.Log("OnLaunched started");

            _mainInstance =
                AppInstance.FindOrRegisterForKey(
                    "UkulaApp");

            if (!_mainInstance.IsCurrent)
            {
                _mainInstance
                    .RedirectActivationToAsync(
                        AppInstance
                            .GetCurrent()
                            .GetActivatedEventArgs())
                    .AsTask()
                    .Wait();

                Current.Exit();

                return;
            }

            if (!IsMinimizedLaunch())
            {
                _mainInstance.Activated += OnAppActivated;
            }
            _window = new MainWindow();
            try
            {
                Logger.Log("Tray setup started");

                _trayIcon = new TaskbarIcon
                {
                    IconSource =
                        new Microsoft.UI.Xaml.Media.Imaging
                            .BitmapImage(
                                new Uri(
                                    "ms-appx:///Assets/Icons/app.ico")),

                    ToolTipText = "Ukula"
                };

                _trayIcon.LeftClickCommand =
                    new RelayCommand(() =>
                    {
                        Logger.Log("Tray left click");
                        ShowMainWindow();
                    });

                _trayIcon.RightClickCommand =
                    new RelayCommand(() =>
                    {
                        Logger.Log("Tray right click -> exit");

                        MainWindow.Instance
                            ?.DispatcherQueue
                            .TryEnqueue(() =>
                            {
                                MainWindow.Instance
                                    .ExitApplication();
                            });
                    });

                _trayIcon.ForceCreate();

                if (!HasShownTrayTip())
                {
                    _trayIcon?.ShowNotification(
                        title: "Ukula",
                        message: _window.L("TrayInfoMessage"));

                    MarkTrayTipShown();
                }
                Logger.Log("Tray created");
            }
            catch (Exception ex)
            {
                Logger.Log(
                    "Tray setup failed",
                    ex);
            }
            if (IsMinimizedLaunch())
            {
                _window.Hide();

                if (!HasShownTrayTip())
                {
                    _trayIcon?.ShowNotification(
                        title: "Ukula",
                        message: _window.L("TrayInfoMessage"));

                    MarkTrayTipShown();
                }
            }
            else
            {
                _window.Activate();
            }
        }

        private bool HasShownTrayTip()
        {
            using var key =
                Registry.CurrentUser.CreateSubKey(
                    @"Software\Ukula");

            return (int?)key?.GetValue("TrayTipShown", 0) == 1;
        }

        private void MarkTrayTipShown()
        {
            using var key =
                Registry.CurrentUser.CreateSubKey(
                    @"Software\Ukula");

            key?.SetValue("TrayTipShown", 1);
        }

        private static bool IsMinimizedLaunch()
        {
            return Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        }

        private static void ShowMainWindow()
        {
            MainWindow.Instance
                ?.DispatcherQueue
                .TryEnqueue(() =>
                {
                    MainWindow.Instance.ResetWindowSize();
                    MainWindow.Instance.Show();
                    MainWindow.Instance.Activate();
                });
        }

        private void OnAppActivated(
            object? sender,
            AppActivationArguments args)
        {
            try
            {
                ShowMainWindow();
            }
            catch (Exception ex)
            {
                Logger.Log(
                    "Activation failed",
                    ex);
            }
        }

        private void ExitApp_Click(
            object sender,
            RoutedEventArgs e)
        {
            Logger.Log("Exit clicked");

            MainWindow.Instance
                ?.ExitApplication();
        }
    }
}
