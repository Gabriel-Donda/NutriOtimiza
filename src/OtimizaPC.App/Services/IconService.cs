using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace OtimizaPC.App.Services
{
    /// <summary>Carrega ícones de executáveis e imagens sem repetir trabalho nem manter arquivos abertos.</summary>
    public static class IconService
    {
        private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new ConcurrentDictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        public static BitmapSource FromExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return Cache.GetOrAdd(path, p =>
            {
                try { using (var icon = Icon.ExtractAssociatedIcon(p)) return icon == null ? null : ToBitmapSource(icon); }
                catch { return null; }
            });
        }
        public static BitmapSource FromImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return Cache.GetOrAdd(path, p =>
            {
                try { var image = new BitmapImage(new Uri(p, UriKind.Absolute)); image.Freeze(); return image; }
                catch { return null; }
            });
        }
        private static BitmapSource ToBitmapSource(Icon icon)
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32)); source.Freeze(); return source;
        }
    }
}
