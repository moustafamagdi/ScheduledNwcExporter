using System;
using System.Reflection;
using System.Text;
using System.Windows;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Revit;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class DiagnosticsWindow : Window
    {
        private readonly FileLogger _logger;

        public DiagnosticsWindow(FileLogger logger)
        {
            InitializeComponent();
            _logger = logger;
            LoadDiagnostics();
        }

        private void LoadDiagnostics()
        {
            var exporterService = new NwcExporterService(_logger);
            bool exporterAvailable = exporterService.IsExporterAvailable();

            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            string versionStr = version != null ? version.ToString() : "1.0.0";

            var sb = new StringBuilder();
            sb.AppendLine("=== Scheduled Nwc Export Manager Diagnostics ===");
            sb.AppendLine($"Add-in Version: {versionStr}");
            sb.AppendLine("Revit Target: Revit 2024 (.NET Framework 4.8)");
            sb.AppendLine($".NET Version: {Environment.Version}");
            sb.AppendLine($"OS Version: {Environment.OSVersion}");
            sb.AppendLine($"Machine Name: {Environment.MachineName}");
            sb.AppendLine($"Current User: {Environment.UserName}");
            sb.AppendLine($"Navisworks Exporter Available: {exporterAvailable}");
            sb.AppendLine($"Log File Path: {_logger.LogFilePath}");
            sb.AppendLine($"AppData Configuration Path: {System.IO.Path.GetDirectoryName(_logger.LogFilePath)}");

            DiagnosticTextBox.Text = sb.ToString();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(DiagnosticTextBox.Text);
            MessageBox.Show("Diagnostics copied to clipboard.", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
