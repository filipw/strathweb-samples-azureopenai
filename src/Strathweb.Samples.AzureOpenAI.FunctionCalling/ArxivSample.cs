﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// ArxivSample.cs

using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Spectre.Console;
using Strathweb.Samples.AzureOpenAI.Shared;

namespace Strathweb.Samples.AzureOpenAI.FunctionCalling;

static class ArxivSample
{
    public static async Task Run(string azureOpenAiServiceEndpoint, string azureOpenAiServiceKey,
        string azureOpenAiDeploymentName)
    {
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

        var introMessage =
            "I'm an Arxiv AI Assistant! Ask me about quantum computing/physics papers from a given day, or ask me to summarize a paper!";
        AnsiConsole.MarkupLine($":robot: {introMessage}");
        
        var openAiClient = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint),
            new AzureKeyCredential(azureOpenAiServiceKey));

        var arxivClient = new ArxivClient(openAiClient, azureOpenAiDeploymentName);
        var executionHelper = new ExecutionHelper(arxivClient);
        
        var messageHistory = new List<ChatRequestMessage> { new ChatRequestSystemMessage(systemInstructions), new ChatRequestAssistantMessage(introMessage) };

        while (true)
        {
            Console.Write("> ");
            var prompt = Console.ReadLine();
            messageHistory.Add(new ChatRequestUserMessage(prompt));

            var request = new ChatCompletionsOptions(azureOpenAiDeploymentName, messageHistory)
            {
                Temperature = 0,
                MaxTokens = 400
            };
            
            foreach (var function in executionHelper.GetAvailableFunctions())
            {
                request.Functions.Add(function);
            }

            var completionResponse = await openAiClient.GetChatCompletionsStreamingAsync(request);
            var functionParams = new StringBuilder();
            var modelResponse = new StringBuilder();

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
                    modelResponse.Append(message.ContentUpdate);
                    Console.Write(message.ContentUpdate);
                }
            }
            
            var modelResponseText = modelResponse.ToString();
            if (!string.IsNullOrEmpty(modelResponseText))
            {
                messageHistory.Add(new ChatRequestAssistantMessage(modelResponseText));
            }

            if (functionCall != null)
            {
                AnsiConsole.WriteLine($"I'm calling a function called {functionCall}... Stay tuned...");
                var functionResult = await executionHelper.InvokeFunction(functionCall, functionParams.ToString());
                if (!functionResult)
                {
                    AnsiConsole.WriteLine("There was an error exeucting a function!");
                }
            }

            Console.WriteLine();
        }
    }

    private class ExecutionHelper
    {
        private readonly ArxivClient _arxivClient;

        public ExecutionHelper(ArxivClient arxivClient)
        {
            _arxivClient = arxivClient;
        }

        public List<FunctionDefinition> GetAvailableFunctions()
            => new()
            {
                new FunctionDefinition
                {
                    Description =
                        "Fetches quantum physics or quantum computing papers from ArXiv for a given date",
                    Name = "FetchPapers",
                    Parameters = BinaryData.FromObjectAsJson(new
                    {
                        type = "object",
                        properties = new
                        {
                            searchQuery =
                                new
                                {
                                    type = "string",
                                    @enum = new[] { "QuantumPhysics", "QuantumComputing" }
                                },
                            date = new { type = "string", format = "date" }
                        },
                        required = new[] { "searchQuery", "date" }
                    })
                },
                new FunctionDefinition
                {
                    Description = "Summarizes a given paper based on the ArXiv ID of the paper.",
                    Name = "SummarizePaper",
                    Parameters = BinaryData.FromObjectAsJson(new
                    {
                        type = "object",
                        properties = new { paperId = new { type = "string" }, },
                        required = new[] { "paperId" }
                    })
                }
            };

        public async Task<bool> InvokeFunction(string functionName, string functionArguments)
        {
            try
            {
                if (functionName == "FetchPapers")
                {
                    var doc = JsonDocument.Parse(functionArguments);
                    var root = doc.RootElement;

                    var searchQueryString = root.GetProperty("searchQuery").GetString();
                    var searchQuery = Enum.Parse<SearchQuery>(searchQueryString);
                    var date = root.GetProperty("date").GetDateTime();

                    var feed = await _arxivClient.FetchPapers(searchQuery, date);
                    WriteOutItems(feed.Entries);
                    return true;
                }

                if (functionName == "SummarizePaper")
                {
                    var doc = JsonDocument.Parse(functionArguments);
                    var root = doc.RootElement;

                    var paperId = root.GetProperty("paperId").GetString();
                    var summary = await _arxivClient.SummarizePaper(paperId);

                    Console.WriteLine();
                    var panel = new Panel(summary) { Header = new PanelHeader("Summary") };
                    AnsiConsole.Write(panel);
                    return true;
                }
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
                // in case of any error, send back to user in error state
            }

            return false;
        }
        
        private void WriteOutItems(List<Entry> entries) 
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
    }
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

enum SearchQuery
{
    QuantumPhysics,
    QuantumComputing
}
