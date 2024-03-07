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

        while (true)
        {
            var prompt = Console.ReadLine();
            var request = new ChatCompletionsOptions
            {
                DeploymentName = context.AzureOpenAiDeploymentName,
                Messages = {
                    new ChatRequestSystemMessage(context.SystemInstructions),
                    new ChatRequestUserMessage(prompt)
                },
                Temperature = 1,
                MaxTokens = 400,
                NucleusSamplingFactor = 1f,
                AzureExtensionsOptions = new AzureChatExtensionsOptions
                {
                    Extensions = 
                    { 
                        new AzureSearchChatExtensionConfiguration()
                        {
                            ShouldRestrictResultScope = context.RestrictToSearchResults,
                            SearchEndpoint = new Uri($"https://{context.AzureSearchService}.search.windows.net"),
                            Authentication = new OnYourDataApiKeyAuthenticationOptions(context.AzureSearchKey),
                            IndexName = context.AzureSearchIndex,
                            DocumentCount = (int)context.SearchDocumentCount,
                            QueryType = new AzureSearchQueryType(context.AzureSearchQueryType),
                            SemanticConfiguration = context.AzureSearchQueryType is "semantic" or "vectorSemanticHybrid"
                                ? context.AzureSearchSemanticSearchConfig
                                : "",
                            FieldMappingOptions = new AzureSearchIndexFieldMappingOptions()
                            {
                                ContentFieldNames = { "content" },
                                UrlFieldName = "blog_url",
                                TitleFieldName = "metadata_storage_name",
                                FilepathFieldName = "metadata_storage_path"
                            },
                            RoleInformation = context.SystemInstructions,
                            VectorizationSource = context.AzureSearchQueryType is "vector" ? new OnYourDataEndpointVectorizationSource(new Uri(context.EmbeddingEndpoint), new OnYourDataApiKeyAuthenticationOptions(context.AzureOpenAiServiceKey)) : null
                        } 
                    }
                }
            };

            var completionResponse = await openAiClient.GetChatCompletionsStreamingAsync(request);

            AnsiConsole.Markup(":robot: ");
            var citationsResponses = new List<AzureChatExtensionDataSourceResponseCitation>();
            await foreach (var message in completionResponse)
            {
                Console.Write(message.ContentUpdate);

                if (message.AzureExtensionsContext != null)
                {
                    citationsResponses.AddRange(message.AzureExtensionsContext.Citations);
                }
            }


            if (citationsResponses.Any())
            {
                Console.WriteLine();
                var referencesContent = new StringBuilder();
                referencesContent.AppendLine();
                
                for (var i = 1; i <= citationsResponses.Count; i++)
                {
                    var citation = citationsResponses[i - 1];
                    referencesContent.AppendLine($"  :page_facing_up: [[doc{i}]] {citation.Title}");
                    referencesContent.AppendLine($"  :link: {citation.Url ?? citation.Filepath}");
                }

                var panel = new Panel(referencesContent.ToString())
                {
                    Header = new PanelHeader("References")
                };
                AnsiConsole.Write(panel);
            }
            else
            {
                Console.WriteLine();
            }
        }
    }
}