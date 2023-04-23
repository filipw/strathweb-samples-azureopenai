using System.Text.Json;
using System.Xml.Linq;
using Azure;
using Azure.AI.OpenAI;
using Spectre.Console;

var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT");
var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");

var feedItems = new List<FeedItem>();
var feedUrl = "https://export.arxiv.org/rss/quant-ph";
var httpClient = new HttpClient();
var httpResponse = await httpClient.GetAsync(feedUrl);

if (httpResponse.IsSuccessStatusCode)
{
    var xmlContent = await httpResponse.Content.ReadAsStringAsync();
    var xDoc = XDocument.Parse(xmlContent);
    var defaultNs = xDoc.Root.GetDefaultNamespace();
    var items = xDoc.Root.Descendants().Where(x => x.Name.LocalName == "item");
    feedItems = items.Select(x => new FeedItem(x, defaultNs)).ToList();
}
else
{
    Console.WriteLine("Failed to load the feed.");
    return;
}

var results = await GetAIRatings(azureOpenAiDeploymentName, feedItems);

foreach (var rated in results) 
{
    var feedItem = feedItems.FirstOrDefault(x => x.Id == rated.Id);
    if (feedItem != null)
    {
        feedItem.Rating = rated.R;
    }
}

feedItems = feedItems.OrderByDescending(x => x.Rating).ToList();
WriteOutItems(feedItems);

void WriteOutItems(IList<FeedItem> feedItems) {
    var table = new Table
    {
        Border = TableBorder.HeavyHead
    };

    // Add some columns
    table.AddColumn(new TableColumn("Rating").Centered());
    table.AddColumn("Title");
    table.AddColumn("Link");

    table.Columns[1].Padding(0, 10);

    foreach (var feedItem in feedItems)
    {
        var color = feedItem.Rating switch
        {
            1 => "red",
            2 => "yellow",
            3 => "yellow",
            4 => "green",
            5 => "green",
            _ => "white"
        };
        table.AddRow($"[{color}]{Markup.Escape(feedItem.Rating.ToString())}[/]", Markup.Escape(feedItem.Title), $"[link={feedItem.Link}]{feedItem.Id}[/]");
    }

    AnsiConsole.Write(table);
}

async Task<RatedArticleResponse[]> GetAIRatings(string azureOpenAiDeploymentName, IList<FeedItem> feedItems)
{
    var client = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint), new AzureKeyCredential(azureOpenAiServiceKey));

    var completionsOptions = new CompletionsOptions()
    {
        Temperature = 0,
        MaxTokens = 2000,
        NucleusSamplingFactor = 1,
        FrequencyPenalty = 0,
        PresencePenalty = 0,
        GenerationSampleCount = 1,
    };

    var input = string.Join("\n", feedItems.Select(x => x.Id + ", " + x.Title));

    completionsOptions.Prompts.Add(
    """
Rate on a scale of 1-5 how relevant each headline is to quantum computing software engineers. 
Titles mentioning software, algorithms and error correction should be rated highly. Quantum computing hardware topics should be rated lower. Other quantum physics topics should get low rating. Produce result in JSON format as specified in the output example.

<Input>
1234.56789, Quantum Error Correction For Dummies.
4567.45678, Fast quantum search algorithm modelling on conventional computers: Information analysis of termination problem.
4566.32262, A pedagogical revisit on the hydrogen atom induced by a uniform static electric field

<Output>
[
    {"Id": "1234.56789", "R": 5},
    {"Id": "4567.45678", "R": 4},
    {"Id": "4566.32262", "R": 1}
]

<Input>
""" + "\n" + input + "\n" + "<Output>"
    );

    var completionsResponse = await client.GetCompletionsAsync(azureOpenAiDeploymentName, completionsOptions);
    if (completionsResponse.Value.Choices.Count == 0)
    {
        Console.WriteLine("No completions found.");
        return Array.Empty<RatedArticleResponse>();
    }

    var preferredChoice = completionsResponse.Value.Choices[0];
    var rawJsonResponse = preferredChoice.Text.Trim();

    // debug only
    // Console.WriteLine("Raw JSON response: " + rawJsonResponse);

    var results = JsonSerializer.Deserialize<RatedArticleResponse[]>(rawJsonResponse);

    return results;
}