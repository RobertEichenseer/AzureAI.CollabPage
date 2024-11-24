namespace CXP.AI.AgentRoom; 

using Microsoft.Extensions.Logging;
using FTA.AI.Agents.CollabPage.BehaviorContract;
using FTA.AI.Agents.CollabPage.AgentTooling;
using System.Text.Json;

[AgentClass("LanguageDetection Agent", true)]
public class LanguageDetectionAgentGrain : Grain, ILanguageDetectionAgent
{
    private readonly ILogger _logger;
    private readonly IPersistentState<StateLanguageDetectionAgent> _languageDetectionAgentState;
    private Configuration _configuration = new Configuration();
    private StorageTooling _storageTooling;
    private OpenAITooling _openAITooling;
    private string _agentPurpose = "Detect language in provided text";

    private string _systemPrompt = @"
        You are an assistant which detects language in provided Text. 
        You answer with the Language you have detected.
    "; 

    private string _prompt = @"Detect the language of: |||INPUT|||";

    public LanguageDetectionAgentGrain(
        ILogger<LanguageDetectionAgentGrain> logger,
        [PersistentState("languageDetectionAgentState", "agentStore")] IPersistentState<StateLanguageDetectionAgent> agentState,
        Configuration configuration, 
        StorageTooling storageTooling,
        OpenAITooling openAITooling
    ) {
        _logger = logger;
        _languageDetectionAgentState = agentState;
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

        CollabPageEvent collabPageEvent = 
            JsonSerializer.Deserialize<CollabPageEvent>(collabPageEventSerialization) 
            ?? 
            new CollabPageEvent();

        if (String.IsNullOrEmpty(collabPageEvent.Id))
            return false;

        // Process input
        if (collabPageEvent.InputCreated) {
            if (
                    ! processedInput.Contains($"{collabPageEvent.InputFileName}-{collabPageEvent.Time}") 
                    &&
                    ! providedInput.Contains(collabPageEvent.InputFileName)
                    &&
                    ! collabPageEvent.IsAgentCreated
                    && 
                    await _openAITooling.TaskContribution(
                        collabPageEvent.ExpectedProcessingOutput,
                        _agentPurpose)
                ) {
                    
                string inputContent = await _storageTooling.GetInputContent(collabPageEvent.InputFileName, collabPageEvent.InstanceId);
                string llmResponse = await DetectLanguage(inputContent);

                string currentClassName = this.GetType().Name;
                
                string outputFileName = await _storageTooling.GetOutputFileName(
                    collabPageEvent.InputFileName,
                    this.GetType().Name, 
                    collabPageEvent.InstanceId);

                providedInput.Add(outputFileName);
                processedInput.Add(
                    $"{collabPageEvent.InputFileName}-{collabPageEvent.Time}"
                );
                await SetProvidedInput(providedInput);
                await SetProcessedInput(processedInput);

                CollabPageFileMetaData collabPageFileMetaData = new CollabPageFileMetaData(){
                    IsAgentCreated = true,
                    AgentName = this.GetType().Name,
                    InputFiles = new string[] {collabPageEvent.InputFileName},
                };
                await _storageTooling.StoreAgentResponse(outputFileName, collabPageEvent.InstanceId, llmResponse, collabPageFileMetaData);
            }   
            else {
            }
        }

        return true; 
    }
    public async Task<string> DetectLanguage(string text)
    {
        _prompt = _prompt.Replace("|||INPUT|||", text);
        string response = await _openAITooling.GetChatCompletion(_systemPrompt, _prompt);
        
        return response; 
    }

    private Task<List<string>> GetProvidedInput()
    {
        return Task.FromResult(_languageDetectionAgentState.State.ProvidedInput);
    }

    private async Task SetProvidedInput(List<string> providedInput)
    {
        _languageDetectionAgentState.State.ProvidedInput = providedInput;
        await _languageDetectionAgentState.WriteStateAsync();
    }

    public Task<List<string>> GetProcessedInput() {
        return Task.FromResult(_languageDetectionAgentState.State.ProcessedInput);
    }

    public async Task SetProcessedInput(List<string> processedInput)
    {
        _languageDetectionAgentState.State.ProcessedInput = processedInput;
        await _languageDetectionAgentState.WriteStateAsync();
    }


}