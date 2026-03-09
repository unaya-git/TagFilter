using System.Windows.Media.Imaging;

namespace TagFilter
{
    public static class ThumbnailLoader
    {
        public static BitmapImage Load(string path, int decodeWidth = 200)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new System.Uri(path);
            bmp.DecodePixelWidth = decodeWidth; // メモリ節約
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze(); // 他スレッドから参照可能にする
            return bmp;
        }
    }
}