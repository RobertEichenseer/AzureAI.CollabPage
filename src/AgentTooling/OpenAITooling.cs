using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace FTA.AI.Agents.CollabPage.AgentTooling;

public class OpenAITooling {

    Configuration _configuration;
    ChatClient _chatClient; 
    public OpenAITooling(Configuration configuration)
    {
        _configuration = configuration;

        ApiKeyCredential apiKeyCredential = new ApiKeyCredential(_configuration.AOAIApiKey);
        AzureOpenAIClient azureOpenAIClient = new AzureOpenAIClient(new Uri(_configuration.AOAIEndPoint), apiKeyCredential);
        _chatClient = azureOpenAIClient.GetChatClient(_configuration.AOAIChatCompletionDeploymentName);
    }

    public async Task<string> GetChatCompletion(string systemPrompt, string prompt)
    {
        ChatCompletionOptions chatComletionOptions = new ChatCompletionOptions(){
            Temperature = 0.0f,
            TopP = 0.0f,
            FrequencyPenalty = 0.7f,
            PresencePenalty = 0.7f,
        };
        
        ChatCompletion chatCompletion = await _chatClient.CompleteChatAsync(
            messages: new List<ChatMessage> {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(prompt)
            }, 
            options: chatComletionOptions
        );

        return chatCompletion.Content[0].Text;
    }

    public async Task<bool> TaskContribution(string expectedProcessingOutput, string agentPurpose)
    {
        string systemPrompt = @"
            You check if a specific task should be performed based on a given capability description.
            The description of the capability is: @@_agentPurpose@@ 
            You answer with ""true"" if the task should be processed. 
            You answer with ""false"" if the task should not be processed. 
            You answer with ""true"" if the task is just partially fulfilled.
            You answer with a valid JSON object.
            The JSON object is formatted as follows: {""processing"": true}
        ";
        systemPrompt = systemPrompt.Replace("@@_agentPurpose@@", agentPurpose);

        string prompt = @"
            Should the task be performed on this expected output: @@expectedProcessingOutput@@? 
        ";
        prompt = prompt.Replace("@@expectedProcessingOutput@@", expectedProcessingOutput);
        string response = await GetChatCompletion(systemPrompt, prompt);

        try
        {
            Dictionary<string, bool>? jsonResponse = JsonSerializer.Deserialize<Dictionary<string, bool>>(response);
            if (jsonResponse != null && jsonResponse.TryGetValue("processing", out bool responseValue))
            {
                return responseValue;
            }
        }
        catch {}
     
        return false; 
        
    }


}