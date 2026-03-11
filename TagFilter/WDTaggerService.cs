using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TagFilter
{
    // ── モデルプリセット ────────────────────────────────────────────
    public class WD14ModelPreset
    {
        public string Name { get; }  // 表示名
        public string OnnxFile { get; }  // ローカル保存ファイル名
        public string CsvFile { get; }  // ローカル保存CSVファイル名
        public string ModelUrl { get; }
        public string CsvUrl { get; }
        public string Description { get; }

        public WD14ModelPreset(string name, string onnxFile, string csvFile,
                               string modelUrl, string csvUrl, string description)
        {
            Name = name;
            OnnxFile = onnxFile;
            CsvFile = csvFile;
            ModelUrl = modelUrl;
            CsvUrl = csvUrl;
            Description = description;
        }

        public string OnnxPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, OnnxFile);
        public string CsvPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CsvFile);


        public static readonly WD14ModelPreset[] All = new[]
{
    new WD14ModelPreset(
        "WD ViT v2（標準・軽量）",
        "wd-vit-v2.onnx",
        "wd-vit-v2.csv",
        "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/model.onnx",
        "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/selected_tags.csv",
        "汎用。アニメ向け標準モデル（約365MB）"),

    new WD14ModelPreset(
        "WD SwinV2 v2（高精度）",
        "wd-swinv2-v2.onnx",
        "wd-swinv2-v2.csv",
        "https://huggingface.co/SmilingWolf/wd-v1-4-swinv2-tagger-v2/resolve/main/model.onnx",
        "https://huggingface.co/SmilingWolf/wd-v1-4-swinv2-tagger-v2/resolve/main/selected_tags.csv",
        "v2高精度版（約365MB）"),

    new WD14ModelPreset(
        "WD ViT v3（実写向け改善）",
        "wd-vit-v3.onnx",
        "wd-vit-v3.csv",
        "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/model.onnx",
        "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/selected_tags.csv",
        "v3。実写にも比較的対応（約365MB）"),

    new WD14ModelPreset(
        "WD ViT Large v3（高精度）",
        "wd-vit-large-v3.onnx",
        "wd-vit-large-v3.csv",
        "https://huggingface.co/SmilingWolf/wd-vit-large-tagger-v3/resolve/main/model.onnx",
        "https://huggingface.co/SmilingWolf/wd-vit-large-tagger-v3/resolve/main/selected_tags.csv",
        "v3大型モデル。高精度だが低速（約1.2GB）"),

    new WD14ModelPreset(
        "WD Eva02 Large v3（最高精度）",
        "wd-eva02-large-v3.onnx",
        "wd-eva02-large-v3.csv",
        "https://huggingface.co/SmilingWolf/wd-eva02-large-tagger-v3/resolve/main/model.onnx",
        "https://huggingface.co/SmilingWolf/wd-eva02-large-tagger-v3/resolve/main/selected_tags.csv",
        "v3最大モデル。最高精度・最低速（約2.5GB）"),
        };

    }

    public class WD14TaggerService : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly List<TagInfo> _tags;
        private const int IMAGE_SIZE = 448;
        private const float DEFAULT_THRESHOLD = 0.35f;
        private string _inputName;

        // ── Hugging Face ダウンロードURL ───────────────────────────────
        // 後方互換用（デフォルトモデルのURL）
        private const string MODEL_URL =
            "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/model.onnx";
        private const string CSV_URL =
            "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/selected_tags.csv";

        // ── DirectML 使用可否チェック ──────────────────────────────────
        public static bool IsDirectMLAvailable()
        {
            try
            {
                // DirectML はDX12対応GPUがあれば使える（Windows標準）
                // ダミーセッション生成で確認
                using (var opt = new SessionOptions())
                {
                    opt.AppendExecutionProvider_DML(0);
                }
                return true;
            }
            catch { return false; }
        }

        // ── モデルファイルのダウンロード ───────────────────────────────

        /// <summary>プリセット指定でダウンロード</summary>
        public static async Task EnsureModelFilesAsync(
            WD14ModelPreset preset,
            IProgress<(double Ratio, string Message)> progress = null,
            CancellationToken ct = default)
        {
            await EnsureModelFilesAsync(
                preset.OnnxPath, preset.CsvPath,
                preset.ModelUrl, preset.CsvUrl,
                progress, ct);
        }

        /// <summary>
        /// modelPath / csvPath が存在しない場合、Hugging Face から自動ダウンロードする。
        /// progress: 0.0〜1.0 の進捗（モデル50%まで、CSV残り50%）
        /// </summary>
        /// <summary>パス・URL直接指定でダウンロード</summary>
        public static async Task EnsureModelFilesAsync(
            string modelPath,
            string csvPath,
            string modelUrl = MODEL_URL,
            string csvUrl = CSV_URL,
            IProgress<(double Ratio, string Message)> progress = null,
            CancellationToken ct = default)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(30);

                if (!File.Exists(modelPath))
                {
                    progress?.Report((0.0, "モデルをダウンロード中..."));
                    await DownloadFileAsync(client, modelUrl, modelPath,
                        ratio => progress?.Report((ratio * 0.9,
                            $"モデルをダウンロード中... {ratio:P0}")), ct);
                }

                if (!File.Exists(csvPath))
                {
                    progress?.Report((0.9, "タグCSVをダウンロード中..."));
                    await DownloadFileAsync(client, csvUrl, csvPath,
                        ratio => progress?.Report((0.9 + ratio * 0.1,
                            $"タグCSVをダウンロード中... {ratio:P0}")), ct);
                }
            }
            progress?.Report((1.0, "ダウンロード完了"));
        }

        private static async Task DownloadFileAsync(
            HttpClient client,
            string url,
            string destPath,
            Action<double> onProgress,
            CancellationToken ct)
        {
            // 途中で失敗した場合に備えて一時ファイルに書き出す
            var tmpPath = destPath + ".tmp";
            try
            {
                using (var response = await client.GetAsync(url,
                    HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? -1L;

                    using (var srcStream = await response.Content.ReadAsStreamAsync())
                    using (var destStream = File.Create(tmpPath))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        int read;
                        while ((read = await srcStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                        {
                            await destStream.WriteAsync(buffer, 0, read, ct);
                            received += read;
                            if (total > 0) onProgress?.Invoke((double)received / total);
                        }
                    }
                }

                // 正常完了 → 本ファイルに移動
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(tmpPath, destPath);
            }
            catch
            {
                // 失敗時は一時ファイルを削除
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
                throw;
            }
        }

        // ── コンストラクタ ──────────────────────────────────────────────
        public WD14TaggerService(string modelPath, string csvPath,
                                 ExecutionDevice device = ExecutionDevice.Auto)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"モデルファイルが見つかりません: {modelPath}");
            if (!File.Exists(csvPath))
                throw new FileNotFoundException($"CSVファイルが見つかりません: {csvPath}");

            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            bool useGpu = device == ExecutionDevice.Gpu
                       || (device == ExecutionDevice.Auto && IsDirectMLAvailable());

            if (useGpu)
            {
                try { options.AppendExecutionProvider_DML(0); }
                catch { /* DML失敗 → CPUにフォールバック */ }
            }

            _session = new InferenceSession(modelPath, options);
            _inputName = _session.InputMetadata.Keys.First();
            _tags = LoadTags(csvPath);
        }

        // ── 推論 ────────────────────────────────────────────────────────
        public List<PredictedTag> Predict(string imagePath, float threshold = DEFAULT_THRESHOLD)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"画像が見つかりません: {imagePath}");

            // OneDriveファイルを強制ローカル化
            FileHelper.EnsureLocal(imagePath);

            using (var mat = Cv2.ImRead(imagePath, ImreadModes.Unchanged))
            {
                if (mat.Empty())
                    throw new InvalidOperationException($"画像の読み込みに失敗: {imagePath}");

                var tensor = PreprocessImage(mat);
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_inputName, tensor)
                };

                using (var results = _session.Run(inputs))
                {
                    var scores = results.First().AsEnumerable<float>().ToArray();
                    var predicted = new List<PredictedTag>();
                    for (int i = 0; i < _tags.Count && i < scores.Length; i++)
                    {
                        if (scores[i] >= threshold)
                        {
                            predicted.Add(new PredictedTag(
                                new TagEntry(i, _tags[i].Name, (TagCategory)_tags[i].CsvCategory, _tags[i].CsvCategory),
                                scores[i],
                                _tags[i].CsvCategory));
                        }
                    }

                    return predicted.OrderByDescending(p => p.Score).ToList();
                }
            }
        }

        private DenseTensor<float> PreprocessImage(Mat src)
        {
            Mat workMat = src.Channels() == 4
                ? CompositeOnWhiteViaGdi(src)
                : src.Clone();

            using (workMat)
            {
                int size = Math.Max(workMat.Width, workMat.Height);
                using (var padded = new Mat(
                    new OpenCvSharp.Size(size, size),
                    MatType.CV_8UC3,
                    new Scalar(255, 255, 255)))
                {
                    int xOffset = (size - workMat.Width) / 2;
                    int yOffset = (size - workMat.Height) / 2;
                    workMat.CopyTo(padded[new Rect(xOffset, yOffset,
                                                   workMat.Width, workMat.Height)]);
                    using (var resized = new Mat())
                    {
                        Cv2.Resize(padded, resized,
                            new OpenCvSharp.Size(IMAGE_SIZE, IMAGE_SIZE),
                            interpolation: InterpolationFlags.Area);
                        // ── BGR変換なし・OpenCVのBGRをそのまま送る──
                        {
                            var tensor = new DenseTensor<float>(
                                new[] { 1, IMAGE_SIZE, IMAGE_SIZE, 3 });
                            for (int y = 0; y < IMAGE_SIZE; y++)
                                for (int x = 0; x < IMAGE_SIZE; x++)
                                {
                                    var pixel = resized.At<Vec3b>(y, x);
                                    tensor[0, y, x, 0] = pixel.Item0; // B
                                    tensor[0, y, x, 1] = pixel.Item1; // G
                                    tensor[0, y, x, 2] = pixel.Item2; // R
                                }
                            return tensor;
                        }

                    }
                }
            }
        }

        private static Mat CompositeOnWhiteViaGdi(Mat bgraMat)
        {
            int w = bgraMat.Width, h = bgraMat.Height;
            byte[] srcBytes = new byte[w * h * 4];
            Marshal.Copy(bgraMat.Data, srcBytes, 0, srcBytes.Length);

            var srcBmp = new System.Drawing.Bitmap(w, h,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var srcBd = srcBmp.LockBits(
                new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Marshal.Copy(srcBytes, 0, srcBd.Scan0, srcBytes.Length);
            srcBmp.UnlockBits(srcBd);

            var whiteBmp = new System.Drawing.Bitmap(w, h,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(whiteBmp))
            {
                g.Clear(System.Drawing.Color.White);
                g.DrawImage(srcBmp, 0, 0, w, h);
            }
            srcBmp.Dispose();

            var dstBd = whiteBmp.LockBits(
                new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            int stride = dstBd.Stride;
            byte[] dstBytes = new byte[stride * h];
            Marshal.Copy(dstBd.Scan0, dstBytes, 0, dstBytes.Length);
            whiteBmp.UnlockBits(dstBd);
            whiteBmp.Dispose();

            var mat = new Mat(h, w, MatType.CV_8UC3);
            int matStep = (int)mat.Step();
            byte[] matBytes = new byte[h * matStep];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(dstBytes, y * stride, matBytes, y * matStep, w * 3);
            Marshal.Copy(matBytes, 0, mat.Data, matBytes.Length);
            return mat;
        }

        // タグ情報（名前＋CSVカテゴリ番号）
        private class TagInfo
        {
            public string Name { get; }
            public int CsvCategory { get; }
            public TagInfo(string name, int csvCategory)
            { Name = name; CsvCategory = csvCategory; }
        }

        private List<TagInfo> LoadTags(string csvPath)
        {
            var tags = new List<TagInfo>();
            bool isFirst = true;
            foreach (var line in File.ReadLines(csvPath))
            {
                if (isFirst) { isFirst = false; continue; } // ヘッダースキップ
                var parts = line.Split(',');
                if (parts.Length < 3) continue;
                var name = parts[1].Trim().Replace(" ", "_");
                int.TryParse(parts[2].Trim(), out int cat);
                tags.Add(new TagInfo(name, cat));
            }
            return tags;
        }

        public void Dispose() => _session?.Dispose();
    }



    public class TagEntry
    {
        public int Id { get; }
        public string Name { get; }
        public TagCategory Category { get; }
        public int CsvCategory { get; }  
        public TagEntry(int id, string name, TagCategory category, int csvCategory = 0)
        {
            Id = id;
            Name = name;
            Category = category;
            CsvCategory = csvCategory;  
        }
    }

    public class PredictedTag
    {
        public TagEntry Tag { get; }
        public float Score { get; }
        public int CsvCategory { get; }
        public PredictedTag(TagEntry tag, float score, int csvCategory = 0)
        { Tag = tag; Score = score; CsvCategory = csvCategory; }
    }

    public enum TagCategory
    {
        General = 0, Artist = 1, Copyright = 3,
        Character = 4, Meta = 5, Rating = 9
    }
}