// ViewModelBase.cs（新規作成）
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TagFilter
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}