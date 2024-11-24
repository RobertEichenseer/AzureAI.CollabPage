[Serializable]
public class StateSummarizerAgent
{
    public List<string> ProcessedInput { get; set; } = new List<string>();
    public List<string> ProvidedInput { get; set; } = new List<string>();
    public string LastLLMResponse { get; set; } = "";
    public List<string> LLMResponses { get; set; } = new List<string>();
    public string Result { get; set; } = "";

}