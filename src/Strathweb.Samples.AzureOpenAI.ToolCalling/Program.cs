using Spectre.Console;
using Strathweb.Samples.AzureOpenAI.ToolCalling;

var azureOpenAiServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_SERVICE_ENDPOINT") ??
                                 throw new Exception("AZURE_OPENAI_SERVICE_ENDPOINT missing");

var azureOpenAiServiceKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ??
                            throw new Exception("AZURE_OPENAI_API_KEY missing");

var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ??
                                throw new Exception("AZURE_OPENAI_DEPLOYMENT_NAME missing");

var demo = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("Choose the [green]example[/] to run?")
        .AddChoices("Arxiv Assistant", "Concert Booking"));

switch (demo)
{
    case "Arxiv Assistant":
        await ArxivSample.Run(azureOpenAiServiceEndpoint, azureOpenAiServiceKey, azureOpenAiDeploymentName);
        break;
    case "Concert Booking":
        await BookingSample.Run(azureOpenAiServiceEndpoint, azureOpenAiServiceKey, azureOpenAiDeploymentName);
        break;
    default:
        Console.WriteLine("Nothing selected!");
        Environment.Exit(0);
        break;
}
