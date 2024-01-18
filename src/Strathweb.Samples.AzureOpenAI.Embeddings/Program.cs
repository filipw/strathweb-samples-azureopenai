using System.Numerics.Tensors;
using Azure;
using Azure.AI.OpenAI;
using Spectre.Console;
using Strathweb.Samples.AzureOpenAI.Shared;

var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT") ??
                                 throw new ArgumentException("AZURE_OPENAI_SERVICE_ENDPOINT is mandatory");
var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ??
                            throw new ArgumentException("AZURE_OPENAI_API_KEY is mandatory");
var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME") ??
                                throw new ArgumentException("AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME is mandatory");

var date = args.Length == 1 ? args[0] : DateTime.UtcNow.ToString("yyyyMMdd");

var feed = await ArxivHelper.FetchArticles("cat:quant-ph", date);
if (feed == null) 
{
    Console.WriteLine("Failed to load the feed.");
    return;
}

var client = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint), new AzureKeyCredential(azureOpenAiServiceKey));

var baseline =
    """
Optimal Qubit Reuse for Near-Term Quantum Computers

Near-term quantum computations are limited by high error rates, the scarcity of qubits and low qubit connectivity. 
Increasing support for mid-circuit measurements and qubit reset in near-term quantum computers enables qubit reuse that may yield quantum computations with fewer qubits and lower errors. 
In this work, we introduce a formal model for qubit reuse optimization that delivers provably optimal solutions with respect to quantum circuit depth, number of qubits, or number of swap gates for the first time. 
This is in contrast to related work where qubit reuse is used heuristically or optimally but without consideration of the mapping effort. 
We further investigate reset errors on near-term quantum computers by performing reset error characterization experiments. 
Using the hereby obtained reset error characterization and calibration data of a near-term quantum computer, we then determine a qubit assignment that is optimal with respect to a given cost function. 
We define this cost function to include gate errors and decoherence as well as the individual reset error of each qubit. 
We found the reset fidelity to be state-dependent and to range, depending on the reset qubit, from 67.5% to 100% in a near-term quantum computer. 
We demonstrate the applicability of the developed method to a number of quantum circuits and show improvements in the number of qubits and swap gate insertions, estimated success probability, and Hellinger fidelity of the investigated quantum circuits.
""";

var baselineEmbedding = await client.GetEmbeddingsAsync(
    new EmbeddingsOptions(azureOpenAiDeploymentName, new[] { baseline }));
var baselineVector = baselineEmbedding.Value.Data[0].Embedding.ToArray();

var entriesWithEmbeddings = new List<EntryWithEmbeddingItem>();

foreach (var entry in feed.Entries)
{
    var embedding = await client.GetEmbeddingsAsync( 
        new EmbeddingsOptions(azureOpenAiDeploymentName,  new[] { entry.Title + Environment.NewLine + Environment.NewLine + entry.Summary }));
    
    var vector = embedding.Value.Data[0].Embedding.ToArray();
    var similarity = TensorPrimitives.CosineSimilarity(vector, baselineVector);
    
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