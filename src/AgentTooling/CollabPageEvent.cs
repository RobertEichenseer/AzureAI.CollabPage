using System.Text.Json.Serialization;

namespace FTA.AI.Agents.CollabPage.AgentTooling;

//*****************************************************************
//* CollabPage Event
//*****************************************************************
public class CollabPageEvent
{
    private string _type = ""; 

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("type")]
    public string Type { 
        get {
            return _type;
        }
        set {
            _type = value;
            this.InputCreated = value == "Microsoft.Storage.BlobCreated";
            this.InputDeleted = value == "Microsoft.Storage.BlobDeleted";
        }
    }
    private string _subject = ""; 
    [JsonPropertyName("subject")]
    public string Subject { 
        get {
            return _subject;
        }
        set {
            _subject = value;

            string[] subject = value.Split('/');
            this.InstanceId = subject[value.Split('/').Length - 2];
            if (this.InstanceId.ToUpper() == "BLOB") {
                this.InstanceId = String.Empty;
            }
            this.InputFileName = subject[subject.Length - 1];
        }
    }
    [JsonPropertyName("time")]
    public DateTime Time { get; set; } = new DateTime();
    [JsonPropertyName("data")]
    public PageEventFileInfo Data { get; set; } = new PageEventFileInfo();
    public string InputFileName { get; set; } = "";
    public bool InputCreated { get; set; } = false;
    public bool InputDeleted { get; set; } = false;
    public string InstanceId {get;set;} = "";

    public string AgentName {get; set; } = "";
    public bool IsAgentCreated {get; set;} = false;
    public string[] InputFiles {get; set;} = new string[0];
    public string[] AdditionalOutputFiles {get; set;} = new string[0];
    public string ExpectedProcessingOutput {get; set;} = "";
}

public class PageEventFileInfo
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}


//*****************************************************************
//* CollabPage File Meta Data
//*****************************************************************
public class CollabPageFileMetaData
{
    public string AgentName { get; set; } = "";
    public bool IsAgentCreated { get; set; } = false;
    public string[] InputFiles { get; set; } = new string[0];
    public string[] AdditionalOutputFiles {get; set;} = new string[0];
    public string ExpectedProcessingOutput {get; set;} = "";
    public string [] TargetedAgents {get; set;} = new string[0];
}