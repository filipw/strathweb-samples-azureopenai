namespace Strathweb.Samples.AzureOpenAI.BringYourOwnData;

public record AzureOpenAiContext
{
    /// <summary>
    /// Defines the Azure OpenAI Service instance URL
    /// in the format https://{name}.openai.azure.com/
    /// </summary>
    public string AzureOpenAiServiceEndpoint { get; init; }
    
    /// <summary>
    /// The key to access the Azure OpenAI Service 
    /// </summary>
    public string AzureOpenAiServiceKey { get; init; }
    
    /// <summary>
    /// The name of the deployed OpenAI model, available
    /// within the Azure OpenAI Service instance
    /// </summary>
    public string AzureOpenAiDeploymentName { get; init; }
    
    /// <summary>
    /// The name of the Azure Cognitive Search (just name not full URL)
    /// </summary>
    public string AzureSearchService { get; init; }
    
    /// <summary>
    /// The key to access the Azure Cognitive Search
    /// </summary>
    public string AzureSearchKey { get; init; }
    
    /// <summary>
    /// Search index inside the Azure Cognitive Search
    /// which can be used to query for data
    /// </summary>
    public string AzureSearchIndex { get; init; }
    
    /// <summary>
    /// Defines if the model should be engaged if the
    /// search query produces 0 results
    /// </summary>
    public bool RestrictToSearchResults { get; init; }
    
    /// <summary>
    /// How many search results should be fed into the model prompt?
    /// </summary>
    public uint SearchDocumentCount { get; init; }
    
    /// <summary>
    /// Type of query to execute. Can be "simple", "semantic", "vector",
    /// "vectorSimpleHybrid" or "vectorSemanticHybrid".
    /// When choosing vectors, vectorized fields are needed in the index
    /// </summary>
    public string AzureSearchQueryType { get; init; }
    
    /// <summary>
    /// Semantic configuration for the search, if semantic search is used
    /// </summary>
    public string AzureSearchSemanticSearchConfig { get; init; }
    
    /// <summary>
    /// System-level prompt instructions for the OpenAI model
    /// </summary>
    public string SystemInstructions { get; init; }
    
    /// <summary>
    /// The first message that is shown to the user, inviting the user
    /// to engage in a conversation
    /// </summary>
    public string InitialAssistantMessage { get; init; }
}