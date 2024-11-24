namespace FTA.AI.Agents.CollabPage.AgentTooling;

public class Configuration {
    public string StorageConnectionString { get; set; } = "";
    public string StorageCollabContainer { get; set; } = "";
    public string ServiceBusConnectionString { get; set; } = "";
    public string ServiceBusTopicName { get; set; } = "";
    public string ServiceBusSubscriptionName { get; set; } = "";
    public string ServiceBusSubscriptionHumanName { get; set; } = "";
    public string AOAIEndPoint { get; set; } = "";
    public string AOAIApiKey { get; set; } = ""; 
    public string AOAIChatCompletionDeploymentName { get; set; } = ""; 
    public string AgentResponseFormat { get; set; } = "";
}