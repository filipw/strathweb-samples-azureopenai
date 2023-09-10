using System.Text.Json;
using System.Xml.Linq;
using Azure;
using Azure.AI.OpenAI;
using Spectre.Console;

var date = args.Length == 1 ? args[0] : DateTime.UtcNow.ToString("yyyyMMdd");

// cap at 40 results
var feedUrl = $"http://export.arxiv.org/api/query?search_query=cat:quant-ph+AND+submittedDate:[{date}0000+TO+{date}2359]&start=0&max_results=40&sortBy=submittedDate&sortOrder=descending";
var httpClient = new HttpClient();
var httpResponse = await httpClient.GetAsync(feedUrl);

Feed feed = null;
if (httpResponse.IsSuccessStatusCode)
{
    var ns = XNamespace.Get("http://www.w3.org/2005/Atom");
    var opensearch = XNamespace.Get("http://a9.com/-/spec/opensearch/1.1/");
    var arxiv = XNamespace.Get("http://arxiv.org/schemas/atom");

    var xmlContent = await httpResponse.Content.ReadAsStringAsync();
    var xDoc = XDocument.Parse(xmlContent);
    var feedElement = xDoc.Element(ns + "feed");

    feed = new Feed
    {
        Title = (string)feedElement.Element(ns + "title"),
        Id = (string)feedElement.Element(ns + "id"),
        Updated = (DateTime)feedElement.Element(ns + "updated"),
        TotalResults = (int)feedElement.Element(opensearch + "totalResults"),
        StartIndex = (int)feedElement.Element(opensearch + "startIndex"),
        ItemsPerPage = (int)feedElement.Element(opensearch + "itemsPerPage"),
        Entries = feedElement.Elements(ns + "entry").Select(entryElement => new Entry
        {
            Id = ((string)entryElement.Element(ns + "id")).Split(".").Last()[..^2],
            Updated = (DateTime)entryElement.Element(ns + "updated"),
            Published = (DateTime)entryElement.Element(ns + "published"),
            Title = (string)entryElement.Element(ns + "title"),
            Summary = (string)entryElement.Element(ns + "summary"),
            Authors = entryElement.Elements(ns + "author").Select(authorElement => new Author
            {
                Name = (string)authorElement.Element(ns + "name")
            }).ToList(),
            PdfLink = entryElement.Elements(ns + "link").FirstOrDefault(link => (string)link.Attribute("title") == "pdf")?.Attribute("href")?.Value,
            PrimaryCategory = (string)entryElement.Element(arxiv + "primary_category")?.Attribute("term")?.Value,
            Categories = entryElement.Elements(ns + "category").Select(category => (string)category.Attribute("term")).ToList()
        }).ToList()
    };
}

if (feed == null) 
{
    Console.WriteLine("Failed to load the feed.");
    return;
}

var results = await GetAIRatings(feed);

foreach (var rated in results) 
{
    var existingEntry = feed.Entries.FirstOrDefault(x => x.Id == rated.Id);
    if (existingEntry != null)
    {
        existingEntry.Rating = rated.R;
    }
}

feed.Entries = feed.Entries.OrderByDescending(x => x.Rating).ThenByDescending(x => x.Updated).ToList();
WriteOutItems(feed);

void WriteOutItems(Feed feed) 
{
    if (feed.Entries.Count == 0) 
    {
        Console.WriteLine("No items today...");
        return;
    }

    var table = new Table
    {
        Border = TableBorder.HeavyHead
    };

    table.AddColumn(new TableColumn("Rating").Centered());
    table.AddColumn("Updated");
    table.AddColumn("Title");
    table.AddColumn("Authors");
    table.AddColumn("Link");

    foreach (var entry in feed.Entries)
    {
        var color = entry.Rating switch
        {
            1 => "red",
            2 or 3 => "yellow",
            4 or 5 => "green",
            _ => "white"
        };
        table.AddRow(
            $"[{color}]{Markup.Escape(entry.Rating.ToString())}[/]", 
            $"[{color}]{Markup.Escape(entry.Updated.ToString("yyyy-MM-dd HH:mm:ss"))}[/]", 
            $"[{color}]{Markup.Escape(entry.Title)}[/]", 
            $"[{color}]{Markup.Escape(string.Join(", ", entry.Authors.Select(x => x.Name).ToArray()))}[/]",
            $"[link={entry.PdfLink} {color}]{entry.PdfLink}[/]"
        );
    }

    AnsiConsole.Write(table);
}

async Task<RatedArticleResponse[]> GetAIRatings(Feed feed)
{
    var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT");
    var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
    var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");

    var client = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint), new AzureKeyCredential(azureOpenAiServiceKey));

    var completionsOptions = new CompletionsOptions()
    {
        Temperature = 0,
        MaxTokens = 2400,
        NucleusSamplingFactor = 1,
        FrequencyPenalty = 0,
        PresencePenalty = 0,
        GenerationSampleCount = 1,
    };

    var input = string.Join("\n", feed.Entries.Select(x => x.Id + ", " + x.Title));

    completionsOptions.Prompts.Add(
    """
Rate on a scale of 1-5 how relevant each headline is to quantum computing software engineers. 
Titles mentioning quantum frameworks, software, algorithms, machine learning and error correction should be rated highly. Quantum computing hardware topics should be rated lower. Other quantum physics topics should get low rating. Produce JSON result as specified in the output example.

<Input>
1, Quantum Error Correction For Dummies.
2, Quantum Algorithm for Unsupervised Anomaly Detection
3, Fast quantum search algorithm modelling on conventional computers: Information analysis of termination problem.
4, A pedagogical revisit on the hydrogen atom induced by a uniform static electric field

<Output>
[
    {"Id": "1", "R": 5},
    {"Id": "2", "R": 5},
    {"Id": "3", "R": 4},
    {"Id": "4", "R": 1}
]

<Input>
""" + "\n" + input + "\n" + "<Output>"
    );

    // debug only
    // Console.WriteLine("Raw input: " + completionsOptions.Prompts[0]);

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
