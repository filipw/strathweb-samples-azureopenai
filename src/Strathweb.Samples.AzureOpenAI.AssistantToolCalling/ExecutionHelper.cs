// Copyright (c) Microsoft Corporation. All rights reserved.
// ExecutionHelper.cs

using System.Text.Json;
using Azure;
using Azure.AI.OpenAI.Assistants;

namespace Strathweb.Samples.AzureOpenAI.AssistantToolCalling;

public class ExecutionHelper
{
    private readonly ArxivClient _arxivClient;

    public ExecutionHelper(ArxivClient arxivClient)
    {
        _arxivClient = arxivClient;
    }
    
    public async Task<ToolOutput> InvokeTool(RequiredFunctionToolCall requiredFunctionToolCall)
    {
        if (requiredFunctionToolCall.Name == "FetchPapers")
        {
            var doc = JsonDocument.Parse(requiredFunctionToolCall.Arguments);
            var root = doc.RootElement;

            var searchQueryString = root.GetProperty("searchQuery").GetString();
            var searchQuery = Enum.Parse<SearchQuery>(searchQueryString);
            var date = root.GetProperty("date").GetDateTime();

            var feed = await _arxivClient.FetchPapers(searchQuery, date);
            return new ToolOutput(requiredFunctionToolCall, feed);
        }

        if (requiredFunctionToolCall.Name == "SummarizePaper")
        {
            var doc = JsonDocument.Parse(requiredFunctionToolCall.Arguments);
            var root = doc.RootElement;

            var paperId = root.GetProperty("paperId").GetString();
            var summary = await _arxivClient.SummarizePaper(paperId);

            return new ToolOutput(requiredFunctionToolCall, summary);
        }

        return null;
    }
    
    public List<ToolDefinition> GetAvailableFunctions() 
        => new()
        {
            new FunctionToolDefinition("FetchPapers",
                "Fetches papers from ArXiv for a given date (required). The searchQuery parameter is mandatory and controls whether the papers are quantum physics or quantum computing.",
                BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        searchQuery =
                            new { type = "string", @enum = new[] { "QuantumPhysics", "QuantumComputing" } },
                        date = new { type = "string", format = "date" }
                    },
                    required = new[] { "searchQuery", "date" }
                })),
            new FunctionToolDefinition("SummarizePaper",
                "Summarizes a given paper based on the ArXiv ID of the paper.",
                BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new { paperId = new { type = "string" }, },
                    required = new[] { "paperId" }
                }))
        };

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
