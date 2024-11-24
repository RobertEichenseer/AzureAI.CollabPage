namespace FTA.AI.Agents.CollabPage.BehaviorContract;
using FTA.AI.Agents.CollabPage.AgentTooling;


[AgentInterface("SentimentDetection Agent", true)]
public interface ISentimentDetectionAgent : IGrainWithStringKey
{
    
    [AgentEventHandler("Agent Event Handler", true)]
    Task<bool> PageEvent(string collabPageEvent); 
    Task<string> DetectSentiment(string text);

}
