using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Spectre.Console;

namespace Strathweb.Samples.AzureOpenAI.BringYourOwnData;

static partial class Demo
{
    public static async Task RunWithSdk(AzureOpenAiContext context, JsonSerializerOptions options)
    {
        var openAiClient = new OpenAIClient(new Uri(context.AzureOpenAiServiceEndpoint),
            new AzureKeyCredential(context.AzureOpenAiServiceKey));

        var searchExtension = new AzureCognitiveSearchChatExtensionConfiguration
        {
            ShouldRestrictResultScope = context.RestrictToSearchResults,
            SearchEndpoint = new Uri($"https://{context.AzureSearchService}.search.windows.net"),
            IndexName = context.AzureSearchIndex,
            DocumentCount = (int)context.SearchDocumentCount,
            QueryType = new AzureCognitiveSearchQueryType(context.AzureSearchQueryType),
            SemanticConfiguration = context.AzureSearchQueryType is "semantic" or "vectorSemanticHybrid"
                ? context.AzureSearchSemanticSearchConfig
                : "",
            FieldMappingOptions = new AzureCognitiveSearchIndexFieldMappingOptions
            {
                ContentFieldNames = { "content" },
                UrlFieldName = "blog_url",
                TitleFieldName = "metadata_storage_name",
                FilepathFieldName = "metadata_storage_path"
            }
        };
        
        searchExtension.SetSearchKey(context.AzureSearchKey);
        
        while (true)
        {
            var prompt = Console.ReadLine();
            var request = new ChatCompletionsOptions(context.AzureOpenAiDeploymentName, new[] {
                new ChatMessage(ChatRole.System, context.SystemInstructions),
                new ChatMessage(ChatRole.Assistant, context.InitialAssistantMessage),
                new ChatMessage(ChatRole.User, prompt)
            })
            {
                Temperature = 1,
                MaxTokens = 400,
                NucleusSamplingFactor = 1f,
                AzureExtensionsOptions = new AzureChatExtensionsOptions
                {
                    Extensions = { searchExtension }
                }
            };

            var completionResponse = await openAiClient.GetChatCompletionsStreamingAsync(request);

            OpenAICitationResponse citationResponse = null;
            await foreach (var message in completionResponse)
            {
                //await foreach (var message in choice.())
                {
                    if (message.AzureExtensionsContext != null)
                    {
                        var extensionMessage = message.AzureExtensionsContext.Messages.FirstOrDefault();
                        if (extensionMessage != null && !string.IsNullOrWhiteSpace(extensionMessage.Content))
                        {
                            Console.WriteLine(extensionMessage.Content);
                            citationResponse =
                                JsonSerializer.Deserialize<OpenAICitationResponse>(extensionMessage.Content, options);
                        }
                    }
                    else
                    {
                        Console.Write(message.ContentUpdate);
                    }
                }
            }


            if (citationResponse != null && citationResponse.Citations.Any())
            {
                Console.WriteLine();
                var referencesContent = new StringBuilder();
                referencesContent.AppendLine();

                for (var i = 1; i <= citationResponse.Citations.Length; i++)
                {
                    var citation = citationResponse.Citations[i - 1];
                    referencesContent.AppendLine($"  :page_facing_up: [[doc{i}]] {citation.Title}");
                    referencesContent.AppendLine($"  :link: {citation.Url ?? citation.FilePath}");
                }

                var panel = new Panel(referencesContent.ToString())
                {
                    Header = new PanelHeader("References")
                };
                AnsiConsole.Write(panel);
            }
        }
    }
}