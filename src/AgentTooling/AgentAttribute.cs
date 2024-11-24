namespace FTA.AI.Agents.CollabPage.AgentTooling;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public class AgentClass : Attribute
{
    public string Description { get; set; } = "";
    public bool Agent {get; set;} = false;

    public AgentClass(string description, bool agent)
    {
        Description = description;
        Agent = agent;
    }
}

[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public class AgentInterface : Attribute
{
    public string Description { get; set; } = "";
    public bool Agent {get; set;} = false;

    public AgentInterface(string description, bool agent)
    {
        Description = description;
        Agent = agent;
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class AgentEventHandler : Attribute
{
    public string Description {get; set;} = "";
    public bool EventHandler {get; set;} = false; 

    public AgentEventHandler(string description, bool eventHandler)
    {
        Description = description;
        EventHandler = eventHandler; 
    }
}