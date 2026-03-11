using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TagFilter
{
    public static class FileHelper
    {
        // OneDriveの「オンラインのみ」ファイルをローカルに強制ダウンロード
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetFileAttributes(string lpFileName, uint dwFileAttributes);

        private const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        /// <summary>
        /// OneDriveのオンライン専用ファイルをローカルに落とす。
        /// 通常ファイルは何もしない。
        /// </summary>
        public static void EnsureLocal(string path)
        {
            if (!File.Exists(path)) return;

            var attr = File.GetAttributes(path);

            // ReparsePoint = OneDriveプレースホルダーの可能性
            if ((attr & FileAttributes.ReparsePoint) == 0) return;

            // ファイルを実際に読むことでOneDriveに強制ダウンロードさせる
            try
            {
                using (var fs = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.Read,
                    bufferSize: 1, // 1バイト読むだけでOK
                    FileOptions.SequentialScan))
                {
                    fs.ReadByte();
                }
            }
            catch (IOException ex) when (
                (uint)ex.HResult == 0x8007016A ||  // クラウドファイルプロバイダーが実行されていません
                (uint)ex.HResult == 0x80070194)    // クラウドファイルが無効
            {
                throw new IOException(
                    $"OneDriveファイルをダウンロードできませんでした。\n" +
                    $"OneDriveが起動しているか確認してください。\n" +
                    $"またはファイルを右クリック→「常にこのデバイスに保存」で\n" +
                    $"ローカルに保存してから再度お試しください。\n\n" +
                    $"ファイル: {path}", ex);
            }
        }

        /// <summary>
        /// フォルダ内の全ファイルがローカルにあるか確認
        /// </summary>
        public static bool IsOnlineOnly(string path)
        {
            if (!File.Exists(path)) return false;
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.ReparsePoint) != 0;
        }
    }
}