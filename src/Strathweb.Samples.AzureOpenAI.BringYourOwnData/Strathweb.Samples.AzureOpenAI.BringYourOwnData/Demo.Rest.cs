using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace Strathweb.Samples.AzureOpenAI.BringYourOwnData;

static partial class Demo
{
    public static async Task RunWithRestApi(AzureOpenAiContext context, JsonSerializerOptions options)
    {
        var client = new HttpClient();

        while (true)
        {
            var prompt = Console.ReadLine();
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"{context.AzureOpenAiServiceEndpoint}openai/deployments/{context.AzureOpenAiDeploymentName}/extensions/chat/completions?api-version=2023-08-01-preview");

            var body = new OpenAIRequest
            {
                Temperature = 1,
                MaxTokens = 400,
                TopP = 1,
                DataSources = new[]
                {
                    new DataSource
                    {
                        Type = "AzureCognitiveSearch",
                        Parameters = new DataSourceParameters()
                        {
                            Endpoint = $"https://{context.AzureSearchService}.search.windows.net",
                            Key = context.AzureSearchKey,
                            IndexName = context.AzureSearchIndex,
                            FieldsMapping = new DataSourceFieldsMapping
                            {
                                ContentFields = new[] { "content" },
                                UrlField = "blog_url",
                                TitleField = "metadata_storage_name",
                                FilepathField = "metadata_storage_path"
                            },
                            InScope = context.RestrictToSearchResults,
                            TopNDocuments = context.SearchDocumentCount,
                            QueryType = context.AzureSearchQueryType,
                            SemanticConfiguration = context.AzureSearchQueryType is "semantic" or "vectorSemanticHybrid"
                                ? context.AzureSearchSemanticSearchConfig
                                : "",
                            RoleInformation = context.SystemInstructions
                        }
                    }
                },
                Messages = new[]
                {
                    new OpenAIMessage
                    {
                        Role = "system",
                        Content = context.SystemInstructions
                    },
                    new OpenAIMessage()
                    {
                        Role = "assistant",
                        Content = context.InitialAssistantMessage
                    },
                    new OpenAIMessage
                    {
                        Role = "user",
                        Content = prompt
                    }
                }
            };

            var rawRequest = JsonSerializer.Serialize(body, options);
            req.Content = new StringContent(rawRequest);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Headers.Add("api-key", context.AzureOpenAiServiceKey);

            var completionsResponseMessage = await client.SendAsync(req);
            var rawResponse = await completionsResponseMessage.Content.ReadAsStringAsync();
            if (!completionsResponseMessage.IsSuccessStatusCode)
            {
                AnsiConsole.Markup(":robot: ");
                Console.Write("Unfortunately, there was an error retrieving the response!");
                Console.WriteLine(rawResponse);
                return;
            }

            var response = JsonSerializer.Deserialize<OpenAIResponse>(rawResponse, options);
            var responseMessage = response?.Choices?.FirstOrDefault(x => x.Message.Role == "assistant")?.Message;

            AnsiConsole.Markup(":robot: ");
            Console.Write(responseMessage?.Content);
            Console.Write(Environment.NewLine);

            var toolRawResponse = responseMessage?.Context?.Messages?.FirstOrDefault(x => x.Role == "tool")?.Content;
            if (toolRawResponse != null)
            {
                var toolResponse = JsonSerializer.Deserialize<OpenAICitationResponse>(toolRawResponse, options);
                if (toolResponse != null && toolResponse.Citations.Any())
                {
                    var referencesContent = new StringBuilder();
                    referencesContent.AppendLine();

                    for (var i = 1; i <= toolResponse.Citations.Length; i++)
                    {
                        var citation = toolResponse.Citations[i - 1];
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
}