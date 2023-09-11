using Azure;
using Azure.AI.OpenAI;

var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT");
var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");

var client = new OpenAIClient(new Uri(azureOpenAiServiceEndpoint), new AzureKeyCredential(azureOpenAiServiceKey));
var embedding = client.GetEmbeddingsAsync(azureOpenAiDeploymentName, new EmbeddingsOptions("foo"));
Console.WriteLine("hello");