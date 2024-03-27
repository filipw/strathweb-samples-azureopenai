using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Assistants;
using Strathweb.Samples.AzureOpenAI.Shared;

var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT") ??
                                 throw new Exception("AZURE_OPENAI_SERVICE_ENDPOINT missing");

var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ??
                            throw new Exception("AZURE_OPENAI_API_KEY missing");

var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ??
                                throw new Exception("AZURE_OPENAI_DEPLOYMENT_NAME missing");

var systemInstructions = $"""
You are an AI assistant designed to support users in navigating the ArXiv browser application, focusing on functions related to quantum physics and quantum computing research. 
The application features specific functions that allow users to fetch papers and summarize them based on precise criteria. Adhere to the following rules rigorously:

1.  **Direct Parameter Requirement:** 
When a user requests an action, directly related to the functions, you must never infer or generate parameter values, especially paper IDs, on your own. 
If a parameter is needed for a function call and the user has not provided it, you must explicitly ask the user to provide this specific information.

2.  **Mandatory Explicit Parameters:** 
For the function `SummarizePaper`, the `paperId` parameter is mandatory and must be provided explicitly by the user. 
If a user asks for a paper summary without providing a `paperId`, you must ask the user to provide the paper ID.

3.  **Avoid Assumptions:** 
Do not make assumptions about parameter values. 
If the user's request lacks clarity or omits necessary details for function execution, you are required to ask follow-up questions to clarify parameter values.

4.  **User Clarification:** 
If a user's request is ambiguous or incomplete, you should not proceed with function invocation. 
Instead, ask for the missing information to ensure the function can be executed accurately and effectively.

5. **Grounding in Time:**
Today is {DateTime.Now.ToString("D")}. When the user asks about papers from today, you will use that date. 
Yesterday was {DateTime.Now.AddDays(-1).ToString("D")}. You will correctly infer past dates.
Tomorrow will be {DateTime.Now.AddDays(1).ToString("D")}. You will ignore requests for papers from the future.
""";

var openAiClient = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint),
    new AzureKeyCredential(azureOpenAiServiceKey));

var arxivClient = new ArxivClient(openAiClient, azureOpenAiDeploymentName);
var client = new AssistantsClient(new Uri(azureOpenAiServiceEndpoint), new AzureKeyCredential(azureOpenAiServiceKey));

async Task<ToolOutput> InvokeTool(ArxivClient client, RequiredFunctionToolCall requiredFunctionToolCall)
{
    if (requiredFunctionToolCall.Name == "FetchPapers")
    {
        var doc = JsonDocument.Parse(requiredFunctionToolCall.Arguments);
        var root = doc.RootElement;

        var searchQueryString = root.GetProperty("searchQuery").GetString();
        var searchQuery = Enum.Parse<SearchQuery>(searchQueryString);
        var date = root.GetProperty("date").GetDateTime();

        var feed = await client.FetchPapers(searchQuery, date);
        return new ToolOutput(requiredFunctionToolCall, feed);
    }

    if (requiredFunctionToolCall.Name == "SummarizePaper")
    {
        var doc = JsonDocument.Parse(requiredFunctionToolCall.Arguments);
        var root = doc.RootElement;

        var paperId = root.GetProperty("paperId").GetString();
        var summary = await client.SummarizePaper(paperId);

        return new ToolOutput(requiredFunctionToolCall, summary);
    }

    return null;
}

var assistantResponse = await client.CreateAssistantAsync(
    new AssistantCreationOptions(azureOpenAiDeploymentName)
    {
        Name = "Arxiv Helper Assistant",
        Instructions = systemInstructions,
        Tools =
        {
            new FunctionToolDefinition("FetchPapers",
                "Fetches quantum physics or quantum computing papers from ArXiv for a given date",
                BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        searchQuery =
                            new { type = "string", @enum = new[] { "QuantumPhysics", "QuantumComputing" } },
                        date = new { type = "string", format = "date" }
                    },
                    required = new[] { "searchQuery", "date" }
                })),
            new FunctionToolDefinition("SummarizePaper",
                "Summarizes a given paper based on the ArXiv ID of the paper.",
                BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new { paperId = new { type = "string" }, },
                    required = new[] { "paperId" }
                }))
        },
    });

var assistant = assistantResponse.Value;
var threadResponse = await client.CreateThreadAsync();
var thread = threadResponse.Value;

while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();

    var messageResponse = await client.CreateMessageAsync(
        thread.Id,
        MessageRole.User,
        prompt);

    var runResponse = await client.CreateRunAsync(thread, assistant);

    while (IsRunPending(runResponse))
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        runResponse = await client.GetRunAsync(thread.Id, runResponse.Value.Id);

        if (RequiresAction(runResponse, out var submitToolOutputsAction))
        {
            var toolOutputs = await ProcessToolCalls(arxivClient, submitToolOutputsAction);
            runResponse = await client.SubmitToolOutputsToRunAsync(runResponse.Value, toolOutputs);
        }
    }

    var messages = await client.GetMessagesAsync(thread.Id, 1);
    var lastMessage = messages.Value.Data.LastOrDefault();

    if (lastMessage != null)
    {
        foreach (var contentItem in lastMessage.ContentItems)
        {
            if (contentItem is MessageTextContent textItem)
            {
                Console.Write(textItem.Text);
            }
        
            Console.WriteLine();
        }
    }
}

bool IsRunPending(Response<ThreadRun> response)
    => response.Value.Status == RunStatus.Queued || response.Value.Status == RunStatus.InProgress;

bool RequiresAction(Response<ThreadRun> response, out SubmitToolOutputsAction submitToolOutputsAction)
{
    submitToolOutputsAction = null;

    if (response.Value.Status == RunStatus.RequiresAction &&
        response.Value.RequiredAction is SubmitToolOutputsAction action)
    {
        submitToolOutputsAction = action;
        return true;
    }

    return false;
}

async Task<List<ToolOutput>> ProcessToolCalls(ArxivClient arxivClient, SubmitToolOutputsAction submitToolOutputsAction)
{
    var toolOutputs = new List<ToolOutput>();
    foreach (var toolCall in submitToolOutputsAction.ToolCalls)
    {
        if (toolCall is RequiredFunctionToolCall requiredFunctionToolCall)
        {
            var toolOutput = await InvokeTool(arxivClient, requiredFunctionToolCall);
            toolOutputs.Add(toolOutput);
        }
    }

    return toolOutputs;
}

class ArxivClient
{
    private readonly OpenAIClient _client;
    private readonly string _azureOpenAiDeploymentName;

    public ArxivClient(OpenAIClient client, string azureOpenAiDeploymentName)
    {
        _client = client;
        _azureOpenAiDeploymentName = azureOpenAiDeploymentName;
    }

    public async Task<string> FetchPapers(SearchQuery searchQuery, DateTime date)
    {
        var query = searchQuery == SearchQuery.QuantumPhysics ? "cat:quant-ph" : "ti:\"quantum computing\"";
        var feed = await ArxivHelper.FetchArticles(query, date.ToString("yyyyMMdd"));
        return string.Join(Environment.NewLine,
            feed.Entries.Select(entry =>
                $"{entry.Id} | {entry.Updated} | {entry.Title} | {string.Join(", ", entry.Authors.Select(x => x.Name).ToArray())} | {entry.PdfLink}"));
    }

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
            Messages = { new ChatRequestSystemMessage(systemPrompt), new ChatRequestUserMessage(prompt) }
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

enum SearchQuery
{
    QuantumPhysics,
    QuantumComputing
}
