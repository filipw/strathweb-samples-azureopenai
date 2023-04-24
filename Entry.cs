public class Entry
{
    public string Id { get; set; }
    public int Rating { get; set; }
    public DateTime Updated { get; set; }
    public DateTime Published { get; set; }
    public string Title { get; set; }
    public string Summary { get; set; }
    public List<Author> Authors { get; set; }
    public string PdfLink { get; set; }
    public string PrimaryCategory { get; set; }
    public List<string> Categories { get; set; }
}
