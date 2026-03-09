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

        // exeと同じフォルダのモデルパス（固定）
        public static string DefaultModelPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wd-v1-4-vit-tagger-v2.onnx");
        public static string DefaultCsvPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selected_tags.csv");

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

        private void TryLoadDefaultModel()
        {
            var modelPath = DefaultModelPath;
            var csvPath = DefaultCsvPath;
            if (!File.Exists(modelPath) || !File.Exists(csvPath)) return;

            try
            {
                _lastModelPath = modelPath;
                _lastCsvPath = csvPath;
                _tagger = new WD14TaggerService(modelPath, csvPath, ExecutionDevice.Auto);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MODEL] ロード失敗: {ex}");
            }
        }

        public bool LoadModel(string modelPath, string csvPath,
                              ExecutionDevice device = ExecutionDevice.Auto)
        {
            try
            {
                _tagger?.Dispose();
                _lastModelPath = modelPath;
                _lastCsvPath = csvPath;
                _tagger = new WD14TaggerService(modelPath, csvPath, device);
                return true;
            }
            catch { _tagger = null; return false; }
        }

        public void ReloadWithDevice(ExecutionDevice device)
        {
            if (_lastModelPath == null) return;
            try
            {
                _tagger?.Dispose();
                _tagger = new WD14TaggerService(_lastModelPath, _lastCsvPath, device);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MODEL] 再ロード失敗: {ex}");
            }
        }

        /// <summary>モデルファイルが無ければダウンロードしてからロードする</summary>
        public async Task<bool> EnsureAndLoadModelAsync(
            ExecutionDevice device,
            IProgress<(double Ratio, string Message)> progress,
            CancellationToken ct = default)
        {
            var modelPath = DefaultModelPath;
            var csvPath = DefaultCsvPath;

            try
            {
                await WD14TaggerService.EnsureModelFilesAsync(modelPath, csvPath, progress, ct);
                return LoadModel(modelPath, csvPath, device);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DOWNLOAD] 失敗: {ex}");
                throw;
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
            int parallelCount = 1)
        {
            int count = 0;
            var targetList = targets.ToList();
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
                                keepCategories.Contains(_classifier.Classify(p.Tag.Name))).ToList();

                        item.Tags = new ObservableCollection<TagViewModel>(
                            filtered.Select(p => new TagViewModel(
                                p.Tag.Name, p.Score,
                                _classifier.Classify(p.Tag.Name))));
                        item.SaveTagsToFile();
                    });

                    progress?.Report(Interlocked.Increment(ref count));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[INFER ERROR] {item.FileName}: {ex.Message}");
                    progress?.Report(Interlocked.Increment(ref count));
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
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