// Copyright (c) Microsoft Corporation. All rights reserved.
// BookingSample.cs

using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Spectre.Console;

namespace Strathweb.Samples.AzureOpenAI.FunctionCalling;

static class BookingSample
{
    public static async Task Run(string azureOpenAiServiceEndpoint, string azureOpenAiServiceKey,
        string azureOpenAiDeploymentName)
    {

        var systemInstructions = $"""
You are an AI assistant designed to support users in searching and booking concert tickets. Adhere to the following rules rigorously:

1.  **Direct Parameter Requirement:** 
When a user requests an action, directly related to the functions, you must never infer or generate parameter values, especially IDs, band names or locations on your own. 
If a parameter is needed for a function call and the user has not provided it, you must explicitly ask the user to provide this specific information.

2.  **Avoid Assumptions:** 
Do not make assumptions about parameter values. 
If the user's request lacks clarity or omits necessary details for function execution, you are required to ask follow-up questions to clarify parameter values.

3.  **User Clarification:** 
If a user's request is ambiguous or incomplete, you should not proceed with function invocation. 
Instead, ask for the missing information to ensure the function can be executed accurately and effectively.

4. **Grounding in Time:**
Today is {DateTime.Now.ToString("D")}
Yesterday was {DateTime.Now.AddDays(-1).ToString("D")}. You will correctly infer past dates.
Tomorrow will be {DateTime.Now.AddDays(1).ToString("D")}.
""";

        var introMessage =
            "I'm a Concert Booking AI assistant! Ask me concerts and I can help you find them and book tickets!";
        AnsiConsole.MarkupLine($":robot: {introMessage}");

        var openAiClient = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint),
            new AzureKeyCredential(azureOpenAiServiceKey));
        var concertApi = new ConcertApi();
        var executionHelper = new ExecutionHelper(concertApi);
        
        var messageHistory = new List<ChatRequestMessage> { new ChatRequestSystemMessage(systemInstructions), new ChatRequestAssistantMessage(introMessage) };

        string prompt = null;
        while (true)
        {
            if (prompt == null)
            {
                Console.Write("> ");
                prompt = Console.ReadLine();
            }

            messageHistory.Add(new ChatRequestUserMessage(prompt));

            var request = new ChatCompletionsOptions(azureOpenAiDeploymentName, messageHistory)
            {
                Temperature = 0,
                MaxTokens = 400
            };
            
            foreach (var function in executionHelper.GetAvailableFunctions())
            {
                request.Tools.Add(new ChatCompletionsFunctionToolDefinition(function));
            }

            var completionResponse = await openAiClient.GetChatCompletionsStreamingAsync(request);

            AnsiConsole.Markup(":robot: ");
            
            var modelResponse = new StringBuilder();
            var functionNames = new Dictionary<int, string>();
            var functionArguments = new Dictionary<int, StringBuilder>();
            await foreach (var message in completionResponse)
            {
                if (message.ToolCallUpdate is StreamingFunctionToolCallUpdate functionToolCallUpdate)
                {
                    if (functionToolCallUpdate.Name != null)
                    {
                        functionNames[functionToolCallUpdate.ToolCallIndex] = functionToolCallUpdate.Name;
                    }
                    
                    if (functionToolCallUpdate.ArgumentsUpdate != null)
                    {
                        if (!functionArguments.TryGetValue(functionToolCallUpdate.ToolCallIndex, out var argumentsBuilder))
                        {
                            argumentsBuilder = new StringBuilder();
                            functionArguments[functionToolCallUpdate.ToolCallIndex] = argumentsBuilder;
                        }

                        argumentsBuilder.Append(functionToolCallUpdate.ArgumentsUpdate);
                    }
                }

                if (message.ContentUpdate != null)
                {
                    modelResponse.Append(message.ContentUpdate);
                    AnsiConsole.Write(message.ContentUpdate);
                }
            }

            var modelResponseText = modelResponse.ToString();
            if (!string.IsNullOrEmpty(modelResponseText))
            {
                messageHistory.Add(new ChatRequestAssistantMessage(modelResponseText));
            }

            // call the first tool that was found
            // in more sophisticated scenarios we may want to call multiple tools or let the user select one
            var functionCall = functionNames.FirstOrDefault().Value;
            var functionArgs = functionArguments.FirstOrDefault().Value?.ToString();
            if (functionCall != null && functionArgs != null)
            {
                AnsiConsole.WriteLine($"I'm calling a function called {functionCall} with arguments {functionArgs}... Stay tuned...");
                var functionResult = await executionHelper.InvokeFunction(functionCall, functionArgs);
                if (functionResult.BackToModel)
                {
                    prompt = functionResult.Output;
                    messageHistory.Add(new ChatRequestFunctionMessage(functionCall, modelResponseText));
                    continue;
                }

                if (functionResult.Output != null)
                {
                    Console.WriteLine(functionResult.Output);
                    prompt = null;
                    continue;
                }
            }

            prompt = null;
            Console.WriteLine();
        }
    }

    private class ExecutionHelper
    {
        private readonly ConcertApi _concertApi;

        public ExecutionHelper(ConcertApi concertApi)
        {
            _concertApi = concertApi;
        }

        public List<FunctionDefinition> GetAvailableFunctions()
            => new()
            {
                new FunctionDefinition
                {
                    Description =
                        "Searches for concerts by a specific band name and location. Returns a list of concerts, each one with its ID, date, band, location, ticket prices and currency.",
                    Name = nameof(ConcertApi.SearchConcerts),
                    Parameters = BinaryData.FromObjectAsJson(new
                    {
                        type = "object",
                        properties = new
                        {
                            band = new { type = "string" },
                            location = new
                            {
                                type = "string",
                                @enum = new[] { "Zurich", "Basel", "Toronto", "NewYork" }
                            },
                        },
                        required = new[] { "band", "location" }
                    })
                },
                new FunctionDefinition
                {
                    Description = "Books a concert ticket to a concert, using the concert's ID.",
                    Name = nameof(ConcertApi.BookTicket),
                    Parameters = BinaryData.FromObjectAsJson(new
                    {
                        type = "object",
                        properties = new { id = new { type = "integer" }, },
                        required = new[] { "id" }
                    })
                }
            };

        public async Task<ToolResult> InvokeFunction(string functionName, string functionArguments)
        {
            try
            {
                if (functionName == nameof(ConcertApi.SearchConcerts))
                {
                    var doc = JsonDocument.Parse(functionArguments);
                    var root = doc.RootElement;

                    var locationString = root.GetProperty("location").GetString();
                    var location = Enum.Parse<Location>(locationString);
                    var band = root.GetProperty("band").GetString();

                    var result = await _concertApi.SearchConcerts(band, location);
                    return new ToolResult(result, true);
                }

                if (functionName == nameof(ConcertApi.BookTicket))
                {
                    var doc = JsonDocument.Parse(functionArguments);
                    var root = doc.RootElement;

                    var id = root.GetProperty("id").GetUInt32();
                    await _concertApi.BookTicket(id);

                    return new ToolResult("Success!");
                }
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
                // in case of any error, send back to user in error state
            }

            return new ToolResult(null, IsError: true);
        }
    }
}

public enum Location
{
    Zurich,
    Basel,
    Toronto,
    NewYork
}

public class ConcertApi
{
    private List<Concert> Concerts = new()
    {
        new Concert(1, new DateTime(2024, 6, 11), "Iron Maiden", Location.Zurich, 150, "CHF"),
        new Concert(2, new DateTime(2024, 6, 12), "Iron Maiden", Location.Basel, 135, "CHF"),
        new Concert(3, new DateTime(2024, 8, 15), "Dropkick Murphys", Location.Toronto, 145, "CAD"),
        new Concert(4, new DateTime(2025, 1, 11), "Green Day", Location.NewYork, 200, "USD"),
    };

    public Task<string> SearchConcerts(string band, Location location)
    {
        var matches = Concerts.Where(c =>
            string.Equals(c.Band, band, StringComparison.InvariantCultureIgnoreCase) && c.Location == location).ToArray();
        return Task.FromResult(JsonSerializer.Serialize(matches));
    }

    public Task BookTicket(uint id)
    {
        if (!Concerts.Any(c => c.Id == id))
        {
            throw new Exception("No such concert!");
        }
        
        // assume when no exception is thrown, booking succeeds
        return Task.CompletedTask;
    }
}

public record Concert(uint Id, DateTime TimeStamp, string Band, Location Location, double Price, string Currency);

public record ToolResult(string Output, bool BackToModel = false, bool IsError = false);
