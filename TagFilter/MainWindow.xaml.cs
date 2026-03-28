using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

            // txt保存設定
            BtnSaveTxt.IsChecked = _settings.SaveTxt;
            BtnSaveTxt.Content = _settings.SaveTxt ? "保存 ON" : "保存 OFF";
            ApplyLocale();

            CmbDevice.SelectedIndex = Math.Min(_settings.DeviceIndex, 2);
            SliderThreshold.Value = Math.Max(0.1, Math.Min(0.9, _settings.Threshold));
            SetParallelCount(_settings.ParallelCount);

            InitModelComboBox();
            InitLoraComboBox();
            RegisterFilterEvents();
            RebuildUnwantedTagChips();

            _loraSettings = LoraTrainingSettings.Load();
            LoadLoraSettingsToUI();

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
            _settings.SaveTxt = BtnSaveTxt.IsChecked == true;
            ReadLoraSettingsFromUI().Save();
            _settings.Save();
        }

        // ── ドラッグ&ドロップ ───────────────────────────────────────────
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /*
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            var folder = paths.FirstOrDefault(p => Directory.Exists(p));
            if (folder == null) return;
            await LoadFolderByPathAsync(folder);
        }
        */
        /*
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

            // フォルダを優先、なければファイルのあるフォルダを使用
            var folder = paths.FirstOrDefault(p => Directory.Exists(p));
            if (folder == null)
            {
                // ファイルが渡された場合はその親フォルダを使用
                var file = paths.FirstOrDefault(p => File.Exists(p) &&
                    new[] { ".jpg", ".jpeg", ".png", ".webp" }
                    .Contains(Path.GetExtension(p).ToLower()));
                if (file != null)
                    folder = Path.GetDirectoryName(file);
            }

            if (folder == null) return;
            await LoadFolderByPathAsync(folder);
        }
        */
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

            // ドロップされた中にフォルダが含まれているか確認
            var folder = paths.FirstOrDefault(p => Directory.Exists(p));

            if (folder != null)
            {
                // フォルダが含まれていれば、従来通りフォルダ丸ごと読み込み
                await LoadFolderByPathAsync(folder);
            }
            else
            {
                // フォルダがなく、ファイルのみがドロップされた場合
                var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var imageFiles = paths.Where(p => File.Exists(p) &&
                    validExtensions.Contains(Path.GetExtension(p).ToLower()))
                    .ToArray();

                // 対象の画像ファイルが存在すればファイル専用の読み込みを行う
                if (imageFiles.Any())
                {
                    await LoadFilesByPathsAsync(imageFiles);
                }
            }
        }
        private async Task LoadFilesByPathsAsync(string[] filePaths)
        {
            SetStatus(Strings.StatusLoading);
            BtnOpenFolder.IsEnabled = false;
            try
            {
                // MainViewModel側で、渡されたファイル群だけを読み込むメソッドを呼び出す
                await _vm.LoadFilesAsync(filePaths);

                await LoadThumbnailsAsync();
                TxtImageCount.Text = $"{_vm.Items.Count} 件";
                RebuildDisplayItems();
                SetStatus(Strings.StatusLoaded(_vm.Items.Count));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルの読み込みに失敗しました\n{ex.Message}",
                    Strings.MsgError, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { BtnOpenFolder.IsEnabled = true; }
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

            // 引数4: LoRA output_name（指定があればLoRA学習を実行）
            if (args.Length >= 5 && !string.IsNullOrWhiteSpace(args[4]))
            {
                _loraSettings = LoraTrainingSettings.Load();
                _loraSettings.OutputName = args[4].Trim();

                if (string.IsNullOrEmpty(_loraSettings.KohyaPath) ||
                    !Directory.Exists(_loraSettings.KohyaPath))
                {
                    AppendLoraLog("[ERROR] kohya_ss path not set. Run GUI first to configure.");
                }
                else if (string.IsNullOrEmpty(_loraSettings.BaseModelPath) ||
                         !File.Exists(_loraSettings.BaseModelPath))
                {
                    AppendLoraLog("[ERROR] Base model not found.");
                }
                else
                {
                    SetStatus("LoRA training...");
                    // UIなしでBtnStartTraining_Clickと同じロジックを実行
                    await Task.Run(() => Dispatcher.Invoke(() => BtnStartTraining_Click(null, null)));
                    // 学習完了を待つ
                    while (_trainingProcess != null && !_trainingProcess.HasExited)
                        await Task.Delay(1000);
                }
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
        /*
        private void BtnSaveAll_Click(object sender, RoutedEventArgs e)
        {
            int count = 0;
            foreach (var item in _vm.Items) { item.SaveTagsToFile(); count++; }
            SetStatus(Strings.StatusSavedAll(count));
        }*/
        private void BtnSaveAll_Click(object sender, RoutedEventArgs e)
        {
            int count = 0;
            foreach (var item in _vm.Items)
            {
                item.SaveTagsToFile(true); 
                count++;
            }
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
            //string localModelName = Strings.ModelNames[CmbModel.SelectedIndex];
            int modelIdx = CmbModel.SelectedIndex;
            string localModelName = (modelIdx >= 0 && modelIdx < Strings.ModelNames.Length)
                ? Strings.ModelNames[modelIdx]
                : "Unknown";
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

        #region LoRA作成
        // ── LoRA Training フィールド ──────────────────────────────────────
        private LoraTrainingSettings _loraSettings;
        private Process _trainingProcess;


        private void LoadLoraSettingsToUI()
        {
            var s = _loraSettings;
            if (s == null) return;

            TxtKohyaPath.Text = s.KohyaPath;
            TxtBaseModel.Text = s.BaseModelPath;
            TxtOutputDir.Text = s.OutputDir;
            TxtOutputName.Text = s.OutputName;
            TxtRepeats.Text = s.Repeats.ToString();

            TxtNetworkDim.Text = s.NetworkDim.ToString();
            TxtNetworkAlpha.Text = s.NetworkAlpha.ToString();
            TxtMaxEpochs.Text = s.MaxTrainEpochs.ToString();
            TxtBatchSize.Text = s.TrainBatchSize.ToString();
            TxtSaveEvery.Text = s.SaveEveryNEpochs.ToString();
            SetLoraComboByContent(CmbResolution, s.Resolution.ToString());
            ChkEnableBucket.IsChecked = s.EnableBucket;
            ChkBucketNoUpscale.IsChecked = s.BucketNoUpscale;

            TxtLr.Text = s.LearningRate;
            TxtUnetLr.Text = s.UnetLr;
            TxtTeLr.Text = s.TextEncoderLr;
            SetLoraComboByContent(CmbLrScheduler, s.LrScheduler);
            TxtWarmupSteps.Text = s.LrWarmupSteps.ToString();
            TxtNumCycles.Text = s.LrSchedulerNumCycles.ToString();

            SetLoraComboByContent(CmbOptimizer, s.OptimizerType);
            SetLoraComboByContent(CmbMixedPrecision, s.MixedPrecision);
            SetLoraComboByContent(CmbSavePrecision, s.SavePrecision);
            ChkGradCheck.IsChecked = s.GradientCheckpointing;
            TxtNumCpuThreads.Text = s.NumCpuThreads.ToString();

            TxtNoiseOffset.Text = s.NoiseOffset.ToString("F2");
            TxtMinSnr.Text = s.MinSnrGamma.ToString("F1");
            TxtScaleWeightNorms.Text = s.ScaleWeightNorms.ToString("F1");
            ChkShuffleCaption.IsChecked = s.ShuffleCaption;
            TxtKeepTokens.Text = s.KeepTokens.ToString();
            TxtClipSkip.Text = s.ClipSkip.ToString();
            ChkNoHalfVae.IsChecked = s.NoHalfVae;

            SetLoraComboByContent(CmbAttentionMode, s.AttentionMode);
            ChkCacheLatents.IsChecked = s.CacheLatents;
            ChkCacheLatentsToDisk.IsChecked = s.CacheLatentsToDisk;

            // Training data dir
            string folder = _vm.Items.Count > 0
                ? Path.GetDirectoryName(_vm.Items[0].ImagePath) : "";
            TxtTrainDataDir.Text = string.IsNullOrEmpty(folder)
                ? "(Open a folder in the main window)" : folder;
        }

        private LoraTrainingSettings ReadLoraSettingsFromUI()
        {
            var s = _loraSettings ?? new LoraTrainingSettings();

            s.KohyaPath = TxtKohyaPath.Text.Trim();
            s.BaseModelPath = TxtBaseModel.Text.Trim();
            s.OutputDir = TxtOutputDir.Text.Trim();
            s.OutputName = TxtOutputName.Text.Trim();
            s.Repeats = ParseLoraInt(TxtRepeats.Text, 10);

            s.NetworkDim = ParseLoraInt(TxtNetworkDim.Text, 32);
            s.NetworkAlpha = ParseLoraInt(TxtNetworkAlpha.Text, 16);

            s.MaxTrainEpochs = ParseLoraInt(TxtMaxEpochs.Text, 10);
            s.TrainBatchSize = ParseLoraInt(TxtBatchSize.Text, 1);
            s.SaveEveryNEpochs = ParseLoraInt(TxtSaveEvery.Text, 2);
            s.Resolution = ParseLoraInt(
                (CmbResolution.SelectedItem as ComboBoxItem)?.Content?.ToString(), 1024);
            s.EnableBucket = ChkEnableBucket.IsChecked == true;
            s.BucketNoUpscale = ChkBucketNoUpscale.IsChecked == true;

            s.LearningRate = TxtLr.Text.Trim();
            s.UnetLr = TxtUnetLr.Text.Trim();
            s.TextEncoderLr = TxtTeLr.Text.Trim();
            s.LrScheduler = (CmbLrScheduler.SelectedItem as ComboBoxItem)
                                      ?.Content?.ToString() ?? "cosine_with_restarts";
            s.LrWarmupSteps = ParseLoraInt(TxtWarmupSteps.Text, 0);
            s.LrSchedulerNumCycles = ParseLoraInt(TxtNumCycles.Text, 1);

            s.OptimizerType = (CmbOptimizer.SelectedItem as ComboBoxItem)
                                      ?.Content?.ToString() ?? "AdamW8bit";
            s.MixedPrecision = (CmbMixedPrecision.SelectedItem as ComboBoxItem)
                                      ?.Content?.ToString() ?? "bf16";
            s.SavePrecision = (CmbSavePrecision.SelectedItem as ComboBoxItem)
                                      ?.Content?.ToString() ?? "bf16";
            s.GradientCheckpointing = ChkGradCheck.IsChecked == true;
            s.NumCpuThreads = ParseLoraInt(TxtNumCpuThreads.Text, 2);

            s.NoiseOffset = ParseLoraDouble(TxtNoiseOffset.Text, 0.0);
            s.MinSnrGamma = ParseLoraDouble(TxtMinSnr.Text, 5.0);
            s.ScaleWeightNorms = ParseLoraDouble(TxtScaleWeightNorms.Text, 1.0);
            s.ShuffleCaption = ChkShuffleCaption.IsChecked == true;
            s.KeepTokens = ParseLoraInt(TxtKeepTokens.Text, 1);
            s.ClipSkip = ParseLoraInt(TxtClipSkip.Text, 1);
            s.NoHalfVae = ChkNoHalfVae.IsChecked == true;

            s.AttentionMode = (CmbAttentionMode.SelectedItem as ComboBoxItem)
                        ?.Content?.ToString() ?? "sdpa";
            s.CacheLatents = ChkCacheLatents.IsChecked == true;
            s.CacheLatentsToDisk = ChkCacheLatentsToDisk.IsChecked == true;

            _loraSettings = s;
            return s;
        }

        // ── Preset ──────────────────────────────────────────────────────
        private void BtnPresetAnime_Click(object sender, RoutedEventArgs e)
        {
            if (_loraSettings == null) _loraSettings = new LoraTrainingSettings();
            ReadLoraSettingsFromUI();
            _loraSettings.ApplyPreset(LoraTrainingSettings.AnimePreset);
            LoadLoraSettingsToUI();
            TxtPresetName.Text = "Anime SDXL preset applied";
        }

        private void BtnPresetPhoto_Click(object sender, RoutedEventArgs e)
        {
            if (_loraSettings == null) _loraSettings = new LoraTrainingSettings();
            ReadLoraSettingsFromUI();
            _loraSettings.ApplyPreset(LoraTrainingSettings.PhotoPreset);
            LoadLoraSettingsToUI();
            TxtPresetName.Text = "Photo SDXL preset applied";
        }

        // ── Browse ──────────────────────────────────────────────────────
        private void BtnBrowseKohya_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            { Description = "Select kohya_ss folder" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtKohyaPath.Text = dlg.SelectedPath;
        }

        private void BtnBrowseModel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select base model",
                Filter = "Model (*.safetensors;*.ckpt)|*.safetensors;*.ckpt"
            };
            if (dlg.ShowDialog() == true)
                TxtBaseModel.Text = dlg.FileName;
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            { Description = "Select output folder" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtOutputDir.Text = dlg.SelectedPath;
        }

        // ── Training ────────────────────────────────────────────────────
        private void BtnStartTraining_Click(object sender, RoutedEventArgs e)
        {
            var s = ReadLoraSettingsFromUI();


            // Validate
            if (string.IsNullOrEmpty(s.KohyaPath) || !Directory.Exists(s.KohyaPath))
            { AppendLoraLog("[ERROR] kohya_ss folder not found."); return; }
            if (string.IsNullOrEmpty(s.BaseModelPath) || !File.Exists(s.BaseModelPath))
            { AppendLoraLog("[ERROR] Base model not found."); return; }

            string sourceDir = _vm.Items.Count > 0
                ? Path.GetDirectoryName(_vm.Items[0].ImagePath) : "";
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            { AppendLoraLog("[ERROR] Open a folder in the main window first."); return; }
            if (string.IsNullOrEmpty(s.OutputDir))
            { AppendLoraLog("[ERROR] output_dir is empty."); return; }

            // kohya_ss用サブフォルダを作成（repeats_概念名）
            string conceptName = string.IsNullOrEmpty(s.OutputName) ? "concept" : s.OutputName;
            string subFolderName = $"{s.Repeats}_{conceptName}";
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string trainDir = Path.Combine(appDir, "__kohya_train__", subFolderName);
            string trainDataDir = Path.Combine(appDir, "__kohya_train__");            // train_data_dirはサブフォルダの親を渡す


            // trainDir作成前に既存の__kohya_train__を削除
            if (Directory.Exists(trainDataDir))
                Directory.Delete(trainDataDir, true);
            Directory.CreateDirectory(trainDir);

            // 画像とtxtをサブフォルダにコピー
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".txt" };
            foreach (var file in Directory.GetFiles(sourceDir)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower())))
            {
                string dest = Path.Combine(trainDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                    File.Copy(file, dest);
            }
            AppendLoraLog($"[INFO] Train dir: {trainDir}");


            Directory.CreateDirectory(s.OutputDir);

            string accelerate = Path.Combine(s.KohyaPath, "venv", "Scripts", "accelerate.exe");
            string trainScript = Path.Combine(s.KohyaPath, "sd-scripts", "sdxl_train_network.py");



            if (!File.Exists(accelerate))
            { AppendLoraLog($"[ERROR] accelerate.exe not found:\n  {accelerate}"); return; }
            if (!File.Exists(trainScript))
            { AppendLoraLog($"[ERROR] sdxl_train_network.py not found:\n  {trainScript}"); return; }

            var args = new System.Text.StringBuilder();
            args.Append($"launch --num_cpu_threads_per_process {s.NumCpuThreads} ");
            args.Append($"\"{trainScript}\" ");
            args.Append($"--pretrained_model_name_or_path \"{s.BaseModelPath}\" ");
            args.Append($"--train_data_dir \"{trainDataDir}\" ");
            args.Append($"--output_dir \"{s.OutputDir}\" ");
            args.Append($"--output_name \"{s.OutputName}\" ");
            args.Append("--network_module networks.lora ");
            args.Append($"--network_dim {s.NetworkDim} ");
            args.Append($"--network_alpha {s.NetworkAlpha} ");
            args.Append($"--max_train_epochs {s.MaxTrainEpochs} ");
            args.Append($"--train_batch_size {s.TrainBatchSize} ");
            args.Append($"--save_every_n_epochs {s.SaveEveryNEpochs} ");
            args.Append($"--resolution {s.Resolution},{s.Resolution} ");
            if (s.EnableBucket) args.Append("--enable_bucket ");
            if (s.BucketNoUpscale) args.Append("--bucket_no_upscale ");
            args.Append($"--learning_rate {s.LearningRate} ");
            args.Append($"--unet_lr {s.UnetLr} ");
            args.Append($"--text_encoder_lr {s.TextEncoderLr} ");
            args.Append($"--lr_scheduler {s.LrScheduler} ");
            args.Append($"--lr_warmup_steps {s.LrWarmupSteps} ");
            args.Append($"--lr_scheduler_num_cycles {s.LrSchedulerNumCycles} ");
            args.Append($"--optimizer_type {s.OptimizerType} ");
            args.Append($"--mixed_precision {s.MixedPrecision} ");
            args.Append($"--save_precision {s.SavePrecision} ");
            if (s.GradientCheckpointing) args.Append("--gradient_checkpointing ");
            if (s.NoiseOffset > 0) args.Append($"--noise_offset {s.NoiseOffset:F2} ");
            if (s.MinSnrGamma > 0) args.Append($"--min_snr_gamma {s.MinSnrGamma:F1} ");
            if (s.ScaleWeightNorms > 0) args.Append($"--scale_weight_norms {s.ScaleWeightNorms:F1} ");
            if (s.ShuffleCaption) args.Append("--shuffle_caption ");
            if (s.KeepTokens > 0) args.Append($"--keep_tokens {s.KeepTokens} ");
            if (s.ClipSkip > 1) args.Append($"--clip_skip {s.ClipSkip} ");
            //if (s.NoHalfVae) args.Append("--no_half_vae ");
            args.Append("--caption_extension .txt ");
            //args.Append("--sdpa ");  // PyTorch標準のSDPAを使用
            //args.Append("--full_bf16 ");
            // args.Append("--cache_latents ");     // VRAMに詰め込むので、外してみる
            //args.Append("--cache_latents_to_disk "); // ディスク保存でVRAM節約・・・らしい（SSDが死ぬ？）
            switch (s.AttentionMode)
            {
                case "sdpa": args.Append("--sdpa "); break;
                case "xformers": args.Append("--xformers "); break;
                    // "none" は何も追加しない
            }
            args.Append("--full_bf16 ");
            if (s.CacheLatents) args.Append("--cache_latents ");
            if (s.CacheLatentsToDisk) args.Append("--cache_latents_to_disk ");

            args.Append("--max_data_loader_n_workers 1 ");   
            args.Append("--persistent_data_loader_workers ");  

            AppendLoraLog($"[START] {DateTime.Now:HH:mm:ss}");
            AppendLoraLog("──────────────────────────────");

            _trainingProcess = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = accelerate,
                    Arguments = args.ToString(),
                    WorkingDirectory = s.KohyaPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };

            _trainingProcess.OutputDataReceived += (_, ev) =>
            {
                if (ev.Data != null) Dispatcher.Invoke(() => AppendLoraLog(ev.Data));
            };
            _trainingProcess.ErrorDataReceived += (_, ev) =>
            {
                if (ev.Data != null) Dispatcher.Invoke(() => AppendLoraLog("[W] " + ev.Data));
            };
            _trainingProcess.Exited += (_, __) => Dispatcher.Invoke(() =>
            {
                int code = _trainingProcess?.ExitCode ?? -1;
                AppendLoraLog("──────────────────────────────");
                AppendLoraLog(code == 0
                    ? $"[DONE] Output: {s.OutputDir}"
                    : $"[ERROR] Exit code {code}");
                SetLoraTrainingState(false);
                _trainingProcess = null;
            });

            try
            {
                _trainingProcess.Start();
                _trainingProcess.BeginOutputReadLine();
                _trainingProcess.BeginErrorReadLine();
                SetLoraTrainingState(true);
            }
            catch (Exception ex)
            {
                AppendLoraLog($"[ERROR] {ex.Message}");
                _trainingProcess = null;
            }
            
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_trainingProcess != null && !_trainingProcess.HasExited)
                {
                    // 子プロセスも含めて強制終了
                    var job = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/F /T /PID {_trainingProcess.Id}",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    job.Start();
                    job.WaitForExit(3000);
                    AppendLoraLog("[STOPPED] Stopped by user.");
                }
            }
            catch (Exception ex)
            {
                AppendLoraLog($"[ERROR] {ex.Message}");
            }
        }

        private void SetLoraTrainingState(bool running)
        {
            BtnStartTraining.IsEnabled = !running;
            BtnStop.IsEnabled = running;
            TxtTrainStatus.Text = running ? "Training..." : "";
        }

        private void AppendLoraLog(string line)
        {
            TxtLog.AppendText(line + "\n");
            LogScrollViewer.ScrollToEnd();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
            => TxtLog.Clear();

        // ── Helpers ─────────────────────────────────────────────────────
        private static int ParseLoraInt(string s, int def)
            => int.TryParse(s, out int v) ? v : def;

        private static double ParseLoraDouble(string s, double def)
            => double.TryParse(s,
               System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture,
               out double v) ? v : def;

        private static void SetLoraComboByContent(ComboBox combo, string value)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Content?.ToString() == value)
                { combo.SelectedItem = item; return; }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        // 表示タグをクリップボードにコピー
        private void BtnCopyTags_Click(object sender, RoutedEventArgs e)
        {
            // Bindingされた DatasetItemView を受け取る
            var view = (sender as FrameworkElement)?.Tag as DatasetItemView;
            if (view == null || view.FilteredTags == null || view.FilteredTags.Count == 0) return;

            bool useUnder = AppSettings.Current.UseUnderscores;

            // Item.Tags ではなく、フィルタリング済みの view.FilteredTags を使う
            var tagLine = string.Join(", ", view.FilteredTags.Select(t =>
                useUnder ? t.Name : t.Name.Replace("_", " ")));

            Clipboard.SetText(tagLine);
            SetStatus($"Copied: {view.Item.FileName} ({view.FilteredTags.Count} view tags)");
        }

        // txt保存トグルボタンの処理
        private void BtnSaveTxt_Click(object sender, RoutedEventArgs e)
        {
            bool save = BtnSaveTxt.IsChecked == true;
            BtnSaveTxt.Content = save ? "保存 ON" : "保存 OFF";
            _settings.SaveTxt = save;

            if (AppSettings.Current != null)
            {
                AppSettings.Current.SaveTxt = save;
            }
        }

        #endregion LoRA作成

        private void SetStatus(string message) => TxtStatus.Text = message;
    }
}