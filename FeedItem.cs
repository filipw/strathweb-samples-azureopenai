using System.Xml.Linq;

public class FeedItem
{
    public FeedItem(XElement item, XNamespace ns)
    {
        var title = item.Element(ns + "title")?.Value ?? "";
        Title = title[..title.LastIndexOf(". (arXiv:")];

        var link = item.Element(ns + "link")?.Value ?? "";
        Link = link;

        Id = link.Split("/").Last();
        Description = item.Element(ns + "description")?.Value ?? "";
        Creator = item.Element(ns + "creator")?.Value ?? "";
    }

    public string Id { get; set; }
    public int Rating { get; set; }
    public string Title { get; set; }
    public string Link { get; set; }
    public string Description { get; set; }
    public string Creator { get; set; }
}
