using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Assistants;
using Strathweb.Samples.AzureOpenAI.AssistantFunctionCalling;

var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT") ??
                                 throw new Exception("AZURE_OPENAI_SERVICE_ENDPOINT missing");

var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ??
                            throw new Exception("AZURE_OPENAI_API_KEY missing");

var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ??
                                throw new Exception("AZURE_OPENAI_DEPLOYMENT_NAME missing");

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

var openAiClient = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint),
    new AzureKeyCredential(azureOpenAiServiceKey));
var client = new AssistantsClient(new Uri(azureOpenAiServiceEndpoint), new AzureKeyCredential(azureOpenAiServiceKey));

var arxivClient = new ArxivClient(openAiClient, azureOpenAiDeploymentName);
var executionHelper = new ExecutionHelper(arxivClient);

var assistantCreationOptions = new AssistantCreationOptions(azureOpenAiDeploymentName)
{
    Name = "Arxiv Helper Assistant", Instructions = systemInstructions
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
