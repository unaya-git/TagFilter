using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;

namespace TagFilter
{
    // ── 一覧表示用ラッパー ──────────────────────────────────────────────
    public class DatasetItemView
    {
        public DatasetItem Item { get; }
        public IList<TagViewModel> FilteredTags { get; private set; }
        private static readonly TagClassifier _classifier = new TagClassifier();

        public DatasetItemView(DatasetItem item, BodyPartCategory? filter)
        {
            Item = item;
            ApplyFilter(filter);
        }

        public void ApplyFilter(BodyPartCategory? filter)
        {
            FilteredTags = filter.HasValue
                ? Item.Tags.Where(t => _classifier.Classify(t.Name) == filter.Value).ToList()
                : Item.Tags.ToList();
        }
    }

    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new MainViewModel();
        private BodyPartCategory? _activeFilter = null;
        private DatasetItem _currentItem = null;
        private bool _isFilterUpdating = false;
        private bool _gpuAvailable = false;

        private readonly ObservableCollection<DatasetItemView> _displayItems
            = new ObservableCollection<DatasetItemView>();

        // 並列数の上限（GPU/CPU で変える）
        private int _maxParallel = 1;

        private static readonly (string Label, BodyPartCategory? Category)[] LoraModes =
        {
            ("全タグ（絞り込みなし）", null),
            ("顔 LoRA",               BodyPartCategory.Face),
            ("服装 LoRA",             BodyPartCategory.Outfit),
            ("体型 LoRA",             BodyPartCategory.Body),
            ("ポーズ LoRA",           BodyPartCategory.Pose),
            ("背景 LoRA",             BodyPartCategory.Background),
            ("スタイル LoRA",         BodyPartCategory.Style),
        };

        private static readonly Dictionary<BodyPartCategory, string> CategoryLabels =
            new Dictionary<BodyPartCategory, string>
        {
            { BodyPartCategory.Face,       "顔" },
            { BodyPartCategory.Body,       "体" },
            { BodyPartCategory.Outfit,     "服装" },
            { BodyPartCategory.Pose,       "ポーズ" },
            { BodyPartCategory.Background, "背景" },
            { BodyPartCategory.Style,      "スタイル" },
            { BodyPartCategory.Other,      "その他" },
        };

        public MainWindow()
        {
            InitializeComponent();
            ImageList.ItemsSource = _vm.Items;
            AllItemsListView.ItemsSource = _displayItems;
            CmbDevice.SelectedIndex = 0; // 自動
            InitLoraComboBox();
            RegisterFilterEvents();
            // 起動直後にGPU可否を非同期チェック
            Loaded += async (_, __) => await CheckGpuAsync();
        }

        // ── GPU 可否チェック（DirectML版）──────────────────────────────
        private async Task CheckGpuAsync()
        {
            SetGpuStatus("確認中...", "#6C7086");
            _gpuAvailable = await Task.Run(() => WD14TaggerService.IsDirectMLAvailable());

            if (_gpuAvailable)
            {
                SetGpuStatus("GPU(DirectML) 使用可", "#A6E3A1");
                if (CmbDevice.SelectedIndex <= 1)
                    SetParallelCount(1);
            }
            else
            {
                SetGpuStatus("CPU のみ", "#F38BA8");
                if (CmbDevice.SelectedIndex == 1)
                    CmbDevice.SelectedIndex = 0;
                SetParallelCount(Math.Max(1, Environment.ProcessorCount / 2));
            }
        }


        private void SetGpuStatus(string text, string hexColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            GpuIndicator.Background = new SolidColorBrush(
                Color.FromArgb(60, color.R, color.G, color.B));
            GpuIndicator.BorderBrush = new SolidColorBrush(color);
            GpuIndicator.BorderThickness = new Thickness(1);
            TxtGpuStatus.Text = text;
            TxtGpuStatus.Foreground = new SolidColorBrush(color);
        }

