public record AzureOpenAiContext
{
    public string AzureOpenAiServiceEndpoint { get; init; }
    
    public string AzureOpenAiServiceKey { get; init; }
    
    public string AzureOpenAiDeploymentName { get; init; }
    
    public string AzureSearchService { get; init; }
    
    public string AzureSearchKey { get; init; }
    
    public string AzureSearchIndex { get; init; }
    
    public bool RestrictToSearchResults { get; init; }
    
    public uint SearchDocumentCount { get; init; }
    
    public string AzureSearchQueryType { get; init; }
    
    public string AzureSearchSemanticSearchConfig { get; init; }
    
    public string SystemInstructions { get; init; }
    
    public string InitialAssistantMessage { get; init; }
}