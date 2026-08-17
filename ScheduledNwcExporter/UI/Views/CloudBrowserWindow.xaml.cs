using System;
using System.Windows;
using ScheduledNwcExporter.UI.ViewModels;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class CloudBrowserWindow : Window
    {
        public CloudNode SelectedNode { get; private set; }

        public CloudBrowserWindow(CloudBrowserViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.NodeSelected += (node) =>
            {
                SelectedNode = node;
                DialogResult = true;
                Close();
            };

            viewModel.RequestClose += () => Close();
        }
    }
}
