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
            Autodesk.Forge.Configuration.Default.AccessToken = accessToken;
            _hubsApi = new HubsApi();
            _projectsApi = new ProjectsApi();
            _foldersApi = new FoldersApi();
            _itemsApi = new ItemsApi();
        }

        public async Task<List<Hub>> GetHubsAsync()
        {
            var hubs = new List<Hub>();
            dynamic response = await _hubsApi.GetHubsAsync();
            foreach (var hub in response.data)
            {
                hubs.Add(new Hub { Id = hub.id, Name = hub.attributes.name });
            }
            return hubs;
        }

        public async Task<List<Project>> GetProjectsAsync(string hubId)
        {
            var projects = new List<Project>();
            dynamic response = await _projectsApi.GetHubProjectsAsync(hubId);
            foreach (var project in response.data)
            {
                projects.Add(new Project { Id = project.id, Name = project.attributes.name });
            }
            return projects;
        }

        public async Task<List<CloudItem>> GetTopFoldersAsync(string hubId, string projectId)
        {
            var items = new List<CloudItem>();
            dynamic response = await _projectsApi.GetProjectTopFoldersAsync(hubId, projectId);
            foreach (var folder in response.data)
            {
                items.Add(new CloudItem 
                { 
                    Id = folder.id, 
                    Name = folder.attributes.displayName, 
                    Type = CloudItemType.Folder 
                });
            }
            return items;
        }

        public async Task<List<CloudItem>> GetFolderContentsAsync(string projectId, string folderId)
        {
            var items = new List<CloudItem>();
            dynamic response = await _foldersApi.GetFolderContentsAsync(projectId, folderId);
            foreach (var item in response.data)
            {
                string type = item.type;
                string displayName = item.attributes.displayName;
                
                if (type == "folders")
                {
                    items.Add(new CloudItem { Id = item.id, Name = displayName, Type = CloudItemType.Folder });
                }
                else if (type == "items" && displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new CloudItem 
                    { 
                        Id = item.id, 
                        Name = displayName, 
                        Type = CloudItemType.File,
                        VersionId = item.relationships.tip.data.id
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
