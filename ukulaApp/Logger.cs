using System;
using System.IO;
using System.Text.RegularExpressions;

namespace UkulaApp
{
    public enum LogLevel
    {
        Verbose,
        Warning,
        Error
    }

    public static class Logger
    {
        private static readonly object LogLock = new();

        private static readonly string LogDir =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Ukula");

        private static readonly string LogFile =
            Path.Combine(LogDir, "app.log");

        private const long MaxLogSize = 2 * 1024 * 1024; // 2 MB
        private const int MaxLogFiles = 3;

        public static void Log(string message, LogLevel level = LogLevel.Verbose)
        {
            if (!ShouldWrite(level))
                return;

            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(LogDir);
                    RotateLogsIfNeeded();

                    File.AppendAllText(
                        LogFile,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] " +
                        $"{Sanitize(message)}\n");
                }
            }
            catch
            {
            }
        }

        public static void Log(string message, Exception ex)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(LogDir);
                    RotateLogsIfNeeded();

                    File.AppendAllText(
                        LogFile,
                        $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Error] " +
                        $"{Sanitize(message)}\n" +
                        $"{Sanitize(ex.ToString())}\n");
                }
            }
            catch
            {
            }
        }

        public static void Log(Exception ex)
        {
            Log("Exception", ex);
        }

        private static bool ShouldWrite(LogLevel level)
        {
#if DEBUG
            return true;
#else
            return level != LogLevel.Verbose;
#endif
        }

        private static void RotateLogsIfNeeded()
        {
            var logInfo = new FileInfo(LogFile);

            if (!logInfo.Exists || logInfo.Length < MaxLogSize)
                return;

            var oldestLog = GetRotatedLogPath(MaxLogFiles - 1);

            if (File.Exists(oldestLog))
                File.Delete(oldestLog);

            for (var i = MaxLogFiles - 2; i >= 1; i--)
            {
                var source = GetRotatedLogPath(i);
                var destination = GetRotatedLogPath(i + 1);

                if (File.Exists(source))
                    File.Move(source, destination);
            }

            File.Move(LogFile, GetRotatedLogPath(1));
        }

        private static string GetRotatedLogPath(int index)
        {
            return Path.Combine(LogDir, $"app_{index}.log");
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // DeepL auth_key
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"auth_key=[^&\s]+",
                "auth_key=***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Azure subscription key
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"Ocp-Apim-Subscription-Key:\s*[^\r\n]+",
                "Ocp-Apim-Subscription-Key: ***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // DeepL authorization header
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"DeepL-Auth-Key\s+[^\r\n\s]+",
                "DeepL-Auth-Key ***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Bearer tokens
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*",
                "Bearer ***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Generic api key / token / secret
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"(?i)(api[_-]?key|token|secret)\s*[:=]\s*[""']?[^""'\s]+",
                "$1=***");

            return text;
        }
    }
}
