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
        private AppSettings _settings;

        private readonly ObservableCollection<DatasetItemView> _displayItems
            = new ObservableCollection<DatasetItemView>();

        // 並列数の上限（GPU/CPU で変える）
        private int _maxParallel = 1;

        private static readonly (string Label, BodyPartCategory? Category)[] LoraModes =
        {
            ("全タグ（絞り込みなし）", null),
            ("顔 LoRA",               BodyPartCategory.Face),
            ("服装 LoRA",             BodyPartCategory.Outfit),
            ("体 LoRA",               BodyPartCategory.Body),
            ("ポーズ LoRA",           BodyPartCategory.Pose),
            ("背景 LoRA",             BodyPartCategory.Background),
            ("スタイル LoRA",         BodyPartCategory.Style),
            ("表現 LoRA",             BodyPartCategory.Expression),
            ("作者名",                BodyPartCategory.Artist),
            ("キャラクター",          BodyPartCategory.Character),  
            ("作品名",                BodyPartCategory.Copyright), 

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
    { BodyPartCategory.Expression, "表現" },
    { BodyPartCategory.Artist,     "作者"       },
    { BodyPartCategory.Copyright,  "作品名"     },
    { BodyPartCategory.Character,  "キャラ"     },
    { BodyPartCategory.Other,      "その他" },
        };

        public MainWindow()
        {
            InitializeComponent();

            // 設定をロード
            _settings = AppSettings.Load();

            // 万一設定ファイルに重複が入っていた場合の保険
            _settings.UnwantedTags = _settings.UnwantedTags
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            ImageList.ItemsSource = _vm.Items;
            AllItemsListView.ItemsSource = _displayItems;

            // 設定から復元
            CmbDevice.SelectedIndex = Math.Min(_settings.DeviceIndex, 2);
            SliderThreshold.Value = Math.Max(0.1, Math.Min(0.9, _settings.Threshold));
            SetParallelCount(_settings.ParallelCount);

            InitModelComboBox();
            InitLoraComboBox();
            RegisterFilterEvents();
            RebuildUnwantedTagChips();

            Closing += MainWindow_Closing;
            Loaded += async (_, __) => await CheckGpuAsync();
        }

        // ウィンドウ終了時に保存
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _settings.ModelIndex = CmbModel.SelectedIndex;
            _settings.DeviceIndex = CmbDevice.SelectedIndex;
            _settings.ParallelCount = GetParallelCount();
            _settings.LoraModeIndex = CmbLoraMode.SelectedIndex;
            _settings.Threshold = SliderThreshold.Value;
            _settings.Save();
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
        // ── モデル選択 ──────────────────────────────────────────────────
        private void InitModelComboBox()
        {
            foreach (var preset in WD14ModelPreset.All)
                CmbModel.Items.Add(preset.Name);
            // 保存済みインデックスで復元（範囲チェックあり）
            CmbModel.SelectedIndex = Math.Min(
                _settings.ModelIndex, WD14ModelPreset.All.Length - 1);
            UpdateModelStatus();
        }

        private void UpdateModelStatus()
        {
            var idx = CmbModel.SelectedIndex;
            if (idx < 0 || idx >= WD14ModelPreset.All.Length) return;
            var preset = WD14ModelPreset.All[idx];

            bool downloaded = File.Exists(preset.OnnxPath) && File.Exists(preset.CsvPath);
            bool isCurrent = _vm.CurrentPreset == preset;

            if (isCurrent && downloaded)
            {
                TxtModelStatus.Text = "使用中";
                SetIndicatorColor(ModelStatusIndicator, TxtModelStatus, "#A6E3A1");
            }
            else if (downloaded)
            {
                TxtModelStatus.Text = "DL済";
                SetIndicatorColor(ModelStatusIndicator, TxtModelStatus, "#89B4FA");
            }
            else
            {
                TxtModelStatus.Text = "未DL";
                SetIndicatorColor(ModelStatusIndicator, TxtModelStatus, "#6C7086");
            }

            // ツールチップに説明を表示
            CmbModel.ToolTip = preset.Description;
        }

        private void SetIndicatorColor(Border border, TextBlock text, string hexColor)
        {
            var color = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
            border.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(60, color.R, color.G, color.B));
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(color);
            border.BorderThickness = new Thickness(1);
            text.Foreground = new System.Windows.Media.SolidColorBrush(color);
        }

        private void CmbModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateModelStatus();
        }

        private WD14ModelPreset GetSelectedPreset()
        {
            int idx = CmbModel.SelectedIndex;
            if (idx < 0 || idx >= WD14ModelPreset.All.Length)
                return WD14ModelPreset.All[0];
            return WD14ModelPreset.All[idx];
        }

        private void InitLoraComboBox()
        {
            foreach (var mode in LoraModes)
                CmbLoraMode.Items.Add(mode.Label);
            CmbLoraMode.SelectedIndex = Math.Min(
                _settings.LoraModeIndex, LoraModes.Length - 1);
        }

        private void RegisterFilterEvents()
        {
            foreach (var tb in GetCategoryToggleButtons())
            {
                tb.Checked += CategoryFilter_Changed;
                tb.Unchecked += CategoryFilter_Changed;
            }
        }

        // ── 不要タグチップ ──────────────────────────────────────────────

        private void RebuildUnwantedTagChips()
        {
            UnwantedTagsPanel.Children.Clear();
            foreach (var tag in _settings.UnwantedTags)
                UnwantedTagsPanel.Children.Add(CreateUnwantedTagChip(tag));
        }

        private UIElement CreateUnwantedTagChip(string tagName)
        {
            // クリックでTxtDeleteTagにセット、[×]で登録削除
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#313149")),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 6, 3),
                Margin = new Thickness(0, 0, 4, 3),
                Cursor = Cursors.Hand,
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var label = new TextBlock
            {
                Text = tagName,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#CDD6F4")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.MouseLeftButtonDown += (s, e) =>
            {
                TxtDeleteTag.Text = tagName;
                e.Handled = true;
            };

            var close = new TextBlock
            {
                Text = " ×",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#F38BA8")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
            };
            close.MouseLeftButtonDown += (s, e) =>
            {
                _settings.UnwantedTags.Remove(tagName);
                RebuildUnwantedTagChips();
                if (TxtDeleteTag.Text == tagName) TxtDeleteTag.Clear();
                e.Handled = true;
            };

            panel.Children.Add(label);
            panel.Children.Add(close);
            border.Child = panel;

            // チップ全体クリックでもTxtDeleteTagにセット
            border.MouseLeftButtonDown += (s, e) =>
            {
                TxtDeleteTag.Text = tagName;
            };

            return border;
        }

        // 登録ボタン
        private void BtnAddUnwantedTag_Click(object sender, RoutedEventArgs e)
        {
            var tag = TxtDeleteTag.Text.Trim();
            if (string.IsNullOrEmpty(tag)) return;

            // 重複チェック
            if (_settings.UnwantedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                TxtDeleteTag.Clear();
                return;
            }

            _settings.UnwantedTags.Add(tag);
            TxtDeleteTag.Clear();
            RebuildUnwantedTagChips();
        }


        // ── フォルダを開く ──────────────────────────────────────────────
        private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            { Description = "画像フォルダを選択してください" };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            SetStatus("読み込み中...");
            BtnOpenFolder.IsEnabled = false;

            try
            {
                // OneDriveオンライン専用ファイルの警告
                await Task.Run(() =>
                {
                    int onlineCount = 0;
                    foreach (var f in System.IO.Directory.GetFiles(dialog.SelectedPath))
                    {
                        if (FileHelper.IsOnlineOnly(f)) onlineCount++;
                    }
                    if (onlineCount > 0)
                    {
                        Dispatcher.Invoke(() =>
                            MessageBox.Show(
                                $"{onlineCount} 件のファイルがOneDriveのオンライン専用状態です。\n\n" +
                                $"自動タグ付け前に全ファイルを右クリック→\n" +
                                $"「常にこのデバイスに保存」でダウンロードしてください。",
                                "OneDriveファイルの警告",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning));
                    }
                });

                await _vm.LoadFolderAsync(dialog.SelectedPath);
                await LoadThumbnailsAsync();
                TxtImageCount.Text = $"{_vm.Items.Count} 件";
                RebuildDisplayItems();
                SetStatus($"{_vm.Items.Count} 件の画像を読み込みました");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"フォルダ読み込みエラー:\n\n種類: {ex.GetType().Name}\nメッセージ: {ex.Message}\n\n{ex.StackTrace}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("フォルダの読み込みに失敗しました");
            }
            finally
            {
                BtnOpenFolder.IsEnabled = true;
            }
        }

        private async Task LoadThumbnailsAsync()
        {
            var errors = new System.Text.StringBuilder();
            foreach (var item in _vm.Items)
            {
                if (item.Thumbnail != null) continue;
                var path = item.ImagePath;
                try
                {
                    var bmp = await Task.Run(() => ThumbnailLoader.Load(path, 196));
                    item.Thumbnail = bmp;
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"{System.IO.Path.GetFileName(path)}: {ex.GetType().Name} - {ex.Message}");
                }
            }

            if (errors.Length > 0)
            {
                MessageBox.Show(
                    $"以下の画像でサムネイル生成に失敗しました:\n\n{errors}",
                    "サムネイル警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── 表示リスト再構築 ────────────────────────────────────────────
        private void RebuildDisplayItems()
        {
            _displayItems.Clear();
            foreach (var item in _vm.Items)
                _displayItems.Add(new DatasetItemView(item, _activeFilter));

            RebuildTagSummary();
        }

        private void ReapplyFilter()
        {
            var items = _displayItems.Select(v => v.Item).ToList();
            _displayItems.Clear();
            foreach (var item in items)
                _displayItems.Add(new DatasetItemView(item, _activeFilter));

            // ※ ReapplyFilter は頻繁に呼ばれるため、
            //    タグ削除・追加時のみ集計更新したい場合は呼び出し元で制御してください
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
            yield return FilterExpression;
            yield return FilterCharacter;
            yield return FilterCopyright;
            yield return FilterArtist;
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
            RebuildTagSummary();
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
                    RebuildTagSummary();
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
            var preset = GetSelectedPreset();

            // 選択中モデルが未ロード or 別モデルに切り替わった場合
            bool needLoad = !_vm.IsTaggerReady || _vm.CurrentPreset != preset;

            if (needLoad)
            {
                bool modelExists = File.Exists(preset.OnnxPath)
                                && File.Exists(preset.CsvPath);

                if (!modelExists)
                {
                    var dlgResult = MessageBox.Show(
                        $"モデル「{preset.Name}」がダウンロードされていません。\n\n" +
                        $"Hugging Face から自動ダウンロードしますか？\n" +
                        $"（約350〜700MB）",
                        "モデルが未ダウンロード",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (dlgResult != MessageBoxResult.Yes) return;

                    BtnAutoTag.IsEnabled = false;
                    BtnOpenFolder.IsEnabled = false;
                    ProgressBar.Visibility = Visibility.Visible;
                    TxtProgress.Visibility = Visibility.Visible;
                    ProgressBar.Maximum = 100;
                    _downloadCts = new CancellationTokenSource();

                    var dlProgress = new Progress<(double Ratio, string Message)>(p =>
                    {
                        ProgressBar.Value = p.Ratio * 100;
                        TxtProgress.Text = $"{p.Ratio:P0}";
                        SetStatus($"{p.Message}");
                    });

                    try
                    {
                        bool ok = await _vm.EnsureAndLoadModelAsync(
                            preset, device, dlProgress, _downloadCts.Token);
                        if (!ok) { SetStatus("モデルのロードに失敗しました"); return; }
                        SetStatus($"「{preset.Name}」のロード完了");
                        UpdateModelStatus();
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
                    // ファイルはあるが切り替えが必要
                    try
                    {
                        SetStatus($"「{preset.Name}」に切り替え中...");
                        bool ok = await Task.Run(
                            () => _vm.LoadModel(preset.OnnxPath, preset.CsvPath, device));
                        if (!ok) { SetStatus("モデルの切り替えに失敗しました"); return; }
                        UpdateModelStatus();
                        SetStatus($"「{preset.Name}」に切り替えました");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "モデル切り替えエラー",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }
            else
            {
                try { _vm.ReloadWithDevice(device); }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "モデル再ロードエラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }



            // ── 以降は推論処理（前回と同じ）──────────────────────────────
            var targets = GetTargetItems();
            if (!targets.Any()) { SetStatus("⚠ フォルダを開いてください"); return; }

            var keepCategories = GetSelectedLoraCategories();
            string modeName = LoraModes[CmbLoraMode.SelectedIndex].Label;
            //int parallel = GetParallelCount();
            // GPU使用時は並列1固定（DirectMLはスレッドセーフでないため）
            bool useGpu = GetExecutionDevice() != ExecutionDevice.Cpu;
            int parallel = useGpu ? 1 : GetParallelCount();
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
                                        progress, keepCategories, parallel                                       
                                        );
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

        // ── 不要タグ一括削除 ────────────────────────────────────────────
        private void TxtDeleteTag_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnBulkDeleteTag.IsEnabled = !string.IsNullOrWhiteSpace(TxtDeleteTag.Text);
        }

        private void TxtDeleteTag_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoBulkDeleteTag();
        }

        private void BtnBulkDeleteTag_Click(object sender, RoutedEventArgs e)
            => DoBulkDeleteTag();

        // クイックボタン（solo / blue_skin 等）
        private void BtnQuickDelete_Click(object sender, RoutedEventArgs e)
        {
            var tagName = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(tagName)) return;
            TxtDeleteTag.Text = tagName;
            DoBulkDeleteTag();
        }

        private void DoBulkDeleteTag()
        {
            var tagName = TxtDeleteTag.Text.Trim().ToLower().Replace(" ", "_");
            if (string.IsNullOrEmpty(tagName))
            {
                SetStatus("⚠ 削除するタグを入力してください");
                return;
            }

            // 対象画像の中にこのタグが何件あるか確認
            var targets = GetTargetItems();
            int hitCount = targets.Count(item =>
                item.Tags.Any(t => t.Name == tagName));

            if (hitCount == 0)
            {
                SetStatus($"「{tagName}」は見つかりませんでした");
                TxtDeleteTag.Clear();
                return;
            }

            string scope = ImageList.SelectedItems.Count > 0
                ? $"選択中 {targets.Count} 枚" : $"全 {targets.Count} 枚";

            var result = MessageBox.Show(
                $"{scope} の画像から\n「{tagName}」を {hitCount} 件削除します。\n\nよろしいですか？",
                "タグ一括削除の確認",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;

            _vm.BulkDeleteTag(tagName, targets);
            ReapplyFilter();

            SetStatus($"「{tagName}」を {hitCount} 件削除しました");
            TxtDeleteTag.Clear();
        }

        // ══ タグ集計パネル ════════════════════════════════════════════════

        private string _selectedSummaryTag = null;
        private bool _isSortUpdating = false;

        /// <summary>全アイテムのタグを集計してパネルを再構築</summary>
        public void RebuildTagSummary()
        {
            var counts = new Dictionary<string, int>();
            foreach (var item in _vm.Items)
                foreach (var tag in item.Tags)
                {
                    if (!counts.ContainsKey(tag.Name)) counts[tag.Name] = 0;
                    counts[tag.Name]++;
                }

            // ソート
            IEnumerable<KeyValuePair<string, int>> sorted;
            if (SortByCount.IsChecked == true)
                sorted = counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key);
            else
                sorted = counts.OrderBy(x => x.Key);

            TagSummaryPanel.Children.Clear();
            _selectedSummaryTag = null;
            BtnDeleteSelectedSummaryTag.IsEnabled = false;

            int totalTags = counts.Count;
            int totalImages = _vm.Items.Count;
            TxtSummaryInfo.Text =
                $"（{totalTags} 種類 / {totalImages} 枚）";

            foreach (var kv in sorted)
                TagSummaryPanel.Children.Add(CreateSummaryTagChip(kv.Key, kv.Value));
        }

        private UIElement CreateSummaryTagChip(string tagName, int count)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#313149")),
                Tag = tagName,
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var nameText = new TextBlock
            {
                Text = tagName,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#CDD6F4")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var countBadge = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#89B4FA")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(5, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = count.ToString(),
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Colors.Black),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            };

            panel.Children.Add(nameText);
            panel.Children.Add(countBadge);
            border.Child = panel;

            border.MouseLeftButtonDown += (s, e) =>
                SelectSummaryTag(border, tagName);

            return border;
        }

        private void SelectSummaryTag(Border border, string tagName)
        {
            // 前の選択を解除
            foreach (var child in TagSummaryPanel.Children.OfType<Border>())
            {
                child.BorderThickness = new Thickness(0);
                child.BorderBrush = null;
            }

            if (_selectedSummaryTag == tagName)
            {
                // 同じタグを再クリック → 選択解除
                _selectedSummaryTag = null;
                BtnDeleteSelectedSummaryTag.IsEnabled = false;
                BtnDeleteSelectedSummaryTag.Content = "選択タグを全削除";
                TxtDeleteTag.Clear();
            }
            else
            {
                // 新しいタグを選択
                _selectedSummaryTag = tagName;
                border.BorderThickness = new Thickness(2);
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#F38BA8"));

                BtnDeleteSelectedSummaryTag.Content = $"「{tagName}」を全削除";
                BtnDeleteSelectedSummaryTag.IsEnabled = true;

                // 不要タグ削除欄にもセット
                TxtDeleteTag.Text = tagName;
            }
        }

        private void BtnDeleteSelectedSummaryTag_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedSummaryTag)) return;

            var targets = GetTargetItems();
            int hitCount = targets.Count(item =>
                item.Tags.Any(t => t.Name == _selectedSummaryTag));

            if (hitCount == 0)
            {
                SetStatus($"「{_selectedSummaryTag}」は見つかりませんでした");
                return;
            }

            string scope = ImageList.SelectedItems.Count > 0
                ? $"選択中 {targets.Count} 枚" : $"全 {targets.Count} 枚";

            var result = MessageBox.Show(
                $"{scope} の画像から\n「{_selectedSummaryTag}」を {hitCount} 件削除します。\n\nよろしいですか？",
                "タグ一括削除の確認",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;

            _vm.BulkDeleteTag(_selectedSummaryTag, targets);
            ReapplyFilter();
            RebuildTagSummary();      // 集計を更新
            SetStatus($"「{_selectedSummaryTag}」を {hitCount} 件削除しました");
            _selectedSummaryTag = null;
            BtnDeleteSelectedSummaryTag.IsEnabled = false;
            BtnDeleteSelectedSummaryTag.Content = "選択タグを全削除";
            TxtDeleteTag.Clear();
        }

        private void SortToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSortUpdating) return;
            if (TagSummaryPanel == null || _vm?.Items == null) return;

            _isSortUpdating = true;
            try
            {
                if (sender == SortByCount) SortByName.IsChecked = false;
                else SortByCount.IsChecked = false;
                // どちらも外れた場合は件数順をデフォルトに
                if (SortByCount.IsChecked != true && SortByName.IsChecked != true)
                    SortByCount.IsChecked = true;
                RebuildTagSummary();
            }
            finally { _isSortUpdating = false; }
        }

        private void SetStatus(string message) => TxtStatus.Text = message;
    }
}