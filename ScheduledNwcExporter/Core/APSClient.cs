using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Forge;
using Newtonsoft.Json;

namespace ScheduledNwcExporter.Core
{
    /// <summary>
    /// Robust client for interacting with Autodesk Platform Services (APS).
    /// Uses custom POCOs and manual deserialization to avoid SDK model casting issues in .NET 4.8.
    /// </summary>
    public class APSClient
    {
        private readonly HubsApi _hubsApi;
        private readonly ProjectsApi _projectsApi;
        private readonly FoldersApi _foldersApi;

        public APSClient(string accessToken)
        {
            Autodesk.Forge.Client.Configuration.Default.AccessToken = accessToken;
            _hubsApi = new HubsApi();
            _projectsApi = new ProjectsApi();
            _foldersApi = new FoldersApi();
        }

        public async Task<List<Hub>> GetHubsAsync()
        {
            var hubsList = new List<Hub>();
            dynamic response = await _hubsApi.GetHubsAsync();
            string json = JsonConvert.SerializeObject(response);
            
            var hubsResponse = JsonConvert.DeserializeObject<ForgeResponse<HubAttributes>>(json);
            if (hubsResponse?.data != null)
            {
                foreach (var item in hubsResponse.data)
                {
                    hubsList.Add(new Hub 
                    { 
                        Id = item.id, 
                        Name = item.attributes?.name ?? "Unknown Hub" 
                    });
                }
            }
            return hubsList;
        }

        public async Task<List<Project>> GetProjectsAsync(string hubId)
        {
            var projectsList = new List<Project>();
            dynamic response = await _projectsApi.GetHubProjectsAsync(hubId);
            string json = JsonConvert.SerializeObject(response);

            var projectsResponse = JsonConvert.DeserializeObject<ForgeResponse<ProjectAttributes>>(json);
            if (projectsResponse?.data != null)
            {
                foreach (var item in projectsResponse.data)
                {
                    projectsList.Add(new Project 
                    { 
                        Id = item.id, 
                        Name = item.attributes?.name ?? "Unknown Project" 
                    });
                }
            }
            return projectsList;
        }

        public async Task<List<CloudItem>> GetTopFoldersAsync(string hubId, string projectId)
        {
            var itemsList = new List<CloudItem>();
            dynamic response = await _projectsApi.GetProjectTopFoldersAsync(hubId, projectId);
            string json = JsonConvert.SerializeObject(response);

            var foldersResponse = JsonConvert.DeserializeObject<ForgeResponse<FolderAttributes>>(json);
            if (foldersResponse?.data != null)
            {
                foreach (var item in foldersResponse.data)
                {
                    itemsList.Add(new CloudItem 
                    { 
                        Id = item.id, 
                        Name = item.attributes?.displayName ?? item.attributes?.name ?? "Unknown Folder", 
                        Type = CloudItemType.Folder 
                    });
                }
            }
            return itemsList;
        }

        public async Task<List<CloudItem>> GetFolderContentsAsync(string projectId, string folderId)
        {
            var itemsList = new List<CloudItem>();
            dynamic response = await _foldersApi.GetFolderContentsAsync(projectId, folderId);
            string json = JsonConvert.SerializeObject(response);

            var contentsResponse = JsonConvert.DeserializeObject<ForgeResponse<FolderAttributes>>(json);
            if (contentsResponse?.data != null)
            {
                foreach (var item in contentsResponse.data)
                {
                    string type = item.type;
                    string displayName = item.attributes?.displayName ?? item.attributes?.name;
                    
                    if (type == "folders")
                    {
                        itemsList.Add(new CloudItem { Id = item.id, Name = displayName, Type = CloudItemType.Folder });
                    }
                    else if (type == "items" && !string.IsNullOrEmpty(displayName) && displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                    {
                        itemsList.Add(new CloudItem 
                        { 
                            Id = item.id, 
                            Name = displayName, 
                            Type = CloudItemType.File,
                            VersionId = item.relationships?.tip?.data?.id
                        });
                    }
                }
            }
            return itemsList;
        }
    }

    #region Custom POCOs for JSON:API Deserialization

    public class ForgeResponse<T>
    {
        public List<ForgeData<T>> data { get; set; }
    }

    public class ForgeData<T>
    {
        public string id { get; set; }
        public string type { get; set; }
        public T attributes { get; set; }
        public ForgeRelationships relationships { get; set; }
    }

    public class HubAttributes
    {
        public string name { get; set; }
    }

    public class ProjectAttributes
    {
        public string name { get; set; }
    }

    public class FolderAttributes
    {
        public string name { get; set; }
        public string displayName { get; set; }
    }

    public class ForgeRelationships
    {
        public ForgeRelationshipTip tip { get; set; }
    }

    public class ForgeRelationshipTip
    {
        public ForgeRelationshipData data { get; set; }
    }

    public class ForgeRelationshipData
    {
        public string id { get; set; }
        public string type { get; set; }
    }

    #endregion

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
