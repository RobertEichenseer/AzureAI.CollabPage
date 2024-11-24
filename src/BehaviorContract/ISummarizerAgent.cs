namespace FTA.AI.Agents.CollabPage.BehaviorContract;
using FTA.AI.Agents.CollabPage.AgentTooling;


[AgentInterface("Summarizer Agent", true)]
public interface ISummarizerAgent : IGrainWithStringKey
{
    
    [AgentEventHandler("Agent Event Handler", true)]
    Task<bool> PageEvent(string collabPageEvent); 
    Task<string> CreateSummary(string text);
    
}
