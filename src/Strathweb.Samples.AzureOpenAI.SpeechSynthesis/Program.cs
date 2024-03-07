using System.Text;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Azure;
using Azure.AI.OpenAI;
using Spectre.Console;
using Strathweb.Samples.AzureOpenAI.Shared;

var date = args.Length == 1 ? args[0] : DateTime.UtcNow.ToString("yyyyMMdd");

var feed = await ArxivHelper.FetchArticles("ti:\"quantum computing\"", date);

if (feed == null) 
{
    Console.WriteLine("Failed to load the feed.");
    return;
}

if (TryWriteOutItems(feed))
{
    Console.WriteLine();

    var inputEntries = feed.Entries.Select(e => $"Title: {e.Title}{Environment.NewLine}Abstract: {e.Summary}");
    await EnhanceWithOpenAi(string.Join(Environment.NewLine, inputEntries));
}

async Task EnhanceWithOpenAi(string prompt)
{
    var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT") ??
                                     throw new ArgumentException("AZURE_OPENAI_SERVICE_ENDPOINT is mandatory");
    var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ??
                                throw new ArgumentException("AZURE_OPENAI_API_KEY is mandatory");
    var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ??
                                    throw new ArgumentException("AZURE_OPENAI_DEPLOYMENT_NAME is mandatory");

    var speechKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                    throw new ArgumentException("Missing SPEECH_KEY");
    var speechRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                       throw new ArgumentException("Missing SPEECH_REGION");
    
    var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
    speechConfig.SpeechSynthesisVoiceName = "en-GB-SoniaNeural";
    
    var audioOutputConfig = AudioConfig.FromDefaultSpeakerOutput();
    using var speechSynthesizer = new SpeechSynthesizer(speechConfig, audioOutputConfig);

    var client = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint), new AzureKeyCredential(azureOpenAiServiceKey));
    var systemPrompt = """
        You are a summarization engine for ArXiv papers. You will take in input in the form of paper title and abstract, and summarize them in a digestible 2-3 sentence format.
        Your output is going to stylistically resemble speech - that is, do not include any bullet points, numbering or other characters not appearing in spoken language. Each summary should be a simple, plain text, separate paragraph.
    """;
    var completionsOptions = new ChatCompletionsOptions
    {
        Temperature = 0,
        NucleusSamplingFactor = 1,
        FrequencyPenalty = 0,
        PresencePenalty = 0,
        MaxTokens = 800,
        DeploymentName = azureOpenAiDeploymentName,
        Messages =
        {
            new ChatRequestSystemMessage(systemPrompt),
            new ChatRequestUserMessage(prompt)
        }
    };
    var responseStream = await client.GetChatCompletionsStreamingAsync(completionsOptions);

    AnsiConsole.Write(new Rule("[green]Summary of the articles[/]"));
    
    var gptBuffer = new StringBuilder();
    await foreach (var completionUpdate in responseStream)
    {
        var message = completionUpdate.ContentUpdate;
        if (string.IsNullOrEmpty(message))
        {
            continue;
        }

        AnsiConsole.Write(message);
        gptBuffer.Append(message);

        // synthesize speech when encountering sentence breaks
        if (message.Contains("\n") || message.Contains(".") || message.Contains(","))
        {
            var sentence = gptBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(sentence))
            {
                await speechSynthesizer.SpeakTextAsync(sentence);
                gptBuffer.Clear();
            }
        }
    }
}

bool TryWriteOutItems(Feed feed) 
{
    if (feed.Entries.Count == 0) 
    {
        Console.WriteLine("No items today...");
        return false;
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

    return true;
}