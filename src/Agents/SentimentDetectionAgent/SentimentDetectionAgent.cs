namespace CXP.AI.AgentRoom; 

using Microsoft.Extensions.Logging;
using FTA.AI.Agents.CollabPage.BehaviorContract;
using FTA.AI.Agents.CollabPage.AgentTooling;
using System.Text.Json;

[AgentClass("SentimentDetection Agent", true)]
public class SentimentDetectionAgentGrain : Grain, ISentimentDetectionAgent
{
    private readonly ILogger _logger;
    private readonly IPersistentState<StateSentimentDetectionAgent> _sentimentDetectionAgentState;
    private Configuration _configuration = new Configuration();
    private StorageTooling _storageTooling;
    private OpenAITooling _openAITooling;

    private string _agentPurpose = "Detect sentiment in provided text";
    
    private string _systemPrompt = @"
        You are an assistant which detects sentiment in provided Text. 
        You answer just with the sentiment you have detected.
        You don't answer with a full sentence.
    ";
    
    public SentimentDetectionAgentGrain(
        ILogger<SentimentDetectionAgentGrain> logger,
        [PersistentState("sentimentDetectionAgentState", "agentStore")] IPersistentState<StateSentimentDetectionAgent> agentState,
        Configuration configuration, 
        StorageTooling storageTooling,
        OpenAITooling openAITooling
    ) {
        _logger = logger;
        _sentimentDetectionAgentState = agentState;
        _configuration = configuration;
        _storageTooling = storageTooling;
        _openAITooling = openAITooling;

    }

    [AgentEventHandler("Agent Event Handler", true)]
    public async Task<bool> PageEvent(string collabPageEventSerialization)
    {
        //Restore state
        List<string> processedInput = await GetProcessedInput();
        List<string> providedInput = await GetProvidedInput(); 

        //Orleans Serializer can be added
        CollabPageEvent collabPageEvent = 
            JsonSerializer.Deserialize<CollabPageEvent>(collabPageEventSerialization) 
            ?? 
            new CollabPageEvent();

        if (String.IsNullOrEmpty(collabPageEvent.Id))
            return false;

        // Process input
        if (collabPageEvent.InputCreated) {
            if (
                    //already processed input
                    ! processedInput.Contains($"{collabPageEvent.InputFileName}-{collabPageEvent.Time}") 
                    &&
                    //self provided input
                    ! providedInput.Contains(collabPageEvent.InputFileName)
                    &&
                    //input from other agents
                    ! collabPageEvent.IsAgentCreated
                    && 
                    //can agent help with this task
                    await _openAITooling.TaskContribution(
                        collabPageEvent.ExpectedProcessingOutput,
                        _agentPurpose
                    )
                ) {
                    
                string inputContent = await _storageTooling.GetInputContent(collabPageEvent.InputFileName, collabPageEvent.InstanceId);
                string llmResponse = await DetectSentiment(inputContent);
                
                string outputFileName = await _storageTooling.GetOutputFileName(
                    collabPageEvent.InputFileName,
                    this.GetType().Name, 
                    collabPageEvent.InstanceId);

                CollabPageFileMetaData collabPageFileMetaData = new CollabPageFileMetaData(){
                    IsAgentCreated = true,
                    AgentName = this.GetType().Name,
                    InputFiles = new string[] {collabPageEvent.InputFileName},
                };
                await _storageTooling.StoreAgentResponse(outputFileName, collabPageEvent.InstanceId, llmResponse, collabPageFileMetaData);

                providedInput.Add(outputFileName);
                processedInput.Add(
                    $"{collabPageEvent.InputFileName}-{collabPageEvent.Time}"
                );
                await SetProvidedInput(providedInput);
                await SetProcessedInput(processedInput);
            }   
            else {
            }
        }

        return true; 
    }

    public async Task<string> DetectSentiment(string text)
    {
        string prompt = $"Detect the sentiment of: {text}";
        string response = await _openAITooling.GetChatCompletion(_systemPrompt, prompt);
        
        return response; 
    }

    private Task<List<string>> GetProvidedInput()
    {
        return Task.FromResult(_sentimentDetectionAgentState.State.ProvidedInput);
    }

    private async Task SetProvidedInput(List<string> providedInput)
    {
        _sentimentDetectionAgentState.State.ProvidedInput = providedInput;
        await _sentimentDetectionAgentState.WriteStateAsync();
    }

    public Task<List<string>> GetProcessedInput() {
        return Task.FromResult(_sentimentDetectionAgentState.State.ProcessedInput);
    }

    public async Task SetProcessedInput(List<string> processedInput)
    {
        _sentimentDetectionAgentState.State.ProcessedInput = processedInput;
        await _sentimentDetectionAgentState.WriteStateAsync();
    }

}