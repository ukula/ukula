using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace UkulaApp
{
    public static class HotkeyManager
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] static extern void PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX, ptY;
        }

        const int WM_HOTKEY = 0x0312;
        const int TRANSLATE_HOTKEY_ID = 9001;
        const int SCREENSHOT_HOTKEY_ID = 9002;
        const int TOGGLE_WINDOW_HOTKEY_ID = 9003;

        // Modifier flagleri
        public const uint MOD_NONE = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CTRL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        private static Thread? _thread;
        private static uint _threadId;
        private static volatile bool _running;

        public static void Start(
            uint transMods, uint transVk, Action onTranslate,
            uint sshotMods, uint sshotVk, Action onScreenshot,
            uint toggleMods, uint toggleVk, Action onToggleWindow)
        {
            if (_thread != null) return;

            _running = true;

            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();

                bool translateOk = transVk != 0 && RegisterHotKey(IntPtr.Zero, TRANSLATE_HOTKEY_ID, transMods, transVk);
                bool screenshotOk = sshotVk != 0 && RegisterHotKey(IntPtr.Zero, SCREENSHOT_HOTKEY_ID, sshotMods, sshotVk);
                bool toggleOk = toggleVk != 0 && RegisterHotKey(IntPtr.Zero, TOGGLE_WINDOW_HOTKEY_ID, toggleMods, toggleVk);

                if (!translateOk && transVk != 0) Logger.Log("Failed to register translate hotkey");
                if (!screenshotOk && sshotVk != 0) Logger.Log("Failed to register screenshot hotkey");
                if (!toggleOk && toggleVk != 0) Logger.Log("Failed to register toggle window hotkey");

                while (_running && GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
                {
                    if (msg.message == WM_HOTKEY)
                    {
                        int id = msg.wParam.ToInt32();
                        if (id == TRANSLATE_HOTKEY_ID)
                        {
                            onTranslate();
                        }
                        else if (id == SCREENSHOT_HOTKEY_ID)
                        {
                            onScreenshot();
                        }
                        else if (id == TOGGLE_WINDOW_HOTKEY_ID) // Yeni tetikleyici
                        {
                            onToggleWindow();
                        }
                    }

                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (translateOk) UnregisterHotKey(IntPtr.Zero, TRANSLATE_HOTKEY_ID);
                if (screenshotOk) UnregisterHotKey(IntPtr.Zero, SCREENSHOT_HOTKEY_ID);
                if (toggleOk) UnregisterHotKey(IntPtr.Zero, TOGGLE_WINDOW_HOTKEY_ID); // Yeni silici
            });

            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public static void Stop()
        {
            _running = false;
            if (_threadId != 0)
            {
                // WM_QUIT ile mesaj döngüsünü bitir
                PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
                _threadId = 0;
            }
            _thread = null;
        }

        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        public static string Describe(uint modifiers, uint vk)
        {
            if (vk == 0) return "—";
            string s = "";
            if ((modifiers & MOD_CTRL) != 0) s += "Ctrl+";
            if ((modifiers & MOD_ALT) != 0) s += "Alt+";
            if ((modifiers & MOD_SHIFT) != 0) s += "Shift+";
            if ((modifiers & MOD_WIN) != 0) s += "Win+";
            s += ((Windows.System.VirtualKey)vk).ToString();
            return s;
        }
    }
}
