using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UkulaApp
{
    public enum TargetLanguage { Turkish, English }

    public class AppSettings
    {
        private static readonly bool _isPackaged = IsPackaged();

        private static readonly string _jsonPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "Ukula",
            "settings.json");

        private static Windows.Storage.ApplicationDataContainer? _local;

        private readonly Dictionary<string, string> _cache = new();
        private static readonly object _lock = new();

        private static AppSettings? _instance;
        public string OcrEngine { get; set; } = "Windows";
        public static bool IsPackaged()
        {
            try
            {
                var _ = Windows.ApplicationModel.Package.Current;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("IsPackaged false", ex);
                return false;
            }
        }
       
        private static void EnsureLocal()
        {
            if (_isPackaged && _local == null)
            {
                _local =
                    Windows.Storage.ApplicationData
                        .Current
                        .LocalSettings;
            }
        }

        private string? GetValue(string key)
        {
            if (_isPackaged)
            {
                EnsureLocal();

                return _local?.Values[key] as string;
            }

            return _cache.TryGetValue(key, out var value)
                ? value
                : null;
        }

        private void SetValue(string key, string value)
        {
            if (_isPackaged)
            {
                EnsureLocal();

                if (_local != null)
                    _local.Values[key] = value;
            }
            else
            {
                _cache[key] = value;
                SaveJson();
            }
        }

        // -----------------------------
        // NORMAL SETTINGS
        // -----------------------------

        public string AppLanguage
        {
            get => GetValue("AppLanguage") ?? "en";
            set => SetValue("AppLanguage", value);
        }

        public TranslationProvider Provider
        {
            get =>
                Enum.TryParse<TranslationProvider>(
                    GetValue("Provider"),
                    out var value)
                    ? value
                    : TranslationProvider.Google;

            set => SetValue("Provider", value.ToString());
        }

        public string AzureRegion
        {
            get => GetValue("AzureRegion") ?? "";
            set => SetValue("AzureRegion", value);
        }

        public string TargetLanguageCode
        {
            get => GetValue("TargetLanguageCode") ?? "en";
            set => SetValue("TargetLanguageCode", value);
        }

        public string ScreenshotSavePath
        {
            get => GetValue("ScreenshotSavePath") ??
                   Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            set => SetValue("ScreenshotSavePath", value);
        }

        public uint HotkeyModifiers
        {
            get =>
                uint.TryParse(
                    GetValue("HotkeyModifiers"),
                    out var value)
                    ? value
                    : 0u;

            set => SetValue(
                "HotkeyModifiers",
                value.ToString());
        }

        public uint HotkeyVk
        {
            get =>
                uint.TryParse(
                    GetValue("HotkeyVk"),
                    out var value)
                    ? value
                    : 0u;

            set => SetValue(
                "HotkeyVk",
                value.ToString());
        }

        public uint ScreenshotHotkeyModifiers
        {
            get =>
                uint.TryParse(
                    GetValue("ScreenshotHotkeyModifiers"),
                    out var value)
                    ? value
                    : 0u;

            set => SetValue(
                "ScreenshotHotkeyModifiers",
                value.ToString());
        }

        public uint ScreenshotHotkeyVk
        {
            get =>
                uint.TryParse(
                    GetValue("ScreenshotHotkeyVk"),
                    out var value)
                    ? value
                    : 0u;

            set => SetValue(
                "ScreenshotHotkeyVk",
                value.ToString());
        }
        public uint ToggleWindowModifiers
        {
            get => uint.TryParse(GetValue("ToggleWindowModifiers"), out var value) ? value : 2; // Varsayılan: MOD_CONTROL (2)
            set => SetValue("ToggleWindowModifiers", value.ToString());
        }

        public uint ToggleWindowVk
        {
            get => uint.TryParse(GetValue("ToggleWindowVk"), out var value) ? value : 192; // Varsayılan: ` (É / tilde tuşu) veya istediğin bir tuş kodu
            set => SetValue("ToggleWindowVk", value.ToString());
        }
        public bool AlwaysOnTop
        {
            get => !bool.TryParse(GetValue("AlwaysOnTop"), out var value) ? true : value;

            set => SetValue("AlwaysOnTop", value.ToString());
        }

        public bool LaunchAtStartup
        {
            get => bool.TryParse(GetValue("LaunchAtStartup"), out var value) && value;

            set => SetValue("LaunchAtStartup", value.ToString());
        }
        public uint ToggleWindowHotkeyModifiers
        {
            get => uint.TryParse(GetValue("ToggleWindowModifiers"), out var value) ? value : 0u;
            set => SetValue("ToggleWindowModifiers", value.ToString());
        }

        public uint ToggleWindowHotkeyVk
        {
            get => uint.TryParse(GetValue("ToggleWindowVk"), out var value) ? value : 0u;
            set => SetValue("ToggleWindowVk", value.ToString());
        }

        // -----------------------------
        // SECURE SETTINGS
        // -----------------------------

        public string DeepLApiKey
        {
            get => SecureStorage.Load("DeepLApiKey") ?? "";
            set => SecureStorage.Save("DeepLApiKey", value);
        }

        public string GoogleCloudApiKey
        {
            get => SecureStorage.Load("GoogleCloudApiKey") ?? "";
            set => SecureStorage.Save("GoogleCloudApiKey", value);
        }

        public string AzureApiKey
        {
            get => SecureStorage.Load("AzureApiKey") ?? "";
            set => SecureStorage.Save("AzureApiKey", value);
        }

        // -----------------------------
        // JSON
        // -----------------------------

        private void SaveJson()
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(_jsonPath)!);

                    var json =
                        JsonSerializer.Serialize(
                            _cache,
                            new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });

                    var tempPath =
                        _jsonPath + ".tmp";

                    File.WriteAllText(
                        tempPath,
                        json);

                    File.Copy(
                        tempPath,
                        _jsonPath,
                        true);

                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("SaveJson failed", ex);
            }
        }

        private void LoadJson()
        {
            try
            {
                if (!File.Exists(_jsonPath))
                    return;

                var json =
                    File.ReadAllText(_jsonPath);

                var data =
                    JsonSerializer.Deserialize<
                        Dictionary<string, string>>(json);

                if (data == null)
                    return;

                _cache.Clear();

                foreach (var kv in data)
                    _cache[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                Logger.Log("LoadJson failed", ex);
            }
        }

        // -----------------------------
        // LOAD
        // -----------------------------

        public static AppSettings Load()
        {
            if (_instance != null)
                return _instance;

            _instance = new AppSettings();

            if (!_isPackaged)
                _instance.LoadJson();

            return _instance;
        }

        public void Save()
        {
            if (!_isPackaged)
                SaveJson();
        }

        public bool HideOnCapture
        {
            get => !bool.TryParse(GetValue("HideOnCapture"), out var value) ? true : value;
            set => SetValue("HideOnCapture", value.ToString());
        }

    }
}
