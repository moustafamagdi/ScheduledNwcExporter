using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ScheduledNwcExporter.UI.ViewModels;

namespace ScheduledNwcExporter.UI
{
    /// <summary>
    /// Adds a confirmation gate to the existing Remove Model command without changing
    /// the command/view-model contract. The preview event cancels the click before the
    /// button executes its command when the user chooses No.
    /// </summary>
    internal static class RemoveConfirmationBehavior
    {
        public static void Attach(Window window)
        {
            window.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }

        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is Window window) || !(window.DataContext is MainViewModel viewModel))
                return;

            DependencyObject? current = e.OriginalSource as DependencyObject;
            ButtonBase? button = null;
            while (current != null)
            {
                if (current is ButtonBase candidate)
                {
                    button = candidate;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            if (button == null || !ReferenceEquals(button.Command, viewModel.RemoveModelCommand) || viewModel.SelectedJob == null)
                return;

            string modelName = viewModel.SelectedJob.DisplaySourcePath;
            MessageBoxResult answer = MessageBox.Show(
                $"Remove this model from the export queue?\n\n{modelName}\n\nThis only removes the job from Hatco NWC Exporter; it does not delete the RVT or NWC file.",
                "Confirm Remove Model",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                e.Handled = true;
            }
        }
    }
}