        // ── デバイス選択変更 ────────────────────────────────────────────
        private void CmbDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtParallel == null) return;

            switch (CmbDevice.SelectedIndex)
            {
                case 0: // 自動
                case 1: // GPU
                    SetParallelCount(1);
                    break;
                case 2: // CPU
                    SetParallelCount(Math.Max(1, Environment.ProcessorCount / 2));
                    break;
            }
        }

        // ── 並列数操作 ──────────────────────────────────────────────────
        private void SetParallelCount(int value)
        {
            _maxParallel = Math.Max(1, value);
            if (TxtParallel != null)
                TxtParallel.Text = _maxParallel.ToString();
        }

        private int GetParallelCount()
        {
            if (int.TryParse(TxtParallel.Text, out int v) && v >= 1)
                return v;
            return 1;
        }

        private void BtnParallelMinus_Click(object sender, RoutedEventArgs e)
            => SetParallelCount(GetParallelCount() - 1);

        private void BtnParallelPlus_Click(object sender, RoutedEventArgs e)
            => SetParallelCount(GetParallelCount() + 1);

        private void TxtParallel_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !e.Text.All(char.IsDigit);

        // ── 実行デバイスを取得 ──────────────────────────────────────────
        private ExecutionDevice GetExecutionDevice()
        {
            switch (CmbDevice.SelectedIndex)
            {
                case 1: return ExecutionDevice.Gpu;
                case 2: return ExecutionDevice.Cpu;
                default: // 自動
                    return _gpuAvailable ? ExecutionDevice.Gpu : ExecutionDevice.Cpu;
            }
        }

        // ── ComboBox 初期化 ─────────────────────────────────────────────
        private void InitLoraComboBox()
        {
            foreach (var mode in LoraModes)
                CmbLoraMode.Items.Add(mode.Label);
            CmbLoraMode.SelectedIndex = 0;
        }

        private void RegisterFilterEvents()
        {
            foreach (var tb in GetCategoryToggleButtons())
            {
                tb.Checked += CategoryFilter_Changed;
                tb.Unchecked += CategoryFilter_Changed;
            }
        }

        // ── フォルダを開く ──────────────────────────────────────────────
        private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            { Description = "画像フォルダを選択してください" };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            SetStatus("読み込み中...");
            BtnOpenFolder.IsEnabled = false;
            await _vm.LoadFolderAsync(dialog.SelectedPath);
            await LoadThumbnailsAsync();
            TxtImageCount.Text = $"{_vm.Items.Count} 件";
            RebuildDisplayItems();
            SetStatus($"{_vm.Items.Count} 件の画像を読み込みました");
            BtnOpenFolder.IsEnabled = true;
        }

        private async Task LoadThumbnailsAsync()
        {
            foreach (var item in _vm.Items)
            {
                if (item.Thumbnail != null) continue;
                var path = item.ImagePath;
                var bmp = await Task.Run(() => ThumbnailLoader.Load(path, 196));
                item.Thumbnail = bmp;
            }
        }

        // ── 表示リスト再構築 ────────────────────────────────────────────
        private void RebuildDisplayItems()
        {
            _displayItems.Clear();
            foreach (var item in _vm.Items)
                _displayItems.Add(new DatasetItemView(item, _activeFilter));
        }

        private void ReapplyFilter()
        {
            var items = _displayItems.Select(v => v.Item).ToList();
            _displayItems.Clear();
            foreach (var item in items)
                _displayItems.Add(new DatasetItemView(item, _activeFilter));
        }

        // ── 画像選択 ────────────────────────────────────────────────────
        private void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentItem = ImageList.SelectedItem as DatasetItem;
            int count = ImageList.SelectedItems.Count;
            TxtSelectedCount.Text = _currentItem == null ? ""
                : count > 1 ? $"{count} 枚選択中"
                : _currentItem.FileName;
        }

        // ── 表示フィルタ ────────────────────────────────────────────────
        private void CategoryFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isFilterUpdating) return;
            _isFilterUpdating = true;
            try
            {
                var btn = sender as ToggleButton;
                if (btn == null) return;
                var tag = btn.Tag as string;

                if (tag == "All")
                {
                    _activeFilter = null;
                    foreach (var tb in GetCategoryToggleButtons())
                        if (tb != FilterAll) tb.IsChecked = false;
                    FilterAll.IsChecked = true;
                }
                else
                {
                    if (btn.IsChecked == true)
                    {
                        FilterAll.IsChecked = false;
                        foreach (var tb in GetCategoryToggleButtons())
                            if (tb != btn && tb != FilterAll) tb.IsChecked = false;
                        if (Enum.TryParse(tag, out BodyPartCategory cat))
                            _activeFilter = cat;
                    }
                    else
                    {
                        _activeFilter = null;
                        FilterAll.IsChecked = true;
                    }
                }

                UpdateDeleteButton();
                ReapplyFilter();
            }
            finally { _isFilterUpdating = false; }
        }

        private void UpdateDeleteButton()
        {
            if (_activeFilter.HasValue && CategoryLabels.TryGetValue(_activeFilter.Value, out var label))
            {
                BtnBulkDeleteCategory.Content = $"「{label}」タグを全削除";
                BtnBulkDeleteCategory.IsEnabled = true;
            }
            else
            {
                BtnBulkDeleteCategory.Content = "選択中カテゴリを削除";
                BtnBulkDeleteCategory.IsEnabled = false;
            }
        }

        private IEnumerable<ToggleButton> GetCategoryToggleButtons()
        {
            yield return FilterAll;
            yield return FilterFace;
            yield return FilterBody;
            yield return FilterOutfit;
            yield return FilterPose;
            yield return FilterBg;
            yield return FilterStyle;
            yield return FilterOther;
        }

        // ── タグ追加 ────────────────────────────────────────────────────
        private void BtnAddTag_Click(object sender, RoutedEventArgs e) => AddTagFromInput();
        private void TxtNewTag_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddTagFromInput();
        }

        private void AddTagFromInput()
        {
            if (_currentItem == null) { SetStatus("⚠ 左の一覧から画像を選択してください"); return; }
            var tagName = TxtNewTag.Text.Trim().ToLower().Replace(" ", "_");
            if (string.IsNullOrEmpty(tagName)) return;
            if (_currentItem.HasTag(tagName))
            {
                SetStatus($"「{tagName}」は既に存在します");
                TxtNewTag.SelectAll();
                return;
            }
            var category = new TagClassifier().Classify(tagName);
            _currentItem.Tags.Insert(0, new TagViewModel(tagName, 1.0f, category));
            _currentItem.SaveTagsToFile();
            ReapplyFilter();
            SetStatus($"追加: {tagName}  ({_currentItem.TagCount} tags)");
            TxtNewTag.Clear();
            TxtNewTag.Focus();
        }

        // ── タグ削除（一覧） ────────────────────────────────────────────
        private void ListViewTagDelete_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var tagVm = (sender as FrameworkElement)?.Tag as TagViewModel;
            if (tagVm == null) return;
            foreach (var item in _vm.Items)
            {
                if (item.Tags.Contains(tagVm))
                {
                    item.Tags.Remove(tagVm);
                    item.SaveTagsToFile();
                    ReapplyFilter();
                    SetStatus($"{item.FileName}: {tagVm.Name} を削除");
                    break;
                }
            }
        }

        // ── 一括挿入 ────────────────────────────────────────────────────
        private void TxtBulkTag_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoBulkInsert();
        }
        private void BtnBulkInsert_Click(object sender, RoutedEventArgs e) => DoBulkInsert();

        private void DoBulkInsert()
        {
            var tagName = TxtBulkTag.Text.Trim().ToLower().Replace(" ", "_");
            if (string.IsNullOrEmpty(tagName)) { SetStatus("⚠ 挿入するタグを入力してください"); return; }
            var targets = GetTargetItems();
            _vm.BulkInsertTag(tagName, targets);
            ReapplyFilter();
            string scope = ImageList.SelectedItems.Count > 0
                ? $"選択中 {targets.Count} 枚" : $"全 {targets.Count} 枚";
            SetStatus($"「{tagName}」を {scope} に挿入しました");
            TxtBulkTag.Clear();
        }

        // ── カテゴリ一括削除 ────────────────────────────────────────────
        private void BtnBulkDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (!_activeFilter.HasValue) return;
            CategoryLabels.TryGetValue(_activeFilter.Value, out var label);
            var targets = GetTargetItems();
            string scope = ImageList.SelectedItems.Count > 0
                ? $"選択中 {targets.Count} 枚" : $"全 {targets.Count} 枚";
            var result = MessageBox.Show(
                $"{scope} の画像から\n「{label}」カテゴリのタグを全て削除します。\n\nよろしいですか？",
                "一括削除の確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;
            _vm.BulkDeleteByCategory(_activeFilter.Value, targets);
            ReapplyFilter();
            SetStatus($"「{label}」タグを {scope} から削除しました");
        }

        private List<DatasetItem> GetTargetItems()
        {
            var selected = ImageList.SelectedItems.Cast<DatasetItem>().ToList();
            return selected.Any() ? selected : _vm.Items.ToList();
        }

        // ── 全保存 ──────────────────────────────────────────────────────
        private void BtnSaveAll_Click(object sender, RoutedEventArgs e)
        {
            int count = 0;
            foreach (var item in _vm.Items) { item.SaveTagsToFile(); count++; }
            SetStatus($"全 {count} 件を保存しました");
        }

        // ── 自動タグ付け ────────────────────────────────────────────────
        private HashSet<BodyPartCategory> GetSelectedLoraCategories()
        {
            int idx = CmbLoraMode.SelectedIndex;
            if (idx <= 0 || idx >= LoraModes.Length) return null;
            var cat = LoraModes[idx].Category;
            return cat.HasValue ? new HashSet<BodyPartCategory> { cat.Value } : null;
        }

        private CancellationTokenSource _downloadCts;

        private async void BtnAutoTag_Click(object sender, RoutedEventArgs e)
        {
            var device = GetExecutionDevice();

            // モデルファイルが無い or 未ロードならダウンロード＋ロード
            if (!_vm.IsTaggerReady)
            {
                bool modelExists = File.Exists(MainViewModel.DefaultModelPath)
                                && File.Exists(MainViewModel.DefaultCsvPath);

                if (!modelExists)
                {
                    var dlgResult = MessageBox.Show(
                        "モデルファイルが見つかりません。\n\n" +
                        "Hugging Face から自動ダウンロードしますか？\n" +
                        "（wd-v1-4-vit-tagger-v2、約350MB）",
                        "モデルが未ダウンロード",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (dlgResult != MessageBoxResult.Yes) return;

                    // ダウンロード中UI
                    BtnAutoTag.IsEnabled = false;
                    BtnOpenFolder.IsEnabled = false;
                    ProgressBar.Visibility = Visibility.Visible;
                    TxtProgress.Visibility = Visibility.Visible;
                    ProgressBar.Maximum = 100;
                    _downloadCts = new CancellationTokenSource();

                    var dlProgress = new Progress<(double Ratio, string Message)>(p =>
                    {
                        ProgressBar.Value = p.Ratio * 100;
                        SetStatus($"ダウンロード中... {p.Message}");
                        TxtProgress.Text = $"{p.Ratio:P0}";
                    });

                    try
                    {
                        bool ok = await _vm.EnsureAndLoadModelAsync(device, dlProgress, _downloadCts.Token);
                        if (!ok)
                        {
                            SetStatus("モデルのロードに失敗しました");
                            return;
                        }
                        SetStatus("モデルのダウンロード・ロード完了");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"ダウンロードに失敗しました:\n{ex.Message}",
                            "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    finally
                    {
                        BtnAutoTag.IsEnabled = true;
                        BtnOpenFolder.IsEnabled = true;
                        ProgressBar.Visibility = Visibility.Collapsed;
                        TxtProgress.Visibility = Visibility.Collapsed;
                        _downloadCts?.Dispose();
                        _downloadCts = null;
                    }
                }
                else
                {
                    // ファイルはあるがまだロードされていない
                    bool ok = await Task.Run(() => _vm.LoadModel(
                        MainViewModel.DefaultModelPath,
                        MainViewModel.DefaultCsvPath, device));
                    if (!ok) { SetStatus("モデルのロードに失敗しました"); return; }
                }
            }
            else
            {
                // デバイス設定が変わった可能性があるので再ロード
                _vm.ReloadWithDevice(device);
            }

            // ── 以降は推論処理（前回と同じ）──────────────────────────────
            var targets = GetTargetItems();
            if (!targets.Any()) { SetStatus("⚠ フォルダを開いてください"); return; }

            var keepCategories = GetSelectedLoraCategories();
            string modeName = LoraModes[CmbLoraMode.SelectedIndex].Label;
            int parallel = GetParallelCount();
            string deviceLabel = GetExecutionDevice() == ExecutionDevice.Gpu ? "DirectML" : "CPU";

            BtnAutoTag.IsEnabled = false;
            BtnOpenFolder.IsEnabled = false;
            ImageList.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;
            TxtProgress.Visibility = Visibility.Visible;
            ProgressBar.Maximum = targets.Count;
            ProgressBar.Value = 0;

            var progress = new Progress<int>(v =>
            {
                ProgressBar.Value = v;
                TxtProgress.Text = $"{v} / {targets.Count}";
                SetStatus($"推論中 [{deviceLabel} x{parallel}]  {v}/{targets.Count} 枚");
            });

            try
            {
                await _vm.BatchTagAsync(targets, (float)SliderThreshold.Value,
                                        progress, keepCategories, parallel);
                RebuildDisplayItems();
                SetStatus($"完了: [{modeName} / {deviceLabel} x{parallel}]  {targets.Count} 枚");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"推論エラー:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnAutoTag.IsEnabled = true;
                BtnOpenFolder.IsEnabled = true;
                ImageList.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
                TxtProgress.Visibility = Visibility.Collapsed;
            }
        }


        private async Task ManualLoadModelAsync(ExecutionDevice device)
        {
            var onnxDlg = new Microsoft.Win32.OpenFileDialog
            { Title = "ONNXモデルを選択", Filter = "ONNX Model (*.onnx)|*.onnx" };
            if (onnxDlg.ShowDialog() != true) return;
            var csvDlg = new Microsoft.Win32.OpenFileDialog
            { Title = "selected_tags.csv を選択", Filter = "CSV (*.csv)|*.csv" };
            if (csvDlg.ShowDialog() != true) return;
            SetStatus("モデルを読み込んでいます...");
            bool ok = await Task.Run(() => _vm.LoadModel(onnxDlg.FileName, csvDlg.FileName, device));
            SetStatus(ok ? "モデルの読み込み完了" : "モデルの読み込みに失敗しました");
        }

        private void SetStatus(string message) => TxtStatus.Text = message;
    }
}