using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Common;
using Tesseract;
using System.Text.RegularExpressions;

namespace UkulaApp
{
    public static class OcrManager
    {
        private static readonly string TessDataPath =
            Path.Combine(AppContext.BaseDirectory, "tessdata");

        private const string TessLanguages =
            "ukr+tur+spa+rus+por+pol+kor+jpn+ita+fra+eng+deu+chi_sim+ara";
        public static string CurrentEngine = "Windows";
        public static async Task<string> RecognizeTextAsync(SoftwareBitmap bitmap)
        {
            if (bitmap == null) return "";

            SoftwareBitmap? convertedBitmap = null;

            try
            {
                var workBitmap = bitmap;
                if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                    bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                {
                    convertedBitmap = SoftwareBitmap.Convert(
                        bitmap,
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied);
                    workBitmap = convertedBitmap;
                }

                SoftwareBitmap ocrBitmap;

                if (CurrentEngine == "Tesseract")
                {
                    ocrBitmap = PrepareForOcr(workBitmap);
                }
                else
                {
                    ocrBitmap = workBitmap;
                }

                string ocrText;

                if (CurrentEngine == "Tesseract")
                {
                    ocrText =
                        RecognizeWithTesseract(ocrBitmap);
                }
                else
                {
                    ocrText =
                        await RecognizeWithWindowsOcr(ocrBitmap);
                }

                ocrText = MergeLinesForTranslation(ocrText);

                var codeResults = DecodeBarcodes(ocrBitmap);

                if (ocrBitmap != workBitmap)
                {
                    ocrBitmap.Dispose();
                }

                //@@@OCR-DEBUG@@@

                //await SaveDebugImageAsync(ocrBitmap);

                //@@@OCR-DEBUG@@@

                if (codeResults.Length == 0)
                    return ocrText;

                var codeText = string.Join(
                    Environment.NewLine,
                    codeResults.Select(r => $"{r.BarcodeFormat}: {r.Text}"));

                return string.IsNullOrWhiteSpace(ocrText)
                    ? codeText
                    : $"{ocrText}{Environment.NewLine}{codeText}";
            }
            finally
            {
                convertedBitmap?.Dispose();
                bitmap.Dispose();
            }
        }


        // Windows OCR
        private static async Task<string> RecognizeWithWindowsOcr(
            SoftwareBitmap bitmap)
        {
            try
            {
                var engine =
                    OcrEngine.TryCreateFromUserProfileLanguages();

                if (engine == null)
                    return "";

                var result =
                    await engine.RecognizeAsync(bitmap);

                return result.Text?.Trim() ?? "";
            }
            catch (Exception ex)
            {
                Logger.Log("Windows OCR failed", ex);
                return "";
            }
        }

        // Tesseract OCR
        private static string RecognizeWithTesseract(SoftwareBitmap bitmap)
        {
            try
            {
                // SoftwareBitmap PNG byte dizisine çevir
                var pngBytes = SoftwareBitmapToPngBytes(bitmap);
                if (pngBytes == null || pngBytes.Length == 0) return "";

                using var engine = new TesseractEngine(
                    TessDataPath,
                    TessLanguages,
                    EngineMode.Default);

                // Tesseract için ayar
                engine.SetVariable("tessedit_pageseg_mode", "3"); // Tam sayfa otomatik layout
                engine.SetVariable("preserve_interword_spaces", "1");

                using var pix = Pix.LoadFromMemory(pngBytes);
                using var page = engine.Process(pix);

                return page.GetText()?.Trim() ?? "";
            }
            catch (Exception ex)
            {
                Logger.Log("Tesseract OCR failed", ex);
                return "";
            }
        }
        private static string MergeLinesForTranslation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 1. İşlemi kolaylaştırmak için Windows tarzı (\r\n) satır sonlarını standart (\n) yapalım
            text = text.Replace("\r\n", "\n");

            // 2. Satır sonu hecelemelerini düzelt (örneğin "para-\ngraph" -> "paragraph")
            text = Regex.Replace(text, @"-\n+", "");

            // 3. Tekli satır sonlarını boşluğa çevir, ama paragrafları (\n\n) koru.
            // (?<!\n)\n(?!\n) : Öncesinde ve sonrasında başka \n olmayan, tek başına duran \n'leri bulur.
            text = Regex.Replace(text, @"(?<!\n)\n(?!\n)", " ");

            // 4. Çift satır sonlarını (\n\n) düzgün bir paragrafa oturt, fazlalıkları sil
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            // 5. Yan yana kalmış gereksiz çift boşlukları tek boşluğa düşür
            text = Regex.Replace(text, @"[ ]{2,}", " ");

            return text.Trim();
        }
        private static byte[]? SoftwareBitmapToPngBytes(SoftwareBitmap bitmap)
        {
            try
            {
                using var ms = new MemoryStream();
                var streamTask = Task.Run(async () =>
                {
                    var ras = ms.AsRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(
                        BitmapEncoder.PngEncoderId, ras);
                    encoder.SetSoftwareBitmap(bitmap);
                    await encoder.FlushAsync();
                });
                streamTask.GetAwaiter().GetResult();
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Log("PNG conversion failed", ex);
                return null;
            }
        }

        //@@@OCR-DEBUG@@@
        private static async Task SaveDebugImageAsync(SoftwareBitmap bitmap){ 
        
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "UkulaDebug");

                Directory.CreateDirectory(folder);

                string path = Path.Combine(
                    folder,
                    $"ocr_debug_{DateTime.Now:HHmmssfff}.png");

                using var stream = File.OpenWrite(path);

