namespace FTA.AI.Agents.CollabPage.BehaviorContract;
using FTA.AI.Agents.CollabPage.AgentTooling;


[AgentInterface("LanguageDetection Agent", true)]
public interface ILanguageDetectionAgent : IGrainWithStringKey
{
    
    [AgentEventHandler("Agent Event Handler", true)]
    Task<bool> PageEvent(string collabPageEvent); 
    Task<string> DetectLanguage(string text);
    
}
