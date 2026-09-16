using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        private bool _revitFilesOnly = true;
        public bool RevitFilesOnly
        {
            get => _revitFilesOnly;
            set { if (SetProperty(ref _revitFilesOnly, value)) ApplyFilter(); }
        }

        private string _searchStatus = "Browse or search loaded ACC folders.";
        public string SearchStatus
        {
            get => _searchStatus;
            private set => SetProperty(ref _searchStatus, value);
        }

        private string _breadcrumbs = "Cloud Root";
        public string Breadcrumbs
        {
            get => _breadcrumbs;
            set => SetProperty(ref _breadcrumbs, value);
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

            SelectCommand = new RelayCommand(OnSelect, () => SelectedNode != null && SelectedNode.Type == CloudItemType.File && SelectedNode.Name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase));
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
            LoadInitialData();
        }

        private void UpdateBreadcrumbs()
        {
            if (SelectedNode == null)
            {
                Breadcrumbs = "Cloud Root";
                return;
            }

            var path = new List<string>();
            CloudNode current = SelectedNode;
            while (current != null)
            {
                path.Insert(0, current.Name);
                current = current.Parent;
            }
            Breadcrumbs = string.Join(" > ", path);
        }

        private void ApplyFilter()
        {
            string search = (SearchText ?? string.Empty).Trim();
            int matches = 0;
            foreach (CloudNode root in Nodes)
                matches += root.ApplyFilter(search, RevitFilesOnly);

            SearchStatus = string.IsNullOrWhiteSpace(search)
                ? (RevitFilesOnly ? "Showing folders and Revit (.rvt) files in loaded branches." : "Showing all loaded ACC items.")
                : $"{matches} matching file(s) in loaded branches. Expand a project/folder to load deeper content.";
        }

        private async void LoadInitialData()
        {
            IsLoading = true;
            try
            {
                var hubs = await _apsClient.GetHubsAsync();
                if (hubs.Count == 0)
                    _logger.Warning("CloudBrowser", "No hubs found for the current user.");

                foreach (var hub in hubs)
                {
                    var hubNode = new CloudNode(hub.Name, CloudItemType.Folder, hub.Id, null, null)
                    {
                        IsHub = true,
                        HubId = hub.Id,
                        Region = hub.Region,
                        ApsClient = _apsClient,
                        TreeChanged = ApplyFilter
                    };
                    hubNode.Children.Add(CloudNode.CreateLoadingNode(hubNode, ApplyFilter));
                    Nodes.Add(hubNode);
                }
                ApplyFilter();
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
            if (SelectedNode != null && SelectedNode.Type == CloudItemType.File && SelectedNode.Name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                NodeSelected?.Invoke(SelectedNode);
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
        public Action TreeChanged { get; set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value)
                    LoadChildren();
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

        public static CloudNode CreateLoadingNode(CloudNode parent, Action treeChanged)
        {
            return new CloudNode("Loading...", CloudItemType.Folder, null, null, parent) { TreeChanged = treeChanged };
        }

        public int ApplyFilter(string search, bool revitFilesOnly)
        {
            if (string.Equals(Name, "Loading...", StringComparison.OrdinalIgnoreCase))
            {
                IsVisible = string.IsNullOrWhiteSpace(search);
                return 0;
            }

            int childMatches = 0;
            bool childVisible = false;
            foreach (CloudNode child in Children)
            {
                childMatches += child.ApplyFilter(search, revitFilesOnly);
                childVisible |= child.IsVisible;
            }

            bool isFolder = Type == CloudItemType.Folder;
            bool isRvt = Type == CloudItemType.File && Name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase);
            bool typeAllowed = isFolder || !revitFilesOnly || isRvt;
            bool textMatches = string.IsNullOrWhiteSpace(search) || Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            bool selfVisible = typeAllowed && textMatches;

            IsVisible = selfVisible || childVisible;
            if (!string.IsNullOrWhiteSpace(search) && childVisible)
                IsExpanded = true;

            return childMatches + ((Type == CloudItemType.File && selfVisible) ? 1 : 0);
        }

        public string GetReadableCloudPath()
        {
            var pathParts = new List<string>();
            CloudNode current = this;
            while (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.Name) && !string.Equals(current.Name, "Loading...", StringComparison.OrdinalIgnoreCase))
                    pathParts.Insert(0, current.Name);
                current = current.Parent;
            }
            return pathParts.Count > 0 ? "ACC / " + string.Join(" / ", pathParts) : "ACC";
        }

        private async void LoadChildren()
        {
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
                            HubId = Id,
                            Region = Region,
                            RevitProjectGuid = p.RevitProjectGuid,
                            ApsClient = ApsClient,
                            TreeChanged = TreeChanged
                        };
                        pNode.Children.Add(CreateLoadingNode(pNode, TreeChanged));
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
                            ApsClient = ApsClient,
                            TreeChanged = TreeChanged
                        };
                        folderNode.Children.Add(CreateLoadingNode(folderNode, TreeChanged));
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
                            RevitProjectGuid = item.RevitProjectGuid,
                            RevitModelGuid = item.RevitModelGuid,
                            ApsClient = ApsClient,
                            TreeChanged = TreeChanged
                        };
                        if (item.Type == CloudItemType.Folder)
                            node.Children.Add(CreateLoadingNode(node, TreeChanged));
                        Children.Add(node);
                    }
                }
                TreeChanged?.Invoke();
            }
            catch (Exception)
            {
                Children.Clear();
                Children.Add(new CloudNode("Error loading items", CloudItemType.Folder, null, null, this) { TreeChanged = TreeChanged });
                TreeChanged?.Invoke();
            }
        }
    }
}
