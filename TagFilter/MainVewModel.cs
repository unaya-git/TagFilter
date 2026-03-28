using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TagFilter
{
    public enum ExecutionDevice { Auto, Gpu, Cpu }

    public class MainViewModel : ViewModelBase
    {
        private WD14TaggerService _tagger;
        private readonly TagClassifier _classifier;


        // 選択中のプリセット（デフォルトはインデックス0）
        public WD14ModelPreset CurrentPreset { get; private set; }
            = WD14ModelPreset.All[0];

        public static string DefaultModelPath =>
            WD14ModelPreset.All[0].OnnxPath;
        public static string DefaultCsvPath =>
            WD14ModelPreset.All[0].CsvPath;

        private void TryLoadDefaultModel()
        {
            TryLoadPreset(CurrentPreset, ExecutionDevice.Auto);
        }

        private void TryLoadPreset(WD14ModelPreset preset, ExecutionDevice device)
        {
            if (!File.Exists(preset.OnnxPath) || !File.Exists(preset.CsvPath)) return;
            try
            {
                _tagger?.Dispose();
                _lastModelPath = preset.OnnxPath;
                _lastCsvPath = preset.CsvPath;
                CurrentPreset = preset;
                _tagger = new WD14TaggerService(preset.OnnxPath, preset.CsvPath, device);
            }
            catch (Exception ex)
            {
                _tagger = null;
                System.Diagnostics.Debug.WriteLine($"[MODEL] ロード失敗: {ex}");
            }
        }


        /// <summary>プリセットを切り替えてロード（未DLならダウンロードも行う）</summary>
        public async Task<bool> EnsureAndLoadModelAsync(
            WD14ModelPreset preset,
            ExecutionDevice device,
            IProgress<(double Ratio, string Message)> progress,
            CancellationToken ct = default)
        {
            try
            {
                await WD14TaggerService.EnsureModelFilesAsync(preset, progress, ct);

                _tagger?.Dispose();
                _lastModelPath = preset.OnnxPath;
                _lastCsvPath = preset.CsvPath;
                CurrentPreset = preset;
                return LoadModel(preset.OnnxPath, preset.CsvPath, device);
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DOWNLOAD] 失敗: {ex}");
                throw;
            }
        }

        private string _lastModelPath;
        private string _lastCsvPath;

        public bool IsTaggerReady => _tagger != null;

        public ObservableCollection<DatasetItem> Items { get; }
            = new ObservableCollection<DatasetItem>();

        public MainViewModel()
        {
            _classifier = new TagClassifier();
            TryLoadDefaultModel();
        }


        public ExecutionDevice LastDevice { get; private set; } = ExecutionDevice.Auto;

        public bool LoadModel(string modelPath, string csvPath,
                              ExecutionDevice device = ExecutionDevice.Auto)
        {
            try
            {
                _tagger?.Dispose();
                _lastModelPath = modelPath;
                _lastCsvPath = csvPath;
                LastDevice = device;  // ← 追加
                _tagger = new WD14TaggerService(modelPath, csvPath, device);
                return true;
            }
            catch { _tagger = null; return false; }
        }

        public void ReloadWithDevice(ExecutionDevice device)
        {
            if (_lastModelPath == null || _lastCsvPath == null) return;
            try
            {
                var old = _tagger;
                _tagger = null;
                old?.Dispose();
                _tagger = new WD14TaggerService(_lastModelPath, _lastCsvPath, device);
                LastDevice = device;  // ← 追加
            }
            catch (Exception ex)
            {
                _tagger = null;
                throw new InvalidOperationException(
                    $"モデルの再ロードに失敗しました:\n{ex.Message}", ex);
            }
        }

        public async Task LoadFolderAsync(string folderPath)
        {
            var extensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".webp" };
            var imageFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            Items.Clear();
            await Task.Run(() =>
            {
                foreach (var path in imageFiles)
                {
                    var item = new DatasetItem { ImagePath = path };
                    item.LoadTagsFromFile();
                    Application.Current.Dispatcher.Invoke(() => Items.Add(item));
                }
            });
        }

        public async Task BatchTagAsync(
            IEnumerable<DatasetItem> targets,
            float threshold = 0.35f,
            IProgress<int> progress = null,
            HashSet<BodyPartCategory> keepCategories = null,
            int parallelCount = 1
            //Action<string> onError = null
            )
        {
            int count = 0;
            var targetList = targets.ToList();
            //var errorFiles = new System.Collections.Concurrent.ConcurrentBag<string>();
            var errorFiles = new System.Collections.Concurrent.ConcurrentBag<(string File, string Error)>();
            var semaphore = new SemaphoreSlim(Math.Max(1, parallelCount));

            var tasks = targetList.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var predicted = await Task.Run(
                        () => _tagger.Predict(item.ImagePath, threshold));

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var filtered = keepCategories == null
                            ? predicted
                            : predicted.Where(p =>
                                keepCategories.Contains(
                                    _classifier.ClassifyWithCsvCategory(p.Tag.Name, p.CsvCategory))
                              ).ToList();

                        item.Tags = new ObservableCollection<TagViewModel>(
                            filtered.Select(p => new TagViewModel(
                                p.Tag.Name, p.Score,
                                _classifier.ClassifyWithCsvCategory(p.Tag.Name, p.CsvCategory))));
                        item.SaveTagsToFile();
                    });

                    progress?.Report(Interlocked.Increment(ref count));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[INFER ERROR] {item.FileName}: {ex.GetType().Name}: {ex.Message}");
                    //errorFiles.Add(item.FileName);
                    errorFiles.Add((item.FileName, $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"));
                    progress?.Report(Interlocked.Increment(ref count));

                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);

            if (errorFiles.Count > 0)
            {
                /*
                var msg = errorFiles.Count == 1
                    ? $"以下の画像でタグ付けに失敗しました:\n{errorFiles.First()}"
                    : $"{errorFiles.Count} 枚の画像でタグ付けに失敗しました:\n"
                      + string.Join("\n", errorFiles.Take(10))
                      + (errorFiles.Count > 10 ? $"\n...他 {errorFiles.Count - 10} 件" : "");

                Application.Current.Dispatcher.Invoke(() =>
                    MessageBox.Show(msg, "タグ付けエラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
                */
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(Strings.MsgThumbnailFailed);
                foreach (var (file, error) in errorFiles.Take(3))
                {
                    sb.AppendLine($"── {file} ──");
                    sb.AppendLine(error);
                    sb.AppendLine();
                }
                if (errorFiles.Count > 3)
                    sb.AppendLine($"...他 {errorFiles.Count - 3} 件");
                MessageBox.Show(sb.ToString(), Strings.MsgError,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void BulkInsertTag(string tagName, IEnumerable<DatasetItem> targets)
        {
            foreach (var item in targets)
            {
                if (!item.Tags.Any(t => t.Name == tagName))
                    item.Tags.Insert(0, new TagViewModel(tagName));
                item.SaveTagsToFile();
            }
        }

        public void BulkDeleteByCategory(BodyPartCategory category, IEnumerable<DatasetItem> targets)
        {
            foreach (var item in targets)
            {
                var toRemove = item.Tags
                    .Where(t => _classifier.Classify(t.Name) == category).ToList();
                foreach (var tag in toRemove) item.Tags.Remove(tag);
                item.SaveTagsToFile();
            }
        }

        public void BulkDeleteTag(string tagName, IEnumerable<DatasetItem> targets)
        {
            foreach (var item in targets)
            {
                var tag = item.Tags.FirstOrDefault(t => t.Name == tagName);
                if (tag != null) item.Tags.Remove(tag);
                item.SaveTagsToFile();
            }
        }

    }

}