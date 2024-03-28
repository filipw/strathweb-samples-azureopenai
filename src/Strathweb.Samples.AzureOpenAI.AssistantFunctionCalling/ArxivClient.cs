// Copyright (c) Microsoft Corporation. All rights reserved.
// ArxivClient.cs

using Azure.AI.OpenAI;
using Strathweb.Samples.AzureOpenAI.Shared;

namespace Strathweb.Samples.AzureOpenAI.AssistantFunctionCalling;

public class ArxivClient
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
