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
                    UpdateBreadcrumbs();
                }
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
        }

        private string _breadcrumbs = "Cloud Root";
        public string Breadcrumbs
        {
            get => _breadcrumbs;
            set => SetProperty(ref _breadcrumbs, value);
        }

        private void UpdateBreadcrumbs()
        {
            if (SelectedNode == null)
            {
                Breadcrumbs = "Cloud Root";
                return;
            }

            var path = new List<string>();
            var current = SelectedNode;
            while (current != null)
            {
                path.Insert(0, current.Name);
                current = current.Parent;
            }
            Breadcrumbs = string.Join(" > ", path);
        }

        private void ApplyFilter()
        {
            // Simple filtering of top-level nodes for now
            // In a real tree, we'd need recursive visibility
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
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apsClient = new APSClient(accessToken, _logger);
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
                    var hubNode = new CloudNode(hub.Name, CloudItemType.Folder, hub.Id, null, null)
                    {
                        IsHub = true,
                        HubId = hub.Id,
                        Region = hub.Region,
                        ApsClient = _apsClient
                    };
                    // Add a dummy child to show the expander
                    hubNode.Children.Add(new CloudNode("Loading...", CloudItemType.Folder, null, null, hubNode));
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
        public DateTime? LastModifiedUtc { get; set; }
        public string Region { get; set; }
        public string RevitProjectGuid { get; set; }
        public string RevitModelGuid { get; set; }
        public bool IsHub { get; set; }
        public bool IsProject { get; set; }
        public APSClient ApsClient { get; set; }
        public CloudNode Parent { get; set; }

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

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public ObservableCollection<CloudNode> Children { get; } = new ObservableCollection<CloudNode>();

        public CloudNode(string name, CloudItemType type, string id, string projectId, CloudNode parent = null)
        {
            Name = name;
            Type = type;
            Id = id;
            ProjectId = projectId;
            Parent = parent;
        }

        /// <summary>
        /// Returns the selected item's human-readable ACC hierarchy for queue display.
        /// Technical identifiers stay separate and are never exposed in the normal UI.
        /// </summary>
        public string GetReadableCloudPath()
        {
            var pathParts = new List<string>();
            CloudNode current = this;

            while (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.Name) &&
                    !string.Equals(current.Name, "Loading...", StringComparison.OrdinalIgnoreCase))
                {
                    pathParts.Insert(0, current.Name);
                }
                current = current.Parent;
            }

            return pathParts.Count > 0 ? "ACC / " + string.Join(" / ", pathParts) : "ACC";
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
                        var pNode = new CloudNode(p.Name, CloudItemType.Folder, p.Id, p.Id, this) 
                        { 
                            IsProject = true, 
                            HubId = Id, // Current node's Id is the HubId
                            Region = Region,
                            RevitProjectGuid = p.RevitProjectGuid,
                            ApsClient = ApsClient 
                        };
                        pNode.Children.Add(new CloudNode("Loading...", CloudItemType.Folder, null, null, pNode));
                        Children.Add(pNode);
                    }
                }
                else if (IsProject)
                {
                    var topFolders = await ApsClient.GetTopFoldersAsync(HubId, Id);
                    foreach (var folder in topFolders)
                    {
                        var folderNode = new CloudNode(folder.Name, CloudItemType.Folder, folder.Id, ProjectId, this) 
                        { 
                            Region = Region,
                            RevitProjectGuid = RevitProjectGuid,
                            ApsClient = ApsClient 
                        };
                        folderNode.Children.Add(new CloudNode("Loading...", CloudItemType.Folder, null, null, folderNode));
                        Children.Add(folderNode);
                    }
                }
                else if (Type == CloudItemType.Folder)
                {
                    var contents = await ApsClient.GetFolderContentsAsync(ProjectId, Id);
                    foreach (var item in contents)
                    {
                        var node = new CloudNode(item.Name, item.Type, item.Id, ProjectId, this) 
                        { 
                            VersionId = item.VersionId,
                            LastModifiedUtc = item.LastModifiedUtc,
                            Region = Region,
                            // The authoritative GUIDs come from the file's tip Version API response.
                            // Do not overwrite the version-level Project GUID with the parent project node value.
                            RevitProjectGuid = item.RevitProjectGuid,
                            RevitModelGuid = item.RevitModelGuid,
                            ApsClient = ApsClient 
                        };
                        if (item.Type == CloudItemType.Folder)
                        {
                            node.Children.Add(new CloudNode("Loading...", CloudItemType.Folder, null, null, node));
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
