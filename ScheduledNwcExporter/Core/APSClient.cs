using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Forge;
using Newtonsoft.Json.Linq;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Core
{
    /// <summary>
    /// Extremely resilient client for interacting with Autodesk Platform Services (APS).
    /// Handles inconsistencies in Forge SDK response formats across different environments.
    /// </summary>
    public class APSClient
    {
        private readonly HubsApi _hubsApi;
        private readonly ProjectsApi _projectsApi;
        private readonly FoldersApi _foldersApi;
        private readonly string _accessToken;
        private readonly ILogger _logger;

        public APSClient(string accessToken, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException("An APS access token is required.", nameof(accessToken));
            }

            _accessToken = accessToken;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Autodesk.Forge.Client.Configuration.Default.AccessToken = _accessToken;
            _hubsApi = new HubsApi();
            _projectsApi = new ProjectsApi();
            _foldersApi = new FoldersApi();
        }

        private JObject GetRawJObject(dynamic response)
        {
            if (response == null) return new JObject();
            
            // Handle cases where ToJson() returns JObject or string
            var raw = response.ToJson();
            if (raw is JObject jObject) return jObject;
            
            string jsonString = raw?.ToString() ?? "{}";
            return JObject.Parse(jsonString);
        }

        private IEnumerable<JToken> GetDataItems(JObject json)
        {
            var data = json["data"];
            if (data == null) yield break;

            if (data is JArray array)
            {
                foreach (var item in array) yield return item;
            }
            else if (data is JObject obj)
            {
                // Handle cases where 'data' is an object with numeric keys "0", "1", etc.
                foreach (var property in obj.Properties())
                {
                    yield return property.Value;
                }
            }
        }

        public async Task<List<Hub>> GetHubsAsync()
        {
            var hubsList = new List<Hub>();
            dynamic response = await _hubsApi.GetHubsAsync();
            JObject json = GetRawJObject(response);
            
            foreach (var item in GetDataItems(json))
            {
                hubsList.Add(new Hub 
                { 
                    Id = item["id"]?.ToString(), 
                    Name = item.SelectToken("attributes.name")?.ToString() ?? "Unknown Hub",
                    Region = item.SelectToken("attributes.region")?.ToString() ?? "US"
                });
            }
            return hubsList;
        }

        public async Task<List<Project>> GetProjectsAsync(string hubId)
        {
            var projectsList = new List<Project>();
            dynamic response = await _projectsApi.GetHubProjectsAsync(hubId);
            JObject json = GetRawJObject(response);

            foreach (var item in GetDataItems(json))
            {
                projectsList.Add(new Project 
                { 
                    Id = item["id"]?.ToString(), 
                    Name = item.SelectToken("attributes.name")?.ToString() ?? "Unknown Project",
                    RevitProjectGuid = item.SelectToken("attributes.extension.data.projectGuid")?.ToString()
                });
            }
            return projectsList;
        }

        public async Task<List<CloudItem>> GetTopFoldersAsync(string hubId, string projectId)
        {
            var itemsList = new List<CloudItem>();
            dynamic response = await _projectsApi.GetProjectTopFoldersAsync(hubId, projectId);
            JObject json = GetRawJObject(response);

            foreach (var item in GetDataItems(json))
            {
                itemsList.Add(new CloudItem 
                { 
                    Id = item["id"]?.ToString(), 
                    Name = item.SelectToken("attributes.displayName")?.ToString() ?? item.SelectToken("attributes.name")?.ToString() ?? "Unknown Folder", 
                    Type = CloudItemType.Folder 
                });
            }
            return itemsList;
        }

        public async Task<List<CloudItem>> GetFolderContentsAsync(string projectId, string folderId)
        {
            var itemsList = new List<CloudItem>();
            dynamic response = await _foldersApi.GetFolderContentsAsync(projectId, folderId);
            JObject json = GetRawJObject(response);

            foreach (var item in GetDataItems(json))
            {
                string type = item["type"]?.ToString();
                string displayName = item.SelectToken("attributes.displayName")?.ToString() ?? item.SelectToken("attributes.name")?.ToString();
                
                if (type == "folders")
                {
                    itemsList.Add(new CloudItem { Id = item["id"]?.ToString(), Name = displayName, Type = CloudItemType.Folder });
                }
                else if (type == "items" && !string.IsNullOrEmpty(displayName) && displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                {
                    string versionId = item.SelectToken("relationships.tip.data.id")?.ToString();
                    var cloudItem = new CloudItem 
                    { 
                        Id = item["id"]?.ToString(), 
                        Name = displayName, 
                        Type = CloudItemType.File,
                        VersionId = versionId
                    };

                    // EXPERT ADVICE: Fetch version details to get reliable GUIDs
                    if (!string.IsNullOrEmpty(versionId))
                    {
                        try
                        {
                            // FIX: Must set the token for the new API instance
                            var versionsApi = new Autodesk.Forge.VersionsApi();
                            versionsApi.Configuration.AccessToken = _accessToken;
                            
                            dynamic vResponse = await versionsApi.GetVersionAsync(projectId, versionId);
                            JObject vJson = GetRawJObject(vResponse);
                            var vData = vJson["data"];
                            
                            string mGuid = vData?.SelectToken("attributes.extension.data.modelGuid")?.ToString();
                            string pGuid = vData?.SelectToken("attributes.extension.data.projectGuid")?.ToString();

                            // Ensure GUIDs are not null before assigning
                            if (!string.IsNullOrEmpty(mGuid)) cloudItem.RevitModelGuid = mGuid;
                            if (!string.IsNullOrEmpty(pGuid)) cloudItem.RevitProjectGuid = pGuid;
                            
                            _logger.Info("CloudBrowser", $"Extracted GUIDs for {displayName}: Model={cloudItem.RevitModelGuid}, Project={cloudItem.RevitProjectGuid}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("CloudBrowser", $"Failed to fetch version GUIDs for {displayName}: {ex.Message}");
                        }
                    }

                    itemsList.Add(cloudItem);
                }
            }
            return itemsList;
        }
    }

    public class Hub
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Region { get; set; }
    }

    public class Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string RevitProjectGuid { get; set; }
    }

    public enum CloudItemType { Folder, File }

    public class CloudItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public CloudItemType Type { get; set; }
        public string VersionId { get; set; }
        public string RevitModelGuid { get; set; }
        public string RevitProjectGuid { get; set; }
    }
}
