using System.Xml.Linq;

namespace Strathweb.Samples.AzureOpenAI.Shared;

public static class ArxivHelper
{
    public static async Task<Feed> FetchArticles(string searchQuery, string date, int maxResults = 40)
    {
        // cat:quant-ph
        // ti:"quantum computing"
        var feedUrl =
            $"https://export.arxiv.org/api/query?search_query={searchQuery}+AND+submittedDate:[{date}0000+TO+{date}2359]&start=0&max_results={maxResults}&sortBy=submittedDate&sortOrder=descending";
        var httpClient = new HttpClient();
        var httpResponse = await httpClient.GetAsync(feedUrl);

        Feed feed = null;
        if (httpResponse.IsSuccessStatusCode)
        {
            var ns = XNamespace.Get("http://www.w3.org/2005/Atom");
            var opensearch = XNamespace.Get("http://a9.com/-/spec/opensearch/1.1/");
            var arxiv = XNamespace.Get("http://arxiv.org/schemas/atom");

            var xmlContent = await httpResponse.Content.ReadAsStringAsync();
            var xDoc = XDocument.Parse(xmlContent);
            var feedElement = xDoc.Element(ns + "feed");

            feed = new Feed
            {
                Title = (string)feedElement.Element(ns + "title"),
                Id = ((string)feedElement.Element(ns + "id")).Replace("http://arxiv.org/abs/", ""),
                Updated = (DateTime)feedElement.Element(ns + "updated"),
                TotalResults = (int)feedElement.Element(opensearch + "totalResults"),
                StartIndex = (int)feedElement.Element(opensearch + "startIndex"),
                ItemsPerPage = (int)feedElement.Element(opensearch + "itemsPerPage"),
                Entries = feedElement.Elements(ns + "entry").Select(entryElement => new Entry
                {
                    Id = ((string)entryElement.Element(ns + "id")).Split('/').Last(),
                    Updated = (DateTime)entryElement.Element(ns + "updated"),
                    Published = (DateTime)entryElement.Element(ns + "published"),
                    Title = (string)entryElement.Element(ns + "title"),
                    Summary = (string)entryElement.Element(ns + "summary"),
                    Authors = entryElement.Elements(ns + "author").Select(authorElement => new Author
                    {
                        Name = (string)authorElement.Element(ns + "name")
                    }).ToList(),
                    PdfLink = entryElement.Elements(ns + "link")
                        .FirstOrDefault(link => (string)link.Attribute("title") == "pdf")?.Attribute("href")?.Value,
                    PrimaryCategory =
                        (string)entryElement.Element(arxiv + "primary_category")?.Attribute("term")?.Value,
                    Categories = entryElement.Elements(ns + "category")
                        .Select(category => (string)category.Attribute("term")).ToList()
                }).ToList()
            };
        }

        return feed;
    }
    
    public static async Task<Feed> FetchArticleById(string id)
    {
        var feedUrl = $"https://export.arxiv.org/api/query?id_list={id}";
        var httpClient = new HttpClient();
        var httpResponse = await httpClient.GetAsync(feedUrl);

        Feed feed = null;
        if (httpResponse.IsSuccessStatusCode)
        {
            var ns = XNamespace.Get("http://www.w3.org/2005/Atom");
            var opensearch = XNamespace.Get("http://a9.com/-/spec/opensearch/1.1/");
            var arxiv = XNamespace.Get("http://arxiv.org/schemas/atom");

            var xmlContent = await httpResponse.Content.ReadAsStringAsync();
            var xDoc = XDocument.Parse(xmlContent);
            var feedElement = xDoc.Element(ns + "feed");

            feed = new Feed
            {
                Title = (string)feedElement.Element(ns + "title"),
                Id = ((string)feedElement.Element(ns + "id")),
                Updated = (DateTime)feedElement.Element(ns + "updated"),
                TotalResults = (int)feedElement.Element(opensearch + "totalResults"),
                StartIndex = (int)feedElement.Element(opensearch + "startIndex"),
                ItemsPerPage = (int)feedElement.Element(opensearch + "itemsPerPage"),
                Entries = feedElement.Elements(ns + "entry").Select(entryElement => new Entry
                {
                    Id = ((string)entryElement.Element(ns + "id")).Split('/').Last(),
                    Updated = (DateTime)entryElement.Element(ns + "updated"),
                    Published = (DateTime)entryElement.Element(ns + "published"),
                    Title = (string)entryElement.Element(ns + "title"),
                    Summary = (string)entryElement.Element(ns + "summary"),
                    Authors = entryElement.Elements(ns + "author").Select(authorElement => new Author
                    {
                        Name = (string)authorElement.Element(ns + "name")
                    }).ToList(),
                    PdfLink = entryElement.Elements(ns + "link")
                        .FirstOrDefault(link => (string)link.Attribute("title") == "pdf")?.Attribute("href")?.Value,
                    PrimaryCategory =
                        (string)entryElement.Element(arxiv + "primary_category")?.Attribute("term")?.Value,
                    Categories = entryElement.Elements(ns + "category")
                        .Select(category => (string)category.Attribute("term")).ToList()
                }).ToList()
            };
        }

        return feed;
    }
}