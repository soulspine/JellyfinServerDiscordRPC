using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DiscordRPC.Utility;

public static class IMDbScraper
{
    public class MovieMetadata
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Year { get; set; }
        public string? Rating { get; set; }
        public string? PosterUrl { get; set; }

        public override string ToString()
        {
            return $"Title: {Title ?? "N/A"} ({Year ?? "N/A"}) | Rating: {Rating ?? "N/A"} | Poster: {PosterUrl ?? "N/A"}";
        }
    }

    public static async Task<MovieMetadata?> GetImdbMetadata(string? imdbId, string languageCode = "en")
    {
        if (string.IsNullOrEmpty(imdbId)) return null;

        try
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                client.DefaultRequestHeaders.Add("Accept-Language", $"{languageCode}; q = 0.9");

                var url = $"https://www.imdb.com/title/{imdbId}/";
                var html = await client.GetStringAsync(url);

                var metadata = new MovieMetadata();

                var titleMatch = Regex.Match(html, @"<meta[^>]+property=""og:title""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
                if (titleMatch.Success)
                {
                    string fullRaw = titleMatch.Groups[1].Value.Replace(" - IMDb", "").Trim();

                    if (fullRaw.Contains('|'))
                    {
                        string titleAndInfo = fullRaw.Split('|')[0].Trim();

                        // 1. Wyciągamy ocenę
                        var ratingMatch = Regex.Match(titleAndInfo, @"(\d\.\d|\d)$");
                        if (ratingMatch.Success)
                        {
                            metadata.Rating = ratingMatch.Value;
                            titleAndInfo = titleAndInfo.Remove(ratingMatch.Index).Trim();
                        }

                        // 2. Wyciągamy rok
                        var yearMatch = Regex.Match(titleAndInfo, @"\((\d{4})\)");
                        if (yearMatch.Success)
                        {
                            metadata.Year = yearMatch.Groups[1].Value;
                            titleAndInfo = titleAndInfo.Replace(yearMatch.Value, "").Trim();
                        }

                        // 3. Usuwamy gwiazdkę i inne symbole (czyścimy wszystko co nie jest literą/cyfrą na końcu)
                        // Ten Regex usuwa wszelkie znaki specjalne, które zostały po wycięciu oceny
                        titleAndInfo = Regex.Replace(titleAndInfo, @"[^\w\s\d\-',.!].*$", "").Trim();

                        // 4. Naprawiamy podwójne spacje
                        metadata.Title = Regex.Replace(titleAndInfo, @"\s+", " ");
                    }
                }
                var imageMatch = Regex.Match(html, @"<meta[^>]+property=""og:image""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
                if (imageMatch.Success)
                {
                    metadata.PosterUrl = imageMatch.Groups[1].Value;
                }

                metadata.Id = imdbId;
                return metadata;
            }
        }
        catch
        {
            return null;
        }
    }
}