using System;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace TagFilter
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // UIスレッドの未処理例外
            DispatcherUnhandledException += (s, ex) =>
            {
                ShowError("UIスレッド例外", ex.Exception);
                ex.Handled = true;
            };

            // バックグラウンドスレッドの未処理例外
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                ShowError("致命的エラー", ex.ExceptionObject as Exception);
            };

            // Task内の未処理例外
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                ShowError("非同期タスク例外", ex.Exception);
                ex.SetObserved();
            };
        }

        private void ShowError(string title, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"【{title}】");
            sb.AppendLine();

            var current = ex;
            int depth = 0;
            while (current != null && depth < 5)
            {
                if (depth > 0) sb.AppendLine($"\n--- 内部例外 {depth} ---");
                sb.AppendLine($"種類: {current.GetType().FullName}");
                sb.AppendLine($"メッセージ: {current.Message}");
                if (!string.IsNullOrEmpty(current.StackTrace))
                {
                    sb.AppendLine("スタックトレース:");
                    sb.AppendLine(current.StackTrace);
                }
                current = current.InnerException;
                depth++;
            }

            var message = sb.ToString();

            // クリップボードにもコピー（報告しやすくする）
            try { Clipboard.SetText(message); } catch { }

            MessageBox.Show(
                message + "\n\n※このメッセージはクリップボードにコピーされました",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}