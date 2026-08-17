using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Forge;
using Autodesk.Forge.Model;
using Newtonsoft.Json.Linq;

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
            dynamic response = await _hubsApi.GetHubsAsync();
            JObject json = JObject.FromObject(response);
            
            var data = json["data"];
            if (data != null)
            {
                foreach (var hub in data)
                {
                    hubsList.Add(new Hub 
                    { 
                        Id = hub["id"]?.ToString(), 
                        Name = hub["attributes"]?["name"]?.ToString() ?? "Unknown Hub" 
                    });
                }
            }
            return hubsList;
        }

        public async Task<List<Project>> GetProjectsAsync(string hubId)
        {
            var projectsList = new List<Project>();
            dynamic response = await _projectsApi.GetHubProjectsAsync(hubId);
            JObject json = JObject.FromObject(response);

            var data = json["data"];
            if (data != null)
            {
                foreach (var project in data)
                {
                    projectsList.Add(new Project 
                    { 
                        Id = project["id"]?.ToString(), 
                        Name = project["attributes"]?["name"]?.ToString() ?? "Unknown Project" 
                    });
                }
            }
            return projectsList;
        }

        public async Task<List<CloudItem>> GetTopFoldersAsync(string hubId, string projectId)
        {
            var items = new List<CloudItem>();
            dynamic response = await _projectsApi.GetProjectTopFoldersAsync(hubId, projectId);
            JObject json = JObject.FromObject(response);

            var data = json["data"];
            if (data != null)
            {
                foreach (var folder in data)
                {
                    items.Add(new CloudItem 
                    { 
                        Id = folder["id"]?.ToString(), 
                        Name = folder["attributes"]?["displayName"]?.ToString() ?? folder["attributes"]?["name"]?.ToString() ?? "Unknown Folder", 
                        Type = CloudItemType.Folder 
                    });
                }
            }
            return items;
        }

        public async Task<List<CloudItem>> GetFolderContentsAsync(string projectId, string folderId)
        {
            var items = new List<CloudItem>();
            dynamic response = await _foldersApi.GetFolderContentsAsync(projectId, folderId);
            JObject json = JObject.FromObject(response);

            var data = json["data"];
            if (data != null)
            {
                foreach (var item in data)
                {
                    string type = item["type"]?.ToString();
                    string displayName = item["attributes"]?["displayName"]?.ToString() ?? item["attributes"]?["name"]?.ToString();
                    
                    if (type == "folders")
                    {
                        items.Add(new CloudItem { Id = item["id"]?.ToString(), Name = displayName, Type = CloudItemType.Folder });
                    }
                    else if (type == "items" && !string.IsNullOrEmpty(displayName) && displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                    {
                        items.Add(new CloudItem 
                        { 
                            Id = item["id"]?.ToString(), 
                            Name = displayName, 
                            Type = CloudItemType.File,
                            VersionId = item["relationships"]?["tip"]?["data"]?["id"]?.ToString()
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
