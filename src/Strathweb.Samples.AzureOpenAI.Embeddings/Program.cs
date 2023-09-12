using Azure;
using Azure.AI.OpenAI;
using Microsoft.SemanticKernel.AI.Embeddings.VectorOperations;
using Spectre.Console;
using Strathweb.Samples.AzureOpenAI.Shared;

var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT") ??
                                 throw new ArgumentException("AZURE_OPENAI_SERVICE_ENDPOINT is mandatory");
var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ??
                            throw new ArgumentException("AZURE_OPENAI_API_KEY is mandatory");
var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME") ??
                                throw new ArgumentException("AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME is mandatory");

var date = args.Length == 1 ? args[0] : DateTime.UtcNow.ToString("yyyyMMdd");

var feed = await ArxivHelper.FetchArticles(date);
if (feed == null) 
{
    Console.WriteLine("Failed to load the feed.");
    return;
}

var client = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint), new AzureKeyCredential(azureOpenAiServiceKey));

var liked =
    """
A pragma based C++ framework for hybrid quantum/classical computation

  Quantum computers promise exponential speed ups over classical computers for
various tasks. This emerging technology is expected to have its first huge
impact in High Performance Computing (HPC), as it can solve problems beyond the
reach of HPC. To that end, HPC will require quantum accelerators, which will
enable applications to run on both classical and quantum devices, via hybrid
quantum-classical nodes. Hybrid quantum-HPC applications should be scalable,
executable on Quantum Error Corrected (QEC) devices, and could use
quantum-classical primitives. However, the lack of scalability, poor
performances, and inability to insert classical schemes within quantum
applications has prevented current quantum frameworks from being adopted by the
HPC community.
""";

var likedEmbedding = await client.GetEmbeddingsAsync(azureOpenAiDeploymentName, 
    new EmbeddingsOptions(liked));
var likedVector = likedEmbedding.Value.Data[0].Embedding.ToArray();

var entriesWithEmbeddings = new List<EntryWithEmbeddingItem>();

foreach (var entry in feed.Entries)
{
    var embedding = await client.GetEmbeddingsAsync(azureOpenAiDeploymentName, 
        new EmbeddingsOptions(entry.Title + Environment.NewLine + Environment.NewLine + entry.Summary));
    
    var vector = embedding.Value.Data[0].Embedding.ToArray();
    var similarity = vector.CosineSimilarity(likedVector);
    
    entriesWithEmbeddings.Add(new EntryWithEmbeddingItem
    {
        Embedding = embedding.Value.Data[0].Embedding.ToArray(),
        SimilarityToBaseline = similarity,
        Entry = entry
    });
}

entriesWithEmbeddings = entriesWithEmbeddings.OrderByDescending(x => x.SimilarityToBaseline).ToList();

var table = new Table
{
    Border = TableBorder.HeavyHead
};

table.AddColumn("Updated");
table.AddColumn("Title");
table.AddColumn("Authors");
table.AddColumn("Link");
table.AddColumn(new TableColumn("Similarity").Centered());

foreach (var entryWithEmbedding in entriesWithEmbeddings)
{
    var color = entryWithEmbedding.SimilarityToBaseline switch
    {
        <= 0.75 => "red",
        > 0.75 and <= 0.8 => "yellow",
        _ => "green"
    };

    table.AddRow(
        $"[{color}]{Markup.Escape(entryWithEmbedding.Entry.Updated.ToString("yyyy-MM-dd HH:mm:ss"))}[/]", 
        $"[{color}]{Markup.Escape(entryWithEmbedding.Entry.Title)}[/]", 
        $"[{color}]{Markup.Escape(string.Join(", ", entryWithEmbedding.Entry.Authors.Select(x => x.Name).ToArray()))}[/]",
        $"[link={entryWithEmbedding.Entry.PdfLink} {color}]{entryWithEmbedding.Entry.PdfLink}[/]",
        $"[{color}]{Markup.Escape(entryWithEmbedding.SimilarityToBaseline.ToString())}[/]"
    );
}

AnsiConsole.Write(table);

class EntryWithEmbeddingItem
{
    public Entry Entry { get; set; }
    
    public IReadOnlyList<float> Embedding { get; set; }
    
    public double SimilarityToBaseline { get; set; }
}