using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ScheduledNwcExporter.Core;
using ScheduledNwcExporter.Logging;

using ScheduledNwcExporter.UI;

namespace ScheduledNwcExporter.UI.ViewModels
{
    public class CloudBrowserViewModel : BindableBase
    {
        private readonly APSClient _apsClient;
        private readonly ILogger _logger;

        private ObservableCollection<CloudNode> _nodes;
        public ObservableCollection<CloudNode> Nodes
        {
            get => _nodes;
            set => SetProperty(ref _nodes, value);
        }

        private CloudNode _selectedNode;
        public CloudNode SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    (SelectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public ICommand SelectCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<CloudNode> NodeSelected;
        public event Action RequestClose;

        public CloudBrowserViewModel(string accessToken, ILogger logger)
        {
            _apsClient = new APSClient(accessToken);
            _logger = logger;
            Nodes = new ObservableCollection<CloudNode>();
            
            SelectCommand = new RelayCommand(OnSelect, () => SelectedNode != null && SelectedNode.Type == CloudItemType.File);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());

            LoadInitialData();
        }

        private async void LoadInitialData()
        {
            IsLoading = true;
            try
            {
                var hubs = await _apsClient.GetHubsAsync();
                if (hubs.Count == 0)
                {
                    _logger.Warning("CloudBrowser", "No hubs found for the current user.");
                }

                foreach (var hub in hubs)
                {
                    var hubNode = new CloudNode(hub.Name, CloudItemType.Folder, hub.Id, null)
                    {
                        IsHub = true,
                        HubId = hub.Id,
                        Region = hub.Region,
                        ApsClient = _apsClient
                    };
                    // Add a dummy child to show the expander
                    hubNode.Children.Add(new CloudNode("Loading...", CloudItemType.Folder, null, null));
                    Nodes.Add(hubNode);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("CloudBrowser", $"Critical error loading hubs: {ex.Message}", string.Empty, "CloudAuth", ex);
                System.Windows.MessageBox.Show($"Failed to connect to Autodesk Cloud:\n{ex.Message}", "Hatco Cloud Explorer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OnSelect()
        {
            if (SelectedNode != null && SelectedNode.Type == CloudItemType.File)
            {
                NodeSelected?.Invoke(SelectedNode);
            }
        }
    }

    public class CloudNode : BindableBase
    {
        public string Name { get; set; }
        public CloudItemType Type { get; set; }
        public string Id { get; set; }
        public string HubId { get; set; }
        public string ProjectId { get; set; }
        public string VersionId { get; set; }
        public string Region { get; set; }
        public string RevitProjectGuid { get; set; }
        public string RevitModelGuid { get; set; }
        public bool IsHub { get; set; }
        public bool IsProject { get; set; }
        public APSClient ApsClient { get; set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value)
                {
                    LoadChildren();
                }
            }
        }

        public ObservableCollection<CloudNode> Children { get; } = new ObservableCollection<CloudNode>();

        public CloudNode(string name, CloudItemType type, string id, string projectId)
        {
            Name = name;
            Type = type;
            Id = id;
            ProjectId = projectId;
        }

        private async void LoadChildren()
        {
            // If already loaded (beyond the dummy), skip
            if (Children.Count > 0 && Children[0].Name != "Loading...") return;

            try
            {
                Children.Clear();
                if (IsHub)
                {
                    var projects = await ApsClient.GetProjectsAsync(Id);
                    foreach (var p in projects)
                    {
                        var pNode = new CloudNode(p.Name, CloudItemType.Folder, p.Id, p.Id) 
                        { 
                            IsProject = true, 
                            HubId = Id, // Current node's Id is the HubId
                            Region = Region,
                            RevitProjectGuid = p.RevitProjectGuid,
                            ApsClient = ApsClient 
                        };
                        pNode.Children.Add(new CloudNode("Loading...", CloudItemType.Folder, null, null));
                        Children.Add(pNode);
                    }
                }
                else if (IsProject)
                {
                    var topFolders = await ApsClient.GetTopFoldersAsync(HubId, Id);
                    foreach (var folder in topFolders)
                    {
                        var folderNode = new CloudNode(folder.Name, CloudItemType.Folder, folder.Id, ProjectId) 
                        { 
                            Region = Region,
                            RevitProjectGuid = RevitProjectGuid,
                            ApsClient = ApsClient 
                        };
                        folderNode.Children.Add(new CloudNode("Loading...", CloudItemType.Folder, null, null));
                        Children.Add(folderNode);
                    }
                }
                else if (Type == CloudItemType.Folder)
                {
                    var contents = await ApsClient.GetFolderContentsAsync(ProjectId, Id);
                    foreach (var item in contents)
                    {
                        var node = new CloudNode(item.Name, item.Type, item.Id, ProjectId) 
                        { 
                            VersionId = item.VersionId,
                            Region = Region,
                            RevitProjectGuid = RevitProjectGuid,
                            RevitModelGuid = item.RevitModelGuid,
                            ApsClient = ApsClient 
                        };
                        if (item.Type == CloudItemType.Folder)
                        {
                            node.Children.Add(new CloudNode("Loading...", CloudItemType.Folder, null, null));
                        }
                        Children.Add(node);
                    }
                }
            }
            catch (Exception)
            {
                Children.Clear();
                Children.Add(new CloudNode("Error loading items", CloudItemType.Folder, null, null));
            }
        }
    }
}
