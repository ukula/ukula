using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace UkulaApp
{
    public static class ThemeHelper
    {
        // =========================
        // ALPHA SETTINGS
        // =========================

        public static byte AccentAlpha = 255;

        public static byte Light1Alpha = 255;
        public static byte Light2Alpha = 255;
        public static byte Light3Alpha = 255;

        public static byte Dark1Alpha = 60;
        public static byte Dark2Alpha = 120;
        public static byte Dark3Alpha = 230;

        // =========================
        // ACCENT
        // =========================

        public static SolidColorBrush AccentBrush(Color c)
        {
            return new SolidColorBrush(
                Color.FromArgb(
                    AccentAlpha,
                    c.R,
                    c.G,
                    c.B));
        }

        // =========================
        // LIGHT
        // =========================

        public static SolidColorBrush Light1Brush(Color c)
        {
            return new SolidColorBrush(
                Color.FromArgb(
                    Light1Alpha,
                    c.R,
                    c.G,
                    c.B));
        }

        public static SolidColorBrush Light2Brush(Color c)
        {
            return new SolidColorBrush(
                Color.FromArgb(
                    Light2Alpha,
                    c.R,
                    c.G,
                    c.B));
        }

        public static SolidColorBrush Light3Brush(Color c)
        {
            return new SolidColorBrush(
                Color.FromArgb(
                    Light3Alpha,
                    c.R,
                    c.G,
                    c.B));
        }

        // =========================
        // DARK
        // =========================

        public static SolidColorBrush Dark1Brush(Color c)
        {
            return new SolidColorBrush(
                Color.FromArgb(
                    Dark1Alpha,
                    c.R,
                    c.G,
                    c.B));
        }

        public static SolidColorBrush Dark2Brush(Color c)
        {
            return new SolidColorBrush(
                Color.FromArgb(
                    Dark2Alpha,
                    c.R,
                    c.G,
                    c.B));
        }

        public static SolidColorBrush Dark3Brush(Color c)
        {
            return new SolidColorBrush(
                Color.FromArgb(
                    Dark3Alpha,
                    c.R,
                    c.G,
                    c.B));
        }
    }
}