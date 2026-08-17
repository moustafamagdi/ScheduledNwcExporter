using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Forge;
using Autodesk.Forge.Model;

namespace ScheduledNwcExporter.Core
{
    public class APSClient
    {
        private readonly HubsApi _hubsApi;
        private readonly ProjectsApi _projectsApi;
        private readonly FoldersApi _foldersApi;
        private readonly ItemsApi _itemsApi;

        public APSClient(string accessToken)
        {
            Autodesk.Forge.Client.Configuration.Default.AccessToken = accessToken;
            _hubsApi = new HubsApi();
            _projectsApi = new ProjectsApi();
            _foldersApi = new FoldersApi();
            _itemsApi = new ItemsApi();
        }

        public async Task<List<Hub>> GetHubsAsync()
        {
            var hubsList = new List<Hub>();
            // Use concrete Hubs type instead of dynamic
            Hubs response = await _hubsApi.GetHubsAsync();
            if (response?.Data != null)
            {
                foreach (var hubData in response.Data)
                {
                    hubsList.Add(new Hub 
                    { 
                        Id = hubData.Id, 
                        Name = hubData.Attributes?.Name ?? "Unknown Hub" 
                    });
                }
            }
            return hubsList;
        }

        public async Task<List<Project>> GetProjectsAsync(string hubId)
        {
            var projectsList = new List<Project>();
            // Use concrete Projects type
            Projects response = await _projectsApi.GetHubProjectsAsync(hubId);
            if (response?.Data != null)
            {
                foreach (var projectData in response.Data)
                {
                    projectsList.Add(new Project 
                    { 
                        Id = projectData.Id, 
                        Name = projectData.Attributes?.Name ?? "Unknown Project" 
                    });
                }
            }
            return projectsList;
        }

        public async Task<List<CloudItem>> GetTopFoldersAsync(string hubId, string projectId)
        {
            var items = new List<CloudItem>();
            // Top folders can be retrieved using GetProjectTopFoldersAsync
            TopFolders response = await _projectsApi.GetProjectTopFoldersAsync(hubId, projectId);
            if (response?.Data != null)
            {
                foreach (var folderData in response.Data)
                {
                    items.Add(new CloudItem 
                    { 
                        Id = folderData.Id, 
                        Name = folderData.Attributes?.DisplayName ?? "Unknown Folder", 
                        Type = CloudItemType.Folder 
                    });
                }
            }
            return items;
        }

        public async Task<List<CloudItem>> GetFolderContentsAsync(string projectId, string folderId)
        {
            var items = new List<CloudItem>();
            // Folder contents use JsonApiCollection
            JsonApiCollection response = await _foldersApi.GetFolderContentsAsync(projectId, folderId);
            if (response?.Data != null)
            {
                foreach (var item in response.Data)
                {
                    // In Forge SDK, item is usually a dynamic object within the collection Data list
                    // but we can access properties safely.
                    string type = item.Type;
                    string displayName = item.Attributes?.DisplayName;
                    
                    if (type == "folders")
                    {
                        items.Add(new CloudItem { Id = item.Id, Name = displayName, Type = CloudItemType.Folder });
                    }
                    else if (type == "items" && !string.IsNullOrEmpty(displayName) && displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                    {
                        items.Add(new CloudItem 
                        { 
                            Id = item.Id, 
                            Name = displayName, 
                            Type = CloudItemType.File,
                            VersionId = item.Relationships?.Tip?.Data?.Id
                        });
                    }
                }
            }
            return items;
        }
    }

    public class Hub
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public enum CloudItemType { Folder, File }

    public class CloudItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public CloudItemType Type { get; set; }
        public string VersionId { get; set; }
    }
}
