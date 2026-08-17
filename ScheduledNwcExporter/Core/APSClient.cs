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
            Configuration.Default.AccessToken = accessToken;
            _hubsApi = new HubsApi();
            _projectsApi = new ProjectsApi();
            _foldersApi = new FoldersApi();
            _itemsApi = new ItemsApi();
        }

        public async Task<List<Hub>> GetHubsAsync()
        {
            var hubs = new List<Hub>();
            dynamic response = await _hubsApi.GetHubsAsync();
            foreach (KeyValuePair<string, dynamic> hub in new DynamicDictionaryItems(response.data))
            {
                hubs.Add(new Hub { Id = hub.Value.id, Name = hub.Value.attributes.name });
            }
            return hubs;
        }

        public async Task<List<Project>> GetProjectsAsync(string hubId)
        {
            var projects = new List<Project>();
            dynamic response = await _hubsApi.GetHubProjectsAsync(hubId);
            foreach (KeyValuePair<string, dynamic> project in new DynamicDictionaryItems(response.data))
            {
                projects.Add(new Project { Id = project.Value.id, Name = project.Value.attributes.name });
            }
            return projects;
        }

        public async Task<List<CloudItem>> GetTopFoldersAsync(string hubId, string projectId)
        {
            var items = new List<CloudItem>();
            dynamic response = await _projectsApi.GetProjectTopFoldersAsync(hubId, projectId);
            foreach (KeyValuePair<string, dynamic> folder in new DynamicDictionaryItems(response.data))
            {
                items.Add(new CloudItem 
                { 
                    Id = folder.Value.id, 
                    Name = folder.Value.attributes.displayName, 
                    Type = CloudItemType.Folder 
                });
            }
            return items;
        }

        public async Task<List<CloudItem>> GetFolderContentsAsync(string projectId, string folderId)
        {
            var items = new List<CloudItem>();
            dynamic response = await _foldersApi.GetFolderContentsAsync(projectId, folderId);
            foreach (KeyValuePair<string, dynamic> item in new DynamicDictionaryItems(response.data))
            {
                string type = item.Value.type;
                string displayName = item.Value.attributes.displayName;
                
                if (type == "folders")
                {
                    items.Add(new CloudItem { Id = item.Value.id, Name = displayName, Type = CloudItemType.Folder });
                }
                else if (type == "items" && displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                {
                    // For Revit models, we need the version/lineage to get the ModelGUID
                    items.Add(new CloudItem 
                    { 
                        Id = item.Value.id, 
                        Name = displayName, 
                        Type = CloudItemType.File,
                        // The version ID is often needed for specific API calls
                        VersionId = item.Value.relationships.tip.data.id
                    });
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
