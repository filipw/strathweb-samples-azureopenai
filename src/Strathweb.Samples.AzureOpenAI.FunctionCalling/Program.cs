using System.Text;
using System.Text.Json;
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

var systemInstructions = """
You are an AI assistant for the ArXiv browser application. The application allows the user to perform ArXiv-related activities specified in the attached functions. 
Don't make assumptions about what arguments to use with functions - DO ask a follow up question to clarify argument values. Do not select a function if the user query is ambiguous.
When you invoke a function, provide an additional non-technical message confirming that you performed an activity.
Current year is 2024.
""";

var openAiClient = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint),
            new AzureKeyCredential(azureOpenAiServiceKey));

var arxivClient = new ArxivClient(openAiClient, azureOpenAiDeploymentName);

while (true)
{
    var prompt = Console.ReadLine();
    var request = new ChatCompletionsOptions
    {
        DeploymentName = azureOpenAiDeploymentName,
        Messages =
        {
            new ChatRequestSystemMessage(systemInstructions),
            new ChatRequestUserMessage(prompt)
        },
        Temperature = 1,
        MaxTokens = 400,
        NucleusSamplingFactor = 1f,
        Functions =
        {
            new FunctionDefinition
            {
                Description = "Fetches quantum physics or quantum computing papers from ArXiv for a given date",
                Name = "FetchPapers",
                Parameters = BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        searchQuery = new { type = "string", @enum = new[] { "QuantumPhysics", "QuantumComputing" } },
                        date = new { type = "string", format = "date" }
                    },
                    required = new[] { "searchQuery", "date" }
                })
            },
            new FunctionDefinition
            {
                Description = "Summarizes a given paper based on the title and abstract",
                Name = "SummarizePaper",
                Parameters = BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string" },
                        paperAbstract = new { type = "string" },
                    },
                    required = new[] { "title", "paperAbstract" }
                })

            }
        }
    };

    var completionResponse = await openAiClient.GetChatCompletionsStreamingAsync(request);
    var functionParams = new StringBuilder();
    string functionCall = null;
    AnsiConsole.Markup(":robot: ");
    await foreach (var message in completionResponse)
    {
        if (!string.IsNullOrWhiteSpace(message.FunctionName))
        {
            functionCall = message.FunctionName;
        }

        if (message.FunctionArgumentsUpdate != null)
        {
            functionParams.Append(message.FunctionArgumentsUpdate);
        }
        
        if (message.ContentUpdate != null)
        {
            Console.Write(message.ContentUpdate);
        }
    }

    if (functionCall != null)
    {
        await InvokeFunction(arxivClient, functionCall, functionParams.ToString());
    }

    Console.WriteLine();
}

async Task InvokeFunction(ArxivClient client, string functionName, string functionArguments)
{
    if (functionName == "FetchPapers")
    {
        Console.WriteLine("Params: " + functionArguments);
        var doc = JsonDocument.Parse(functionArguments);
        var root = doc.RootElement;

        var searchQueryString = root.GetProperty("searchQuery").GetString();
        var searchQuery = Enum.Parse<SearchQuery>(searchQueryString);
        var date = root.GetProperty("date").GetDateTime();

        var feed = await client.FetchPapers(searchQuery, date);
        WriteOutItems(feed);
        return;
    }
    
    if (functionName == "SummarizePaper")
    {
        Console.WriteLine("Params: " + functionArguments);
        return;
    }
    
    Console.WriteLine("Unknown function");
}

void WriteOutItems(Feed feed) 
{
    if (feed.Entries.Count == 0) 
    {
        Console.WriteLine("No items to show...");
        return;
    }

    var table = new Table
    {
        Border = TableBorder.HeavyHead
    };

    table.AddColumn("Updated");
    table.AddColumn("Title");
    table.AddColumn("Authors");
    table.AddColumn("Link");

    foreach (var entry in feed.Entries)
    {
        table.AddRow(
            $"{Markup.Escape(entry.Updated.ToString("yyyy-MM-dd HH:mm:ss"))}", 
            $"{Markup.Escape(entry.Title)}", 
            $"{Markup.Escape(string.Join(", ", entry.Authors.Select(x => x.Name).ToArray()))}",
            $"[link={entry.PdfLink}]{entry.PdfLink}[/]"
        );
    }

    AnsiConsole.Write(table);
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
    
    public async Task<Feed> FetchPapers(SearchQuery searchQuery, DateTime date)
    {
        var query = searchQuery == SearchQuery.QuantumPhysics ? "cat:quant-ph" : "ti:\"quantum computing\"";
        var feed = await ArxivHelper.FetchArticles(query, date.ToString("yyyyMMdd"));
        return feed;
    }
    
    public async Task<string> SummarizePaper(string title, string paperAbstract)
    {
        var prompt = $"Title: {title}{Environment.NewLine}Abstract: {paperAbstract}";
        var systemPrompt = """
        You are a summarization engine for ArXiv papers. You will take in input in the form of paper title and abstract, and summarize them in a digestible 1-2 sentence format.
        Each summary should be a simple, plain text, separate paragraph.
    """;
        var completionsOptions = new ChatCompletionsOptions
        {
            Temperature = 0,
            NucleusSamplingFactor = 1,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            MaxTokens = 1000,
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

enum SearchQuery
{
    QuantumPhysics,
    QuantumComputing
}