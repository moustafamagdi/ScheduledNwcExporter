using System.Windows;
using System.Windows.Input;

namespace ScheduledNwcExporter.UI.Views
{
    /// <summary>
    /// Non-destructive completion dialog. Escape only closes this summary window;
    /// it never affects the already-finished export session.
    /// </summary>
    public partial class SessionCompletedWindow : Window
    {
        public SessionCompletedWindow(string message, bool hasWarning)
        {
            InitializeComponent();
            HeaderText.Text = hasWarning ? "Export completed with attention required" : "Export completed";
            SummaryText.Text = message ?? string.Empty;
            PreviewKeyDown += SessionCompletedWindow_PreviewKeyDown;
        }

        private void SessionCompletedWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            e.Handled = true;
            Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            PreviewKeyDown -= SessionCompletedWindow_PreviewKeyDown;
            base.OnClosed(e);
        }
    }
}