                var encoder = await BitmapEncoder.CreateAsync(
                    BitmapEncoder.PngEncoderId,
                    stream.AsRandomAccessStream());

                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
            }
            catch (Exception ex)
            {
                Logger.Log("Debug image save failed", ex);
            }
        }
        //@@@OCR-DEBUG@@@


        // Görüntü hazırlama scale + grayscale + Otsu threshold
        private static SoftwareBitmap PrepareForOcr(SoftwareBitmap bitmap)
        {
            //Scale hesapla
            var scale = GetOcrScale(bitmap.PixelWidth, bitmap.PixelHeight);
            var targetWidth = Math.Max(1, (int)Math.Round(bitmap.PixelWidth * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(bitmap.PixelHeight * scale));

            var sourcePixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyToBuffer(sourcePixels.AsBuffer());

            //Bilinear interpolasyon + grayscale
            var grayPixels = new double[targetWidth * targetHeight];

            for (var y = 0; y < targetHeight; y++)
            {
                double sourceY = y / scale;
                int y1 = Math.Clamp((int)Math.Floor(sourceY), 0, bitmap.PixelHeight - 1);
                int y2 = Math.Clamp(y1 + 1, 0, bitmap.PixelHeight - 1);
                double yWeight = sourceY - y1;

                for (var x = 0; x < targetWidth; x++)
                {
                    double sourceX = x / scale;
                    int x1 = Math.Clamp((int)Math.Floor(sourceX), 0, bitmap.PixelWidth - 1);
                    int x2 = Math.Clamp(x1 + 1, 0, bitmap.PixelWidth - 1);
                    double xWeight = sourceX - x1;

                    double Lum(int idx) =>
                        (0.114 * sourcePixels[idx]) +
                        (0.587 * sourcePixels[idx + 1]) +
                        (0.299 * sourcePixels[idx + 2]);

                    int idx11 = ((y1 * bitmap.PixelWidth) + x1) * 4;
                    int idx12 = ((y1 * bitmap.PixelWidth) + x2) * 4;
                    int idx21 = ((y2 * bitmap.PixelWidth) + x1) * 4;
                    int idx22 = ((y2 * bitmap.PixelWidth) + x2) * 4;

                    double top = (Lum(idx11) * (1 - xWeight)) + (Lum(idx12) * xWeight);
                    double bottom = (Lum(idx21) * (1 - xWeight)) + (Lum(idx22) * xWeight);

                    grayPixels[(y * targetWidth) + x] =
                        (top * (1 - yWeight)) + (bottom * yWeight);
                }
            }

            //Koyu tema tespiti
            double imageMean = 0;
            for (int i = 0; i < grayPixels.Length; i++)
                imageMean += grayPixels[i];
            imageMean /= grayPixels.Length;
            bool isDarkTheme = imageMean < 128;

            //Otsu threshold
            double otsuThreshold = ComputeOtsuThreshold(grayPixels);

            var targetPixels = new byte[targetWidth * targetHeight * 4];

            for (var y = 0; y < targetHeight; y++)
            {
                for (var x = 0; x < targetWidth; x++)
                {
                    double pixel = grayPixels[(y * targetWidth) + x];
                    bool isBackground = pixel <= otsuThreshold;

                    // Koyu tema: metin açık > eşiğin üstü siyah (invert)
                    // Açık tema: metin koyu > eşiğin altı siyah (normal)
                    byte value = (isBackground == isDarkTheme) ? (byte)255 : (byte)0;

                    int ti = ((y * targetWidth) + x) * 4;
                    targetPixels[ti] = value;
                    targetPixels[ti + 1] = value;
                    targetPixels[ti + 2] = value;
                    targetPixels[ti + 3] = 255;
                }
            }

            var prepared = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8,
                targetWidth,
                targetHeight,
                BitmapAlphaMode.Premultiplied);

            prepared.CopyFromBuffer(targetPixels.AsBuffer());
            return prepared;
        }

        // Barkod
        private static Result[] DecodeBarcodes(SoftwareBitmap bitmap)
        {
            try
            {
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyToBuffer(pixels.AsBuffer());

                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = true,
                        TryInverted = true,
                        PossibleFormats = new[]
                        {
                            BarcodeFormat.QR_CODE,
                            BarcodeFormat.DATA_MATRIX,
                            BarcodeFormat.AZTEC,
                        }
                    }
                };

                var luminance = new RGBLuminanceSource(
                    pixels,
                    bitmap.PixelWidth,
                    bitmap.PixelHeight,
                    RGBLuminanceSource.BitmapFormat.BGRA32);

                return reader.DecodeMultiple(luminance) ?? Array.Empty<Result>();
            }
            catch (Exception ex)
            {
                Logger.Log("Barcode decode failed", ex);
                return Array.Empty<Result>();
            }
        }

        //Yardımcı metodlar
        //Otsu yöntemiyle global threshold hesaplar.
        //Histogram tabanlı; sınıflar arası varyansı maksimize eden eşiği döndürür.
        private static double ComputeOtsuThreshold(double[] grayPixels)
        {
            var histogram = new int[256];
            foreach (var p in grayPixels)
                histogram[(int)Math.Clamp(p, 0, 255)]++;

            int total = grayPixels.Length;
            double sum = 0;
            for (int i = 0; i < 256; i++)
                sum += i * histogram[i];

            double sumB = 0, wB = 0;
            double maxVariance = 0, threshold = 128;

            for (int i = 0; i < 256; i++)
            {
                wB += histogram[i];
                if (wB == 0) continue;

                double wF = total - wB;
                if (wF == 0) break;

                sumB += i * histogram[i];
                double mB = sumB / wB;
                double mF = (sum - sumB) / wF;
                double variance = wB * wF * (mB - mF) * (mB - mF);

                if (variance > maxVariance)
                {
                    maxVariance = variance;
                    threshold = i;
                }
            }

            return threshold;
        }

        //Sürekli scale faktörü hesaplar.
        //Hedef: en uzun kenar ~1500px
        private static double GetOcrScale(int width, int height)
        {
            const int targetDim = 1500;
            var maxDim = Math.Max(width, height);

            if (maxDim <= 0) return 1.0;

            double upscale = (double)targetDim / maxDim;

            //4x'ten fazla büyütme bulanıklık yaratır
            return Math.Min(upscale, 4.0);
        }
    }
}