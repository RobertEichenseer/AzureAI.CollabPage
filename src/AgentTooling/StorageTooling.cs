using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FTA.AI.Agents.CollabPage.AgentTooling;

public class StorageTooling {
    private Configuration _configuration;

    public StorageTooling(Configuration configuration) {
        _configuration = configuration;
    }

    public async Task<List<string>> GetInstanceAgentOutput(string instanceId, string agentName, bool agentMatch) 
    {
        try {
            BlobServiceClient blobServiceClient = new BlobServiceClient(_configuration.StorageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_configuration.StorageCollabContainer);

            //Get list of all blobs which are in the instanceId folder
            List<string> outputFiles = new List<string>();
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: instanceId))
            {
                CollabPageFileMetaData collabPageFileMetaData = await GetMetadata(blobItem.Name.Split("/").Last(), instanceId);
                if (!collabPageFileMetaData.IsAgentCreated)
                    continue;

                if (agentMatch)
                {
                    if (collabPageFileMetaData.AgentName == agentName)
                        outputFiles.Add(blobItem.Name.Split("/").Last());
                }
                else
                {
                    if (collabPageFileMetaData.AgentName != agentName)
                        outputFiles.Add(blobItem.Name.Split("/").Last());
                }
            }

            return new List<string>(outputFiles);

        }
        catch {
            return new List<string>();
        }

    }

    public async Task<List<string>> GetProcessingJournal(string instanceId) 
    {
        try {
            BlobServiceClient blobServiceClient = new BlobServiceClient(_configuration.StorageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_configuration.StorageCollabContainer);

            List<(string Name, DateTimeOffset? CreationTime)> activities = new List<(string, DateTimeOffset?)>();

            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: instanceId))
            {
                BlobClient blobClient = containerClient.GetBlobClient(blobItem.Name);
                BlobProperties properties = await blobClient.GetPropertiesAsync();
                activities.Add((blobItem.Name, properties.CreatedOn));
            }
            List<string> sortedActivities = activities.OrderBy(b => b.CreationTime).Select(b => b.Name).ToList<string>();

            if (sortedActivities.Count == 0)
            {
                return new List<string>();
            }

            List<string> journal = new List<string>();
            foreach (string activity in sortedActivities)
            {
                CollabPageFileMetaData collabPageFileMetaData = await GetMetadata(activity.Split("/").Last(), instanceId);
                journal.Add($"File: {activity.Split("/").Last()}");
                journal.Add($"Agent Created: {collabPageFileMetaData.IsAgentCreated}");
                if (collabPageFileMetaData.IsAgentCreated) {
                    journal.Add($"Agent: {collabPageFileMetaData.AgentName}");
                    journal.Add($"Input Files: {JsonSerializer.Serialize(collabPageFileMetaData.InputFiles)}");
                    journal.Add($"Additional Output Files: {JsonSerializer.Serialize(collabPageFileMetaData.AdditionalOutputFiles)}");
                }
                else {
                    journal.Add($"Expected Processing Output: {collabPageFileMetaData.ExpectedProcessingOutput}");
                    journal.Add($"Targeted Agents: {JsonSerializer.Serialize(collabPageFileMetaData.TargetedAgents)}");
                }
                journal.Add($"File Content: {await GetInputContent(activity.Split("/").Last(), instanceId)} \n\n ");
            }
            return journal;
        }
        catch {
            return new List<string>();
        }

    }


    public async Task<string> GetInputContent(string inputFileName, string instanceId)
    {
        try {
            BlobServiceClient blobServiceClient = new BlobServiceClient(_configuration.StorageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_configuration.StorageCollabContainer);
            BlobClient blobClient = containerClient.GetBlobClient(String.Concat(instanceId, "/", inputFileName));
            
            var response = await blobClient.DownloadAsync();
            using (var reader = new StreamReader(response.Value.Content))
            {
                return await reader.ReadToEndAsync();
            }
        }
        catch {
            return "";
        }
    }

    public async Task<bool> StoreAgentResponse(
        string outputFileName, 
        string instanceId, 
        string content, 
        CollabPageFileMetaData? collabPageFileMetaData = null)
    {
        try {
            BlobServiceClient blobServiceClient = new BlobServiceClient(_configuration.StorageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_configuration.StorageCollabContainer);
            BlobClient blobClient = containerClient.GetBlobClient($"{instanceId}/{outputFileName}");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                await blobClient.UploadAsync(stream, true);
            }
            if (collabPageFileMetaData != null)
            {
                await StoreMetadata(outputFileName, instanceId, collabPageFileMetaData);
            }
            return true; 
        } catch {
            return false;
        }
    }

    public async Task<bool> StoreMetadata(string fileName, string instanceId, CollabPageFileMetaData collabPageFileMetaData)
    {
        try {
            BlobServiceClient blobServiceClient = new BlobServiceClient(_configuration.StorageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_configuration.StorageCollabContainer);
            BlobClient blobClient = containerClient.GetBlobClient($"{instanceId}/{fileName}");

            IDictionary<string, string> metadata = new Dictionary<string, string>();
            if (collabPageFileMetaData != null)
            {
                metadata = collabPageFileMetaData
                    .GetType()
                    .GetProperties()
                    .Where(property => property.GetValue(collabPageFileMetaData) != null)
                    .ToDictionary(
                        property => property.Name,
                        property => JsonSerializer.Serialize(property.GetValue(collabPageFileMetaData))
                    );
            }

            await blobClient.SetMetadataAsync(metadata);

            return true; 
        } catch {
            return false;
        }
    }   

    public async Task<CollabPageFileMetaData> GetMetadata(string fileName, string instanceId)
    {
        try {
            BlobServiceClient blobServiceClient = new BlobServiceClient(_configuration.StorageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_configuration.StorageCollabContainer);
            BlobClient blobClient = containerClient.GetBlobClient($"{instanceId}/{fileName}");

            var blobProperties = await blobClient.GetPropertiesAsync();
            IDictionary<string, string> metadata = blobProperties.Value.Metadata;
            if (metadata.Count == 0)
            {
                return new CollabPageFileMetaData();
            }

            CollabPageFileMetaData collabPageFileMetaData = new CollabPageFileMetaData();
            foreach (PropertyInfo property in collabPageFileMetaData.GetType().GetProperties())
            {
                if (metadata.ContainsKey(property.Name))
                {
                    property.SetValue(collabPageFileMetaData, JsonSerializer.Deserialize(metadata[property.Name], property.PropertyType));
                }
            }

            return collabPageFileMetaData; 
        } catch {
            return new CollabPageFileMetaData();
        }
    }

    public async Task<string> GetOutputFileName(string fileName, string agentName, string instanceId)
    {
        string response = await Task.Run(() =>
            _configuration.AgentResponseFormat
                .Replace("{@InputFileName}", fileName)
                .Replace("{@AgentName}", agentName) 
                .Replace("{@GUID}", Guid.NewGuid().ToString())
        ); 
        return response;  
    }

    public async Task<List<string>> GetInstances()
    {
        try
        {
            BlobServiceClient blobServiceClient = new BlobServiceClient(_configuration.StorageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_configuration.StorageCollabContainer);

            var blobs = containerClient.GetBlobsAsync();
            List<string> instances = new List<string>();

            await foreach (BlobItem blobItem in blobs)
            {
                if (blobItem.Name.Contains("/"))
                {
                        instances.Add(blobItem.Name.Split("/").First());
                }
            }
            return instances;
        }
        catch
        {
            return new List<string>();
        } 
    }

    public async Task<bool> PutInput(
        string fileName, 
        string instanceId, 
        string content, 
        CollabPageFileMetaData? collabPageFileMetaData = null)
    {
        try {
            BlobServiceClient blobServiceClient = new BlobServiceClient(_configuration.StorageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_configuration.StorageCollabContainer);
            BlobClient blobClient = containerClient.GetBlobClient($"{instanceId}/{fileName}");

            IDictionary<string, string> metadata = new Dictionary<string, string>();
            if (collabPageFileMetaData != null)
            {
                metadata = collabPageFileMetaData
                    .GetType()
                    .GetProperties()
                    .Where(property => property.GetValue(collabPageFileMetaData) != null)
                    .ToDictionary(
                        property => property.Name,
                        property => JsonSerializer.Serialize(property.GetValue(collabPageFileMetaData))
                    );
            }   
            
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                await blobClient.DeleteIfExistsAsync();
                await blobClient.UploadAsync(stream, metadata: metadata);
                
            }
                        
            return true; 
        } catch  {
            return false;
        }
    }

}