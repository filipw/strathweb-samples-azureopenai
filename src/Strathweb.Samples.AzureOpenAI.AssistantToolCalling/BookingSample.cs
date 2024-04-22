// Copyright (c) Microsoft Corporation. All rights reserved.
// BookingSample.cs

using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Assistants;
using Spectre.Console;

namespace Strathweb.Samples.AzureOpenAI.AssistantToolCalling;

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

        var openAiClient = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint),
            new AzureKeyCredential(azureOpenAiServiceKey));
        var client = new AssistantsClient(new Uri(azureOpenAiServiceEndpoint),
            new AzureKeyCredential(azureOpenAiServiceKey));

        var arxivClient = new ConcertApi();
        var executionHelper = new ExecutionHelper(arxivClient);

        var assistantCreationOptions = new AssistantCreationOptions(azureOpenAiDeploymentName)
        {
            Name = "Concert Booking Assistant", Instructions = systemInstructions
        };
        
        foreach (var tool in executionHelper.GetAvailableFunctions())
        {
            assistantCreationOptions.Tools.Add(tool);
        }
        
        var assistantResponse = await client.CreateAssistantAsync(assistantCreationOptions);

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

            while (executionHelper.IsRunPending(runResponse))
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                runResponse = await client.GetRunAsync(thread.Id, runResponse.Value.Id);

                if (executionHelper.RequiresAction(runResponse, out var submitToolOutputsAction))
                {
                    var toolOutputs = await executionHelper.ProcessToolCalls(submitToolOutputsAction);
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
    }

    private class ExecutionHelper
    {
        private readonly ConcertApi _concertApi;

        public ExecutionHelper(ConcertApi concertApi)
        {
            _concertApi = concertApi;
        }
        
        public List<ToolDefinition> GetAvailableFunctions()
            => new()
            {
                new FunctionToolDefinition(nameof(ConcertApi.SearchConcerts),
                    "Searches for concerts by a specific band name and location. Returns a list of concerts, each one with its ID, date, band, location, ticket prices and currency.",
                    BinaryData.FromObjectAsJson(new { type = "object", properties = new { band = new { type = "string" }, location = new { type = "string", @enum = new[] { "Zurich", "Basel", "Toronto", "NewYork" } }, }, required = new[] { "band", "location" } })),
                new FunctionToolDefinition(nameof(ConcertApi.BookTicket),
                    "Books a concert ticket to a concert, using the concert's ID.",
                    BinaryData.FromObjectAsJson(new
                    {
                        type = "object",
                        properties = new { id = new { type = "integer" }, },
                        required = new[] { "id" }
                    })
                )
            };

        public async Task<ToolOutput> InvokeTool(RequiredFunctionToolCall requiredFunctionToolCall)
        {
            if (requiredFunctionToolCall.Name == nameof(ConcertApi.SearchConcerts))
            {
                var doc = JsonDocument.Parse(requiredFunctionToolCall.Arguments);
                var root = doc.RootElement;

                var locationString = root.GetProperty("location").GetString();
                var location = Enum.Parse<Location>(locationString);
                var band = root.GetProperty("band").GetString();

                var result = await _concertApi.SearchConcerts(band, location);
                return new ToolOutput(requiredFunctionToolCall, result);
            }

            if (requiredFunctionToolCall.Name == nameof(ConcertApi.BookTicket))
            {
                var doc = JsonDocument.Parse(requiredFunctionToolCall.Arguments);
                var root = doc.RootElement;

                var id = root.GetProperty("id").GetUInt32();
                await _concertApi.BookTicket(id);

                return new ToolOutput(requiredFunctionToolCall, "Success!");
            }

            return null;
        }

        public bool IsRunPending(Response<ThreadRun> response)
            => response.Value.Status == RunStatus.Queued || response.Value.Status == RunStatus.InProgress;

        public bool RequiresAction(Response<ThreadRun> response, out SubmitToolOutputsAction submitToolOutputsAction)
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

        public async Task<List<ToolOutput>> ProcessToolCalls(SubmitToolOutputsAction submitToolOutputsAction)
        {
            var toolOutputs = new List<ToolOutput>();
            foreach (var toolCall in submitToolOutputsAction.ToolCalls)
            {
                if (toolCall is RequiredFunctionToolCall requiredFunctionToolCall)
                {
                    var toolOutput = await InvokeTool(requiredFunctionToolCall);
                    toolOutputs.Add(toolOutput);
                }
            }

            return toolOutputs;
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
