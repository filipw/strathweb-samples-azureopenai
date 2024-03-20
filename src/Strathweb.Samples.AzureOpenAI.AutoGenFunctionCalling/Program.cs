using System.Text.Json;
using AutoGen;
using AutoGen.Core;
using AutoGen.OpenAI;
using Azure;
using Azure.AI.OpenAI;
using Spectre.Console;
using Strathweb.Samples.AzureOpenAI.Shared;

var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT") ??
                                 throw new Exception("AZURE_OPENAI_SERVICE_ENDPOINT missing");

var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ??
                            throw new Exception("AZURE_OPENAI_API_KEY missing");

var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ??
                                throw new Exception("AZURE_OPENAI_DEPLOYMENT_NAME missing");

var systemInstructions = $"""
You are an AI assistant designed to support users in navigating the ArXiv browser application, focusing on functions related to quantum physics and quantum computing research. 
The application features specific functions that allow users to fetch papers and summarize them based on precise criteria. 
""";

var openAiClient = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint),
    new AzureKeyCredential(azureOpenAiServiceKey));

var arxivClient = new ArxivClient(openAiClient, azureOpenAiDeploymentName);
var gptConfig = new AzureOpenAIConfig(azureOpenAiServiceEndpoint, azureOpenAiDeploymentName, azureOpenAiServiceKey);

var assistantAgent = new AssistantAgent(
    name: "agent",
    systemMessage: systemInstructions,
    llmConfig: new ConversableAgentConfig
    {
        Temperature = 0,
        ConfigList = new[] { gptConfig },
        FunctionContracts = new[]
        {
            arxivClient.FetchPapersFunctionContract,
            arxivClient.SummarizePaperFunctionContract,
        },
    },
    functionMap: new Dictionary<string, Func<string, Task<string>>>
    {
        { "FetchPapers", arxivClient.FetchPapersWrapper }, 
        { "SummarizePaper", arxivClient.SummarizePaperWrapper }
    }
).RegisterMiddleware(async (messages, options, agent, ct) =>
{
    var reply = await agent.GenerateReplyAsync(messages, options, ct);

    var toolCall = reply.GetToolCalls()?.FirstOrDefault();
    if (toolCall != null)
    {
        var content = reply.GetContent();
        if (toolCall.FunctionName == "FetchPapers")
        {
            var feed = JsonSerializer.Deserialize<Feed>(content);
            VisualizePapers(feed.Entries);
            return reply;
        }
        
        if (toolCall.FunctionName == "SummarizePaper")
        {
            VisualizeSummary(content);
            return reply;
        }
    }

    return reply;
});

var userProxyAgent = new UserProxyAgent(
        name: "user",
        humanInputMode: HumanInputMode.ALWAYS)
    .RegisterPrintFormatMessageHook();

await userProxyAgent.InitiateChatAsync(
    receiver: assistantAgent,
    maxRound: 10);

void VisualizeSummary(string content)
{
    var panel = new Panel(content)
    {
        Header = new PanelHeader("Summary")
    };
    AnsiConsole.Write(panel);
}

void VisualizePapers(List<Entry> entries) 
{
    if (entries.Count == 0) 
    {
        Console.WriteLine("No items to show...");
        return;
    }

    var table = new Table
    {
        Border = TableBorder.HeavyHead
    };

    table.AddColumn("Id");
    table.AddColumn("Updated");
    table.AddColumn("Title");
    table.AddColumn("Authors");
    table.AddColumn("Link");

    foreach (var entry in entries)
    {
        table.AddRow(
            $"{Markup.Escape(entry.Id)}", 
            $"{Markup.Escape(entry.Updated.ToString("yyyy-MM-dd HH:mm:ss"))}", 
            $"{Markup.Escape(entry.Title)}", 
            $"{Markup.Escape(string.Join(", ", entry.Authors.Select(x => x.Name).ToArray()))}",
            $"[link={entry.PdfLink}]{entry.PdfLink}[/]"
        );
    }

    AnsiConsole.Write(table);
}

public partial class ArxivClient
{
    private readonly OpenAIClient _client;
    private readonly string _azureOpenAiDeploymentName;

    public ArxivClient(OpenAIClient client, string azureOpenAiDeploymentName)
    {
        _client = client;
        _azureOpenAiDeploymentName = azureOpenAiDeploymentName;
    }
    
    /// <summary>
    /// Fetches quantum computing papers from ArXiv for a given date
    /// </summary>
    /// <param name="date"></param>
    [Function]
    public async Task<string> FetchPapers(DateTime date)
    {
        //var query = searchQuery == SearchQuery.QuantumPhysics ? "cat:quant-ph" : "ti:\"quantum computing\"";
        var feed = await ArxivHelper.FetchArticles("ti:\"quantum computing\"", date.ToString("yyyyMMdd"));
        var feedJson = JsonSerializer.Serialize(feed);
        return feedJson;
    }
    
    /// <summary>
    /// Summarizes a given paper based on the ArXiv ID of the paper.
    /// </summary>
    /// <param name="paperId"></param>
    [Function]
    public async Task<string> SummarizePaper(string paperId)
    {
        var feed = await ArxivHelper.FetchArticleById(paperId);
        if (feed == null || feed.Entries.Count == 0)
        {
            return "Paper not found";
        }
        
        if (feed.Entries.Count > 1)
        {
            return "More than one match for this ID!";
        }

        var prompt = $"Title: {feed.Entries[0].Title}{Environment.NewLine}Abstract: {feed.Entries[0].Summary}";
        var systemPrompt = """
        You are a summarization engine for ArXiv papers. You will take in input in the form of paper title and abstract, and summarize them in a digestible 1-2 sentence format.
        Each summary should be a simple, plain text, separate paragraph.
    """;
        var completionsOptions = new ChatCompletionsOptions
        {
            Temperature = 0,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            MaxTokens = 400,
            DeploymentName = _azureOpenAiDeploymentName,
            Messages =
            {
                new ChatRequestSystemMessage(systemPrompt),
                new ChatRequestUserMessage(prompt)
            }
        };
        var completionsResponse = await _client.GetChatCompletionsAsync(completionsOptions);
        if (completionsResponse.Value.Choices.Count == 0)
        {
            return "No response available";
        }

        var preferredChoice = completionsResponse.Value.Choices[0];
        return preferredChoice.Message.Content.Trim();
    }
}
