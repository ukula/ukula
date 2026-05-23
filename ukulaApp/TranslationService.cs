using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using GTranslate.Translators;

namespace UkulaApp
{
    public enum TranslationProvider { Google, DeepL, GoogleCloud, Azure }

    public class TranslationResult
    {
        public string TranslatedText { get; set; } = string.Empty;
        public string DetectedLanguage { get; set; } = "";
        public string Warning { get; set; } = "";
    }

    public static class TranslationService
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        private static readonly GoogleTranslator _googleTranslator = new();

        public static string TargetLanguageCode { get; set; } = "tr";
        public static TranslationProvider CurrentProvider { get; set; } = TranslationProvider.Google;
        public static string DeepLApiKey { get; set; } = "";
        public static string GoogleCloudApiKey { get; set; } = "";
        public static string AzureApiKey { get; set; } = "";
        public static string AzureRegion { get; set; } = "";

        public static readonly Dictionary<string, string> SupportedLanguages = new()
        {
            { "Türkçe", "tr" }, { "English", "en" }, { "Deutsch", "de" }, { "Français", "fr" },
            { "Español", "es" }, { "Italiano", "it" }, { "Русский", "ru" }, { "日本語", "ja" },
            { "中文", "zh" }, { "한국어", "ko" }, { "العربية", "ar" }, { "Português", "pt" },
            { "Polski", "pl" }, { "Українська", "uk" }
        };

        public static async Task<TranslationResult> TranslateAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new TranslationResult { TranslatedText = "", DetectedLanguage = ""};

            Logger.Log($"TranslateAsync provider: {CurrentProvider}");
            return CurrentProvider switch
            {
                TranslationProvider.DeepL => string.IsNullOrWhiteSpace(DeepLApiKey)
                    ? new TranslationResult { TranslatedText = "", DetectedLanguage = "", Warning = "DeepLNoKey" }
                    : await TranslateWithDeepL(text),

                TranslationProvider.GoogleCloud => string.IsNullOrWhiteSpace(GoogleCloudApiKey)
                    ? new TranslationResult { TranslatedText = "", DetectedLanguage = "", Warning = "GoogleCloudNoKey" }
                    : await TranslateWithGoogleCloud(text),

                TranslationProvider.Azure => string.IsNullOrWhiteSpace(AzureApiKey)
                    ? new TranslationResult { TranslatedText = "", DetectedLanguage = "", Warning = "AzureNoKey" }
                    : await TranslateWithAzure(text),

                _ => await TranslateWithGoogle(text)
            };
        }



        private static async Task<TranslationResult> TranslateWithGoogle(string text)
        {
            try
            {
                var result = await _googleTranslator.TranslateAsync(text, TargetLanguageCode);
                return new TranslationResult
                {
                    DetectedLanguage = result.SourceLanguage.Name,
                    TranslatedText = result.Translation
                };
            }
            catch (Exception ex)
            {
                 Logger.Log(ex);
                 return new TranslationResult { TranslatedText = "Google Hatası!", DetectedLanguage = "Hata" }; 
            }
        }

        private static async Task<TranslationResult> TranslateWithDeepL(string text)
        {
            try
            {
                var endpoint = DeepLApiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-free.deepl.com/v2/translate"
                    : "https://api.deepl.com/v2/translate";

                var body = JsonSerializer.Serialize(new
                {
                    text = new[] { text },
                    target_lang = GetDeepLTargetLanguageCode(TargetLanguageCode)
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("Authorization", $"DeepL-Auth-Key {DeepLApiKey}");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);

                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Log(
                        $"DeepL HTTP {(int)response.StatusCode}: {responseText}",
                        LogLevel.Warning);

                    throw new Exception(
                        $"DeepL request failed: {(int)response.StatusCode}");
                }

                using var doc = JsonDocument.Parse(responseText);

                var translation =
                    doc.RootElement
                       .GetProperty("translations")[0];

                return new TranslationResult
                {
                    DetectedLanguage =
                        translation.GetProperty("detected_source_language")
                                   .GetString() ?? "EN",

                    TranslatedText =
                        translation.GetProperty("text")
                                   .GetString() ?? text,
                };
            }
            catch (Exception ex)
            {
                Logger.Log("DeepL failed", ex);

                return new TranslationResult
                {
                    TranslatedText = "",
                    DetectedLanguage = "Hata",
                Warning = "DeepLError"
                };
            }
        }

        private static string GetDeepLTargetLanguageCode(string languageCode)
        {
            return languageCode.ToLowerInvariant() switch
            {
                "en" => "EN-US",
                "pt" => "PT-PT",
                "zh" => "ZH-HANS",
                _ => languageCode.ToUpperInvariant()
            };
        }

        private static async Task<TranslationResult> TranslateWithGoogleCloud(string text)
        {
            return await TranslateWithGoogleCloudV2(text);
        }

        private static async Task<TranslationResult> TranslateWithGoogleCloudV2(string text)
        {
            try
            {
                Logger.Log("Google Cloud v2 deneniyor");

                var url =
                    $"https://translation.googleapis.com/language/translate/v2?key={GoogleCloudApiKey}";

                var body = JsonSerializer.Serialize(new
                {
                    q = text,
                    target = TargetLanguageCode,
                    format = "text"
                });

                var response = await _http.PostAsync(
                    url,
                    new StringContent(body, Encoding.UTF8, "application/json"));

                var responseText = await response.Content.ReadAsStringAsync();

                Logger.Log(
                    $"GoogleCloud HTTP {(int)response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Log(
                        $"GoogleCloud HTTP {(int)response.StatusCode}",
                        LogLevel.Warning);

                    throw new Exception(
                        $"Google Cloud request failed: {(int)response.StatusCode}");
                }

                using var doc = JsonDocument.Parse(responseText);

                var translation = doc.RootElement
                    .GetProperty("data")
                    .GetProperty("translations")[0];

                return new TranslationResult
                {
                    DetectedLanguage =
                        translation.TryGetProperty("detectedSourceLanguage", out var lang)
                            ? lang.GetString() ?? ""
                            : "",

                    TranslatedText =
                        translation.GetProperty("translatedText").GetString() ?? text,

                };
            }
            catch (Exception ex)
            {
                Logger.Log("Google Cloud v2 failed", ex);

                return new TranslationResult
                {
                    TranslatedText = "",
                    DetectedLanguage = "Hata",
                    Warning = "GoogleCloudV2Error"
                };
            }
        }
        private static async Task<TranslationResult> TranslateWithAzure(string text)
        {
            try
            {
                var url = $"https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to={TargetLanguageCode}";
                //var url = $"https://ukula.cognitiveservices.azure.com/translator/text/v3.0/translate?api-version=3.0&to={TargetLanguageCode}";
                var body = JsonSerializer.Serialize(new[]
                {
                    new { Text = text }
                });
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Ocp-Apim-Subscription-Key", AzureApiKey);
                request.Headers.Add("Ocp-Apim-Subscription-Region", AzureRegion);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) throw new Exception();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement[0];
                var detected = root.GetProperty("detectedLanguage").GetProperty("language").GetString() ?? "";
                var translated = root.GetProperty("translations")[0].GetProperty("text").GetString() ?? text;
                return new TranslationResult
                {
                    DetectedLanguage = detected,
                    TranslatedText = translated,
                };
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return new TranslationResult
                {
                    TranslatedText = "",
                    DetectedLanguage = "Hata",
                    Warning = "AzureError"
                };
            }
        }
    }
}
