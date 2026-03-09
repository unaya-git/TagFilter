
namespace TagFilter
{
    public class TagViewModel : ViewModelBase
    {
        private string _name;
        private float _score;
        private BodyPartCategory _category;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public float Score
        {
            get => _score;
            set { _score = value; OnPropertyChanged(); }
        }

        public BodyPartCategory Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        // スコアなしで手動追加する場合用
        public TagViewModel(string name)
        {
            _name = name;
            _score = 1.0f;
            _category = BodyPartCategory.Other;
        }

        // 推論結果から作成する場合用
        public TagViewModel(string name, float score, BodyPartCategory category)
        {
            _name = name;
            _score = score;
            _category = category;
        }
        public string ScoreText
        {
            get { return _score < 1.0f ? $"{_score:F2}" : ""; }
        }

        public System.Windows.Media.SolidColorBrush CategoryColor
        {
            get
            {
                switch (_category)
                {
                    case BodyPartCategory.Face: return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(137, 180, 250)); // blue
                    case BodyPartCategory.Body: return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 139, 168)); // red
                    case BodyPartCategory.Outfit: return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 166, 247)); // mauve
                    case BodyPartCategory.Pose: return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161)); // green
                    case BodyPartCategory.Background: return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 226, 175)); // yellow
                    case BodyPartCategory.Style: return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 226, 213)); // teal
                    default: return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 112, 134)); // gray
                }
            }
        }

    }

}
