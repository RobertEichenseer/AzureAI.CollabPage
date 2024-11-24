namespace CXP.AI.AgentRoom; 

using Microsoft.Extensions.Logging;
using FTA.AI.Agents.CollabPage.BehaviorContract;
using FTA.AI.Agents.CollabPage.AgentTooling;
using System.Text.Json;

[AgentClass("Summarizer Agent", true)]
public class SummarizerAgentGrain : Grain, ISummarizerAgent
{
    private readonly ILogger _logger;
    private readonly IPersistentState<StateSummarizerAgent> _summarizerAgentState;
    private Configuration _configuration = new Configuration();
    private StorageTooling _storageTooling;
    private OpenAITooling _openAITooling;

    private string _systemPrompt = @"
        You are an assistant which takes two values as input.
        You detect language and sentiment within the input.
        You answer with a valid JSON object which includes language and sentiment.
        The JSON object looks like this {""language"":""en"", ""sentiment"":""positive""}
        If one of the needed values isn't provided you don't fill the JSON object with it.
        You don't modify the provided language or sentiment. You just add them to the JSON object.
    "; 

    private string _prompt = @"Transform the input to a valid JSON object: |||INPUT|||";

    public SummarizerAgentGrain(
        ILogger<SummarizerAgentGrain> logger,
        [PersistentState("summarizerAgentState", "agentStore")] IPersistentState<StateSummarizerAgent> agentState,
        Configuration configuration, 
        StorageTooling storageTooling,
        OpenAITooling openAITooling
    ) {
        _logger = logger;
        _summarizerAgentState = agentState;
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
                    collabPageEvent.IsAgentCreated
                ) {

                //Check for further agent input
                string inputContent = ""; 
                List<string> additionalAgentOutputs = await GetOtherInstanceAgentOutput(collabPageEvent.InstanceId, this.GetType().Name);
                foreach (string agentOutput in additionalAgentOutputs)
                {
                    inputContent = $"{inputContent}\n{await _storageTooling.GetInputContent(agentOutput, collabPageEvent.InstanceId)})";
                }

                string llmResponse = await CreateSummary(inputContent);
                
                string outputFileName = await _storageTooling.GetOutputFileName(
                    collabPageEvent.InputFileName,
                    this.GetType().Name, 
                    collabPageEvent.InstanceId);

                CollabPageFileMetaData collabPageFileMetaData = new CollabPageFileMetaData(){
                    IsAgentCreated = true,
                    AgentName = this.GetType().Name,
                    InputFiles = additionalAgentOutputs.ToArray<string>(),
                };

                if(await NewSummarizerAgentResponse(
                    collabPageEvent.InstanceId,
                    llmResponse, 
                    this.GetType().Name)
                ) {
                    await _storageTooling.StoreAgentResponse(outputFileName, collabPageEvent.InstanceId, llmResponse, collabPageFileMetaData);
                    processedInput.Add(
                        $"{collabPageEvent.InputFileName}-{collabPageEvent.Time}"
                    );
                }

                providedInput.Add(outputFileName);
                await SetProvidedInput(providedInput);
                await SetProcessedInput(processedInput);

            }   
            else {
            }
        }

        return true; 
    }

    private async Task<bool> NewSummarizerAgentResponse(string instanceId, string content, string agentName)
    {
        List<string> outputFiles = await _storageTooling.GetInstanceAgentOutput(instanceId, agentName, true);
        foreach (string outputFile in outputFiles)
        {
            string outputContent = await _storageTooling.GetInputContent(outputFile, instanceId);
            if (outputContent == content)
            {
                return false;
            }
        }
        return true;
    }

    private async Task<List<string>> GetOtherInstanceAgentOutput(string instanceId, string agentName)
    {
        return await _storageTooling.GetInstanceAgentOutput(instanceId, agentName, false);
    }

    public async Task<string> CreateSummary(string text)
    {
        _prompt = _prompt.Replace("|||INPUT|||", text);
        string response = await _openAITooling.GetChatCompletion(_systemPrompt, _prompt);
        
        return response; 
    }

    private Task<List<string>> GetProvidedInput()
    {
        return Task.FromResult(_summarizerAgentState.State.ProvidedInput);
    }

    private async Task SetProvidedInput(List<string> providedInput)
    {
        _summarizerAgentState.State.ProvidedInput = providedInput;
        await _summarizerAgentState.WriteStateAsync();
    }

    public Task<List<string>> GetProcessedInput() {
        return Task.FromResult(_summarizerAgentState.State.ProcessedInput);
    }

    public async Task SetProcessedInput(List<string> processedInput)
    {
        _summarizerAgentState.State.ProcessedInput = processedInput;
        await _summarizerAgentState.WriteStateAsync();
    }
}