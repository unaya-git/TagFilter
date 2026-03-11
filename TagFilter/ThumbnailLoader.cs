using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace TagFilter
{
    public static class ThumbnailLoader
    {
        public static BitmapImage Load(string path, int decodeWidth = 196)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"ファイルが見つかりません: {path}");

            // OneDriveファイルを強制ローカル化
            FileHelper.EnsureLocal(path);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = decodeWidth;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}