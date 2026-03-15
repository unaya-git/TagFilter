using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.XPath;

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

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _settings.UnwantedTags = _settings.UnwantedTags
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            ImageList.ItemsSource = _vm.Items;
            AllItemsListView.ItemsSource = _displayItems;

            // 言語設定を復元
            if (_settings.Language == "English")
            {
                Strings.Language = AppLanguage.English;
                BtnLanguage.IsChecked = true;
                BtnLanguage.Content = "JP";
            }
            // アンダースコア設定を復元
            BtnUnderscore.IsChecked = _settings.UseUnderscores;
            BtnUnderscore.Content = _settings.UseUnderscores
                ? Strings.BtnUnderscoreOn : Strings.BtnUnderscoreOff;

            ApplyLocale();

            CmbDevice.SelectedIndex = Math.Min(_settings.DeviceIndex, 2);
            SliderThreshold.Value = Math.Max(0.1, Math.Min(0.9, _settings.Threshold));
            SetParallelCount(_settings.ParallelCount);

            InitModelComboBox();
            InitLoraComboBox();
            RegisterFilterEvents();
            RebuildUnwantedTagChips();

            Closing += MainWindow_Closing;
            Loaded += async (_, __) =>
            {
                await CheckGpuAsync();
                await HandleCommandLineArgsAsync();
            };

            //SetStatus(Strings.StatusReady);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _settings.Language = Strings.Language == AppLanguage.English ? "English" : "Japanese";
            _settings.ModelIndex = CmbModel.SelectedIndex;
            _settings.DeviceIndex = CmbDevice.SelectedIndex;
            _settings.ParallelCount = GetParallelCount();
            _settings.LoraModeIndex = CmbLoraMode.SelectedIndex;
            _settings.Threshold = SliderThreshold.Value;
            _settings.UseUnderscores = BtnUnderscore.IsChecked == true;
            _settings.Save();
        }

        // ── ドラッグ&ドロップ ───────────────────────────────────────────
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            var folder = paths.FirstOrDefault(p => Directory.Exists(p));
            if (folder == null) return;
            await LoadFolderByPathAsync(folder);
        }

        // ── 言語切替 ────────────────────────────────────────────────────
        private void BtnLanguage_Click(object sender, RoutedEventArgs e)
        {
            if (Strings.Language == AppLanguage.Japanese)
            {
                Strings.Language = AppLanguage.English;
                BtnLanguage.Content = "JP";
            }
            else
            {
                Strings.Language = AppLanguage.Japanese;
                BtnLanguage.Content = "EN";
            }
            ApplyLocale();
        }

        private void ApplyLocale()
        {
            // ── ツールバー ──
            BtnOpenFolder.Content = Strings.BtnOpenFolder;
            BtnSaveAll.Content = Strings.BtnSaveAll;
            BtnAutoTag.Content = Strings.BtnAutoTag;

            // デバイス選択（インデックス保持）
            int devIdx = CmbDevice.SelectedIndex;
            CmbDevice.Items.Clear();
            CmbDevice.Items.Add(Strings.DeviceAuto);
            CmbDevice.Items.Add(Strings.DeviceGpu);
            CmbDevice.Items.Add(Strings.DeviceCpu);
            CmbDevice.SelectedIndex = devIdx >= 0 ? devIdx : 0;

            // LoRAモード（インデックス保持）
            int loraIdx = CmbLoraMode.SelectedIndex;
            CmbLoraMode.Items.Clear();
            foreach (var label in Strings.LoraModeLabels)
                CmbLoraMode.Items.Add(label);
            CmbLoraMode.SelectedIndex = loraIdx >= 0 ? loraIdx : 0;

            // ── タグ集計パネル ──
            SortByCount.Content = Strings.SortByCount;
            SortByName.Content = Strings.SortByName;
            BtnDeleteSelectedSummaryTag.Content = Strings.BtnDeleteSelectedDefault;

            // ── 表示フィルタ ──
            FilterAll.Content = Strings.FilterAll;
            FilterFace.Content = Strings.FilterFace;
            FilterBody.Content = Strings.FilterBody;
            FilterOutfit.Content = Strings.FilterOutfit;
            FilterPose.Content = Strings.FilterPose;
            FilterBg.Content = Strings.FilterBackground;
            FilterStyle.Content = Strings.FilterStyle;
            FilterExpression.Content = Strings.FilterExpression;
            FilterCharacter.Content = Strings.FilterCharacter;
            FilterCopyright.Content = Strings.FilterCopyright;
            FilterArtist.Content = Strings.FilterArtist;
            FilterOther.Content = Strings.FilterOther;

            // ── 一括操作 ──
            BtnBulkInsert.Content = Strings.BtnInsert;
            BtnBulkDeleteCategory.Content = Strings.BtnDeleteCategoryDefault;
            BtnAddUnwantedTag.Content = Strings.BtnRegister;
            BtnBulkDeleteTag.Content = Strings.BtnDeleteAllTag;

            // ── タグ追加 ──
            BtnAddTag.Content = Strings.BtnAdd;

            // ── ラベル ──
            LblThreshold.Text = Strings.LblThreshold;
            LblModel.Text = Strings.LblModel;
            LblInfer.Text = Strings.LblInfer;
            LblDevice.Text = Strings.LblDevice;
            LblParallel.Text = Strings.LblParallel;
            LblTagSummary.Text = Strings.LblTagSummary;
            LblSortOrder.Text = Strings.LblSortOrder;
            LblFilter.Text = Strings.LblFilter;
            LblBulkInsert.Text = Strings.LblBulkInsert;
            LblUnwantedTag.Text = Strings.LblUnwantedTag;
            LblAddTag.Text = Strings.LblAddTag;

            // モデル名（インデックス保持）
            int modelIdx = CmbModel.SelectedIndex;
            CmbModel.Items.Clear();
            foreach (var name in Strings.ModelNames)
                CmbModel.Items.Add(name);
            CmbModel.SelectedIndex = modelIdx >= 0 ? modelIdx : 0;

            // ── アンダースコアボタン ──
            BtnUnderscore.Content = (BtnUnderscore.IsChecked == true)
                ? Strings.BtnUnderscoreOn : Strings.BtnUnderscoreOff;

            // ── ステータスバー ──
            SetStatus(Strings.StatusReady);

            UpdateModelStatus();
            UpdateDeleteButton();
        }

        // ── コマンドライン引数処理 ──────────────────────────────────────
        private async Task HandleCommandLineArgsAsync()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length < 2) return;

            string folderPath = args[1];
            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show($"{Strings.MsgFolderNotFound}\n{folderPath}",
                    Strings.MsgArgError, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 引数2: LoRAモード（数字のみ）
            if (args.Length >= 3 &&
                int.TryParse(args[2], out int modeIdx) &&
                modeIdx >= 0 && modeIdx < LoraModes.Length)
            {
                CmbLoraMode.SelectedIndex = modeIdx;
            }

            // 引数3: 一括挿入タグ（カンマ区切り）
            List<string> insertTags = new List<string>();
            if (args.Length >= 4 && !string.IsNullOrWhiteSpace(args[3]))
            {
                insertTags = args[3]
                    .Split(',')
                    .Select(t => t.Trim().ToLower().Replace(" ", "_"))
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
            }

            await LoadFolderByPathAsync(folderPath);
            await RunAutoTagAsync();

            if (insertTags.Any())
            {
                var targets = _vm.Items.ToList();
                foreach (var tag in insertTags)
                    _vm.BulkInsertTag(tag, targets);
                RebuildDisplayItems();
                SetStatus(Strings.StatusInsertDone(string.Join(", ", insertTags)));
            }

            await Task.Delay(500);
            Application.Current.Shutdown();
        }

        // ── GPU 可否チェック ────────────────────────────────────────────
        private async Task CheckGpuAsync()
        {
            SetGpuStatus(Strings.GpuChecking, "#6C7086");
            _gpuAvailable = await Task.Run(() => WD14TaggerService.IsDirectMLAvailable());

            if (_gpuAvailable)
            {
                SetGpuStatus(Strings.GpuAvailable, "#A6E3A1");
                if (CmbDevice.SelectedIndex <= 1) SetParallelCount(1);
            }
            else
            {
                SetGpuStatus(Strings.GpuCpuOnly, "#F38BA8");
                if (CmbDevice.SelectedIndex == 1) CmbDevice.SelectedIndex = 0;
                SetParallelCount(Math.Max(1, Environment.ProcessorCount / 2));
            }
        }

        private void SetGpuStatus(string text, string hexColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            GpuIndicator.Background = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B));
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
                case 0: case 1: SetParallelCount(1); break;
                case 2: SetParallelCount(Math.Max(1, Environment.ProcessorCount / 2)); break;
            }
        }

        // ── 並列数操作 ──────────────────────────────────────────────────
        private void SetParallelCount(int value)
        {
            _maxParallel = Math.Max(1, value);
            if (TxtParallel != null) TxtParallel.Text = _maxParallel.ToString();
        }

        private int GetParallelCount()
        {
            if (int.TryParse(TxtParallel.Text, out int v) && v >= 1) return v;
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
                default: return _gpuAvailable ? ExecutionDevice.Gpu : ExecutionDevice.Cpu;
            }
        }

        // ── モデル選択 ──────────────────────────────────────────────────
        private void InitModelComboBox()
        {
            foreach (var name in Strings.ModelNames)   // ← presetのNameではなくStrings.ModelNamesを使う
                CmbModel.Items.Add(name);
            CmbModel.SelectedIndex = Math.Min(_settings.ModelIndex, WD14ModelPreset.All.Length - 1);
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
                TxtModelStatus.Text = Strings.ModelInUse;
                SetIndicatorColor(ModelStatusIndicator, TxtModelStatus, "#A6E3A1");
            }
            else if (downloaded)
            {
                TxtModelStatus.Text = Strings.ModelDownloaded;
                SetIndicatorColor(ModelStatusIndicator, TxtModelStatus, "#89B4FA");
            }
            else
            {
                TxtModelStatus.Text = Strings.ModelNotDL;
                SetIndicatorColor(ModelStatusIndicator, TxtModelStatus, "#6C7086");
            }
            CmbModel.ToolTip = preset.Description;
        }

        private void SetIndicatorColor(Border border, TextBlock text, string hexColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            border.Background = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B));
            border.BorderBrush = new SolidColorBrush(color);
            border.BorderThickness = new Thickness(1);
            text.Foreground = new SolidColorBrush(color);
        }

        private void CmbModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateModelStatus();

        private WD14ModelPreset GetSelectedPreset()
        {
            int idx = CmbModel.SelectedIndex;
            if (idx < 0 || idx >= WD14ModelPreset.All.Length) return WD14ModelPreset.All[0];
            return WD14ModelPreset.All[idx];
        }

        private void InitLoraComboBox()
        {
            foreach (var mode in LoraModes)
                CmbLoraMode.Items.Add(mode.Label);
            CmbLoraMode.SelectedIndex = Math.Min(_settings.LoraModeIndex, LoraModes.Length - 1);
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
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#313149")),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 6, 3),
                Margin = new Thickness(0, 0, 4, 3),
                Cursor = Cursors.Hand,
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var label = new TextBlock
            {
                Text = tagName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.MouseLeftButtonDown += (s, e) => { TxtDeleteTag.Text = tagName; e.Handled = true; };

            var close = new TextBlock
            {
                Text = " ×",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8")),
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
            border.MouseLeftButtonDown += (s, e) => { TxtDeleteTag.Text = tagName; };
            return border;
        }

        private void BtnAddUnwantedTag_Click(object sender, RoutedEventArgs e)
        {
            var tag = TxtDeleteTag.Text.Trim();
            if (string.IsNullOrEmpty(tag)) return;
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
            { Description = Strings.BtnOpenFolder };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            await LoadFolderByPathAsync(dialog.SelectedPath);
        }

        private async Task LoadFolderByPathAsync(string folderPath)
        {
            SetStatus(Strings.StatusLoading);
            BtnOpenFolder.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    int onlineCount = Directory.GetFiles(folderPath)
                        .Count(f => FileHelper.IsOnlineOnly(f));
                    if (onlineCount > 0)
                        Dispatcher.Invoke(() => MessageBox.Show(
                            Strings.MsgOneDrive(onlineCount),
                            Strings.MsgOneDriveTitle,
                            MessageBoxButton.OK, MessageBoxImage.Warning));
                });

                await _vm.LoadFolderAsync(folderPath);
                await LoadThumbnailsAsync();
                TxtImageCount.Text = $"{_vm.Items.Count} 件";
                RebuildDisplayItems();
                SetStatus(Strings.StatusLoaded(_vm.Items.Count));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Strings.MsgFolderLoadError}\n{ex.Message}",
                    Strings.MsgError, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { BtnOpenFolder.IsEnabled = true; }
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
                    errors.AppendLine($"{Path.GetFileName(path)}: {ex.GetType().Name} - {ex.Message}");
                }
            }
            if (errors.Length > 0)
                MessageBox.Show(Strings.MsgThumbnailFailed + errors,
                    Strings.MsgThumbnailWarning, MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (_activeFilter.HasValue &&
                Strings.CategoryLabels.TryGetValue(_activeFilter.Value, out var label))
            {
                BtnBulkDeleteCategory.Content = Strings.BtnDeleteCategoryLabel(label);
                BtnBulkDeleteCategory.IsEnabled = true;
            }
            else
            {
                BtnBulkDeleteCategory.Content = Strings.BtnDeleteCategoryDefault;
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
            if (_currentItem == null) { SetStatus(Strings.StatusNoImage); return; }
            var tagName = TxtNewTag.Text.Trim().ToLower().Replace(" ", "_");
            if (string.IsNullOrEmpty(tagName)) return;
            if (_currentItem.HasTag(tagName))
            {
                SetStatus(Strings.StatusTagExists(tagName));
                TxtNewTag.SelectAll();
                return;
            }
            var category = new TagClassifier().Classify(tagName);
            _currentItem.Tags.Insert(0, new TagViewModel(tagName, 1.0f, category));
            _currentItem.SaveTagsToFile();
            ReapplyFilter();
            RebuildTagSummary();
            SetStatus(Strings.StatusTagAdded(tagName, _currentItem.TagCount));
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
            if (string.IsNullOrEmpty(tagName)) { SetStatus(Strings.StatusNoTagInput); return; }
            var targets = GetTargetItems();
            _vm.BulkInsertTag(tagName, targets);
            ReapplyFilter();
            string scope = ImageList.SelectedItems.Count > 0
                ? Strings.ScopeSelected(targets.Count)
                : Strings.ScopeAll(targets.Count);
            SetStatus(Strings.StatusInserted(tagName, scope));
            TxtBulkTag.Clear();
        }

        // ── カテゴリ一括削除 ────────────────────────────────────────────
        private void BtnBulkDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (!_activeFilter.HasValue) return;
            Strings.CategoryLabels.TryGetValue(_activeFilter.Value, out var label);
            var targets = GetTargetItems();
            string scope = ImageList.SelectedItems.Count > 0
                ? Strings.ScopeSelected(targets.Count)
                : Strings.ScopeAll(targets.Count);
            var result = MessageBox.Show(
                Strings.MsgBulkDeleteCategory(scope, label),
                Strings.MsgBulkDeleteTitle,
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;
            _vm.BulkDeleteByCategory(_activeFilter.Value, targets);
            ReapplyFilter();
            SetStatus(Strings.StatusCategoryDeleted(label, scope));
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
            SetStatus(Strings.StatusSavedAll(count));
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
            string localModelName = Strings.ModelNames[CmbModel.SelectedIndex];
            bool needLoad = !_vm.IsTaggerReady || _vm.CurrentPreset != preset;

            if (needLoad)
            {
                bool modelExists = File.Exists(preset.OnnxPath) && File.Exists(preset.CsvPath);
                if (!modelExists)
                {
                    var dlgResult = MessageBox.Show(
                        Strings.MsgDownload(localModelName),
                        Strings.MsgDownloadTitle,
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                        SetStatus(p.Message);
                    });

                    try
                    {
                        bool ok = await _vm.EnsureAndLoadModelAsync(
                            preset, device, dlProgress, _downloadCts.Token);
                        if (!ok) { SetStatus(Strings.StatusModelFailed); return; }
                        SetStatus(Strings.StatusModelLoaded(localModelName));
                        UpdateModelStatus();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"{Strings.MsgDownloadFailed}\n{ex.Message}",
                            Strings.MsgError, MessageBoxButton.OK, MessageBoxImage.Error);
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
                    try
                    {
                        SetStatus(Strings.StatusModelSwitching(localModelName));
                        bool ok = await Task.Run(
                            () => _vm.LoadModel(preset.OnnxPath, preset.CsvPath, device));
                        if (!ok) { SetStatus(Strings.StatusModelSwFailed); return; }
                        UpdateModelStatus();
                        SetStatus(Strings.StatusModelSwitched(localModelName));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, Strings.MsgModelSwitchError,
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }
            else
            {
                if (_vm.LastDevice != device)
                {
                    try { _vm.ReloadWithDevice(device); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, Strings.MsgModelReloadError,
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            await RunAutoTagAsync();
        }

        private async Task RunAutoTagAsync()
        {
            var device = GetExecutionDevice();
            var preset = GetSelectedPreset();

            if (!_vm.IsTaggerReady || _vm.CurrentPreset != preset)
            {
                bool ok = await Task.Run(
                    () => _vm.LoadModel(preset.OnnxPath, preset.CsvPath, device));
                if (!ok) { SetStatus(Strings.StatusModelFailed); return; }
                UpdateModelStatus();
            }

            var targets = GetTargetItems();
            if (!targets.Any()) { SetStatus(Strings.StatusNoFolder); return; }

            var keepCategories = GetSelectedLoraCategories();
            bool useGpu = GetExecutionDevice() != ExecutionDevice.Cpu;
            int parallel = useGpu ? 1 : GetParallelCount();
            string deviceLabel = useGpu ? "DirectML" : "CPU";
            string modeName = Strings.LoraModeLabels[CmbLoraMode.SelectedIndex];

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
                SetStatus(Strings.StatusInferring(deviceLabel, parallel, v, targets.Count));
            });

            try
            {
                await _vm.BatchTagAsync(targets, (float)SliderThreshold.Value,
                    progress, keepCategories, parallel);
                RebuildDisplayItems();
                SetStatus(Strings.StatusDone(modeName, deviceLabel, parallel, targets.Count));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Strings.MsgInferError}\n{ex.Message}",
                    Strings.MsgError, MessageBoxButton.OK, MessageBoxImage.Error);
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

        // ── 不要タグ一括削除 ────────────────────────────────────────────
        private void TxtDeleteTag_TextChanged(object sender, TextChangedEventArgs e)
            => BtnBulkDeleteTag.IsEnabled = !string.IsNullOrWhiteSpace(TxtDeleteTag.Text);

        private void TxtDeleteTag_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoBulkDeleteTag();
        }
        private void BtnBulkDeleteTag_Click(object sender, RoutedEventArgs e)
            => DoBulkDeleteTag();

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
            if (string.IsNullOrEmpty(tagName)) { SetStatus(Strings.StatusNoDelInput); return; }

            var targets = GetTargetItems();
            int hitCount = targets.Count(item => item.Tags.Any(t => t.Name == tagName));

            if (hitCount == 0)
            {
                SetStatus(Strings.StatusTagNotFound(tagName));
                TxtDeleteTag.Clear();
                return;
            }

            string scope = ImageList.SelectedItems.Count > 0
                ? Strings.ScopeSelected(targets.Count)
                : Strings.ScopeAll(targets.Count);

            var result = MessageBox.Show(
                Strings.MsgBulkDeleteTag(scope, tagName, hitCount),
                Strings.MsgBulkDeleteTitle,
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;

            _vm.BulkDeleteTag(tagName, targets);
            ReapplyFilter();
            SetStatus(Strings.StatusTagDeleted(tagName, hitCount));
            TxtDeleteTag.Clear();
        }

        // ── タグ集計パネル ───────────────────────────────────────────────
        private string _selectedSummaryTag = null;
        private bool _isSortUpdating = false;

        public void RebuildTagSummary()
        {
            var counts = new Dictionary<string, int>();
            foreach (var item in _vm.Items)
                foreach (var tag in item.Tags)
                {
                    if (!counts.ContainsKey(tag.Name)) counts[tag.Name] = 0;
                    counts[tag.Name]++;
                }

            IEnumerable<KeyValuePair<string, int>> sorted;
            if (SortByCount.IsChecked == true)
                sorted = counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key);
            else
                sorted = counts.OrderBy(x => x.Key);

            TagSummaryPanel.Children.Clear();
            _selectedSummaryTag = null;
            BtnDeleteSelectedSummaryTag.IsEnabled = false;
            BtnDeleteSelectedSummaryTag.Content = Strings.BtnDeleteSelectedDefault;

            //TxtSummaryInfo.Text = $"（{counts.Count} 種類 / {_vm.Items.Count} 枚）";
            TxtSummaryInfo.Text = Strings.TxtSummaryInfo(counts.Count, _vm.Items.Count);

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
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#313149")),
                Tag = tagName,
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var nameText = new TextBlock
            {
                Text = tagName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var countBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(5, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = count.ToString(),
                    Foreground = new SolidColorBrush(Colors.Black),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            };
            panel.Children.Add(nameText);
            panel.Children.Add(countBadge);
            border.Child = panel;
            border.MouseLeftButtonDown += (s, e) => SelectSummaryTag(border, tagName);
            return border;
        }

        private void SelectSummaryTag(Border border, string tagName)
        {
            foreach (var child in TagSummaryPanel.Children.OfType<Border>())
            {
                child.BorderThickness = new Thickness(0);
                child.BorderBrush = null;
            }

            if (_selectedSummaryTag == tagName)
            {
                _selectedSummaryTag = null;
                BtnDeleteSelectedSummaryTag.IsEnabled = false;
                BtnDeleteSelectedSummaryTag.Content = Strings.BtnDeleteSelectedDefault;
                TxtDeleteTag.Clear();
            }
            else
            {
                _selectedSummaryTag = tagName;
                border.BorderThickness = new Thickness(2);
                border.BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#F38BA8"));
                BtnDeleteSelectedSummaryTag.Content = Strings.BtnDeleteSelectedLabel(tagName);
                BtnDeleteSelectedSummaryTag.IsEnabled = true;
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
                SetStatus(Strings.StatusTagNotFound(_selectedSummaryTag));
                return;
            }

            string scope = ImageList.SelectedItems.Count > 0
                ? Strings.ScopeSelected(targets.Count)
                : Strings.ScopeAll(targets.Count);

            var result = MessageBox.Show(
                Strings.MsgBulkDeleteTag(scope, _selectedSummaryTag, hitCount),
                Strings.MsgBulkDeleteTitle,
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;

            _vm.BulkDeleteTag(_selectedSummaryTag, targets);
            ReapplyFilter();
            RebuildTagSummary();
            SetStatus(Strings.StatusTagDeleted(_selectedSummaryTag, hitCount));
            _selectedSummaryTag = null;
            BtnDeleteSelectedSummaryTag.IsEnabled = false;
            BtnDeleteSelectedSummaryTag.Content = Strings.BtnDeleteSelectedDefault;
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
                if (SortByCount.IsChecked != true && SortByName.IsChecked != true)
                    SortByCount.IsChecked = true;
                RebuildTagSummary();
            }
            finally { _isSortUpdating = false; }
        }

        private void BtnUnderscore_Click(object sender, RoutedEventArgs e)
        {
            bool useUnder = BtnUnderscore.IsChecked == true;
            BtnUnderscore.Content = useUnder
                ? Strings.BtnUnderscoreOn : Strings.BtnUnderscoreOff;
            _settings.UseUnderscores = useUnder;
            AppSettings.Current.UseUnderscores = useUnder;
        }


        private void SetStatus(string message) => TxtStatus.Text = message;
    }
}