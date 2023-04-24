public class Feed
{
    public string Title { get; set; }
    public string Id { get; set; }
    public DateTime Updated { get; set; }
    public int TotalResults { get; set; }
    public int StartIndex { get; set; }
    public int ItemsPerPage { get; set; }
    public List<Entry> Entries { get; set; }
}
