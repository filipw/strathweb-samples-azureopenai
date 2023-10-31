using System.Text.Json.Serialization;

record OpenAIResponse
{
    public string Id { get; set; }
    public string Model { get; set; }
    public int Created { get; set; }
    public string Object { get; set; }
    public ResponseChoice[] Choices { get; set; }
}

record ResponseChoice
{
    public int Index { get; set; }
    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; }
    public ResponseMessage Message { get; set; }
}

record ResponseMessage
{
    public int Index { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    [JsonPropertyName("end_turn")]
    public bool EndTurn { get; set; }
    public ResponseContext Context { get; set; }
}

record ResponseContext
{
    public ResponseMessage[] Messages { get; set; }
}

record OpenAICitationResponse
{
    public Citation[] Citations { get; set; }
    public string Intent { get; set; }
}

record Citation
{
    public string Content { get; set; }
    public string Id { get; set; }
    public string Title { get; set; }
    public string FilePath { get; set; }
    public string Url { get; set; }
    public CitationMetadata Metadata { get; set; }
}

record CitationMetadata
{
    public string Chunking { get; set; }
    
    [JsonPropertyName("chunk_id")]
    public string ChunkId { get; set; }
}

record OpenAIRequest
{
    public float Temperature { get; set; }
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }
    [JsonPropertyName("top_p")]
    public float TopP { get; set; }
    public OpenAIMessage[] Messages { get; set; }
    public DataSource[] DataSources { get; set; }
}

record DataSource
{
    public string Type { get; set; }
    public DataSourceParameters Parameters { get; set; }
}

record DataSourceParameters
{
    public string Endpoint { get; set; }
    public string Key { get; set; }
    public string IndexName { get; set; }
    public bool InScope { get; set; }
    public uint TopNDocuments { get; set; }
    public string QueryType { get; set; }
    public string SemanticConfiguration { get; set; }
    public string RoleInformation { get; set; }
    public string EmbeddingEndpoint { get; set; }
    public string EmbeddingKey { get; set; }
    public DataSourceFieldsMapping FieldsMapping { get; set; }
}

record DataSourceFieldsMapping
{
    public string[] VectorFields { get; set; }
    public string[] ContentFields { get; set; }
    public string FilepathField { get; set; }
    public string UrlField { get; set; }
    public string TitleField { get; set; }
}

record OpenAIMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}