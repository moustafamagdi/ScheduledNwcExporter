using System.Windows;
using ScheduledNwcExporter.UI.ViewModels;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class LogViewerWindow : Window
    {
        public LogViewerWindow(string logFilePath)
        {
            InitializeComponent();
            DataContext = new LogViewerViewModel(logFilePath);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
