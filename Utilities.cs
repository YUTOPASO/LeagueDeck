using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;

namespace LeagueDeck
{
    public static class Utilities
    {
        #region vars

        private static readonly ColorMatrix grayscaleMatrix = new ColorMatrix(
            new float[][] {
                new float[] { .3f, .3f, .3f, 0, 0 },
                new float[] { .59f, .59f, .59f, 0, 0 },
                new float[] { .11f, .11f, .11f, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });

        private const string cUpdateImageName = "updating@2x.png";
        private const string cCheckMarkImageName = "check@2x.png";
        private static readonly string _pluginImageFolder = Path.Combine(Environment.CurrentDirectory, "Images", "Plugin");
        private static Image _updateImage;
        private static Image _checkMarkImage;

        public static bool InputRunning { get; private set; }

        #endregion

        #region Public Methods

        public static Image GetUpdateImage()
        {
            if (_updateImage == null)
                _updateImage = Image.FromFile(Path.Combine(_pluginImageFolder, cUpdateImageName));

            lock (_updateImage)
            {
                return (Image)_updateImage.Clone();
            }
        }

        public static Image GetCheckMarkImage()
        {
            if (_checkMarkImage == null)
                _checkMarkImage = Image.FromFile(Path.Combine(_pluginImageFolder, cCheckMarkImageName));

            lock (_checkMarkImage)
            {
                return (Image)_checkMarkImage.Clone();
            }
        }

        public static Image AddChampionToSpellImage(Image spellImage, Image championImage)
        {
            Bitmap image = (Bitmap)spellImage.Clone();

            using (var g = Graphics.FromImage(image))
            {
                var bounds = new Rectangle(0, 0, 32, 32);
                g.DrawImage(championImage, bounds);
            }

            return image;
        }

        public static Image AddCheckMarkToImage(Image source)
        {
            Bitmap image = (Bitmap)source.Clone();

            using (var g = Graphics.FromImage(image))
            {
                var bounds = new Rectangle(0, 0, source.Width, source.Height);
                var check = GetCheckMarkImage();
                g.DrawImage(check, bounds);
            }

            return image;
        }

        public static Image GrayscaleImage(Image source)
        {
            Bitmap grayscaled = new Bitmap(source.Width, source.Height);

            using (Graphics g = Graphics.FromImage(grayscaled))
            {
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(grayscaleMatrix);
                    g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                }
            }

            return grayscaled;
        }

        public static void SendMessageInChat(string message)
        {
            InputRunning = true;

            InputSimulator iis = new InputSimulator();

            // open chat
            iis.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.RETURN);

            Thread.Sleep(20);

            // enter message
            iis.Keyboard.TextEntry(message);

            // fixes the chat not closing, thanks Timmy
            Thread.Sleep(20);

            // send message
            iis.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.RETURN);

            InputRunning = false;
        }

        public static async Task<string> GetApiResponse(string url, CancellationToken ct)
        {
            HttpWebResponse response = null;
            int retries = 0;
            while (response == null)
            {
                try
                {
                    if (ct.IsCancellationRequested)
                        return string.Empty;

                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Timeout = 3000;

                    // accept all SSL certificates
                    request.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

                    response = (HttpWebResponse)await request.GetResponseAsync();
                }
                catch
                {
                    retries++;
                    if (retries >= 10)
                        throw new TimeoutException($"Failed to get API response from {url} after {retries} retries");
                    await Task.Delay(500, ct);
                }
            }

            using (var stream = response.GetResponseStream())
            {
                using (var sr = new StreamReader(stream))
                {
                    return sr.ReadToEnd();
                }
            };
        }

        private static readonly string _iconFolder = Path.Combine(Environment.CurrentDirectory, "Images", "Icons");

        /// <summary>
        /// Loads an icon from Images/Icons folder, resizes to 144x144 with black background.
        /// </summary>
        public static Image LoadIcon(string filename)
        {
            var path = Path.Combine(_iconFolder, filename);
            if (!File.Exists(path))
                return null;

            var bmp = new Bitmap(144, 144);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Black);

                using (var src = Image.FromFile(path))
                {
                    // Fit image within 144x144, centered
                    float scale = Math.Min(130f / src.Width, 130f / src.Height);
                    int w = (int)(src.Width * scale);
                    int h = (int)(src.Height * scale);
                    int x = (144 - w) / 2;
                    int y = (144 - h) / 2;
                    g.DrawImage(src, x, y, w, h);
                }
            }

            return bmp;
        }

        /// <summary>
        /// Generates a 144x144 icon with a colored background circle and text/symbol.
        /// </summary>
        public static Image GenerateIcon(string text, Color bgColor, Color textColor = default(Color))
        {
            if (textColor == default(Color))
                textColor = Color.White;

            var bmp = new Bitmap(144, 144);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Black);

                // Draw filled circle
                using (var brush = new SolidBrush(bgColor))
                {
                    g.FillEllipse(brush, 8, 8, 128, 128);
                }

                // Draw text centered
                float fontSize = text.Length <= 2 ? 48f : (text.Length <= 4 ? 32f : 22f);
                using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold))
                using (var brush = new SolidBrush(textColor))
                {
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(text, font, brush, new RectangleF(0, 0, 144, 144), sf);
                }
            }

            return bmp;
        }

        /// <summary>
        /// Converts an Image to a base64 string suitable for SetImageAsync.
        /// </summary>
        public static string ImageToBase64(Image image)
        {
            using (var ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Png);
                var base64 = Convert.ToBase64String(ms.ToArray());
                return "data:image/png;base64," + base64;
            }
        }

        #endregion
    }
}
