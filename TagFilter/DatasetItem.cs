using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace TagFilter{
    public class DatasetItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── 基本プロパティ ─────────────────────────
        public string ImagePath { get; set; }

        public string TxtPath
        {
            get { return Path.ChangeExtension(ImagePath, ".txt"); }
        }

        public string FileName
        {
            get { return Path.GetFileName(ImagePath); }
        }

        // Tags変更時に TagCount も通知する
        public int TagCount
        {
            get { return _tags.Count; }
        }

        // ── サムネイル ────────────────────────────
        private BitmapImage _thumbnail;
        public BitmapImage Thumbnail
        {
            get { return _thumbnail; }
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        // ── タグコレクション ──────────────────────
        private ObservableCollection<TagViewModel> _tags
            = new ObservableCollection<TagViewModel>();

        public ObservableCollection<TagViewModel> Tags
        {
            get { return _tags; }
            set
            {
                // 旧コレクションのイベント解除
                if (_tags != null)
                    _tags.CollectionChanged -= Tags_CollectionChanged;

                _tags = value;

                // 新コレクションのイベント登録
                if (_tags != null)
                    _tags.CollectionChanged += Tags_CollectionChanged;

                OnPropertyChanged();
                OnPropertyChanged("TagCount");
                OnPropertyChanged("TagsAsText");
            }
        }

        private void Tags_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged("TagCount"); // ★左パネルのtag数を更新
            OnPropertyChanged("TagsAsText");
        }

        public DatasetItem()
        {
            // コンストラクタでもイベント登録
            _tags.CollectionChanged += Tags_CollectionChanged;
        }

        // ── タグのテキスト表現 ────────────────────
        public string TagsAsText
        {
            get { return string.Join(", ", _tags.Select(t => t.Name)); }
        }

        // ── ファイルI/O ───────────────────────────
        private static readonly TagClassifier _classifier = new TagClassifier();

        public void LoadTagsFromFile()
        {
            _tags.Clear();
            if (!File.Exists(TxtPath)) return;

            var text = File.ReadAllText(TxtPath).Trim();
            if (string.IsNullOrEmpty(text)) return;

            // カンマ区切りで分割、空白タグはスキップ
            foreach (var raw in text.Split(','))
            {
                var name = raw.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var category = _classifier.Classify(name);
                _tags.Add(new TagViewModel(name, 1.0f, category));
            }
        }

        public void SaveTagsToFile()
        {
            // タグが0件でもファイルは作成（空ファイル）
            File.WriteAllText(TxtPath, TagsAsText);
        }

        // タグが存在するか確認
        public bool HasTag(string name)
        {
            return _tags.Any(t => t.Name == name);
        }
    }
}