using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace RetroJukebox.Services;

public record MusicBrainzResult(
    string RecordingId,
    string Title,
    string Artist,
    string Album,
    string ReleaseId,
    int Year,
    int TrackNumber,
    int Score
);

public class MusicBrainzService
{
    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect      = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                   | System.Net.DecompressionMethods.Deflate,
            // Bypass SSL cert validation — handles corporate firewalls / SSL inspection proxies
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        var client = new HttpClient(handler);
        // MusicBrainz strictly requires a proper User-Agent or returns 403
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroJukebox/1.0");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    // ── Search ────────────────────────────────────────────────────────────
    public async Task<(List<MusicBrainzResult> Results, string? Error)> SearchAsync(
        string title, string artist, int maxResults = 12)
    {
        try
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(title))
                parts.Add($"recording:{QuoteLucene(title)}");
            if (!string.IsNullOrWhiteSpace(artist))
                parts.Add($"artist:{QuoteLucene(artist)}");

            if (parts.Count == 0)
                return ([], "Please enter a title or artist.");

            var query = string.Join(" AND ", parts);
            var url   = $"https://musicbrainz.org/ws/2/recording"
                      + $"?query={Uri.EscapeDataString(query)}"
                      + $"&limit={maxResults}&fmt=json";

            System.Diagnostics.Debug.WriteLine($"[MB] GET {url}");

            var response = await _http.GetAsync(url);
            var body     = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"[MB] Status={response.StatusCode} Body={body[..Math.Min(200, body.Length)]}");

            if (!response.IsSuccessStatusCode)
                return ([], $"MusicBrainz returned {(int)response.StatusCode}: {response.ReasonPhrase}");

            var obj        = JObject.Parse(body);
            var recordings = obj["recordings"] as JArray;
            if (recordings == null || recordings.Count == 0)
                return ([], null); // success but no results

            var results = new List<MusicBrainzResult>();
            foreach (var rec in recordings)
            {
                var recId    = rec["id"]?.ToString() ?? "";
                var recTitle = rec["title"]?.ToString() ?? "";
                var score    = rec["score"]?.Value<int>() ?? 0;

                var artistCredit = rec["artist-credit"] as JArray;
                var recArtist    = artistCredit?.FirstOrDefault()?["artist"]?["name"]?.ToString()
                                ?? artistCredit?.FirstOrDefault()?["name"]?.ToString()
                                ?? "";

                var releases  = rec["releases"] as JArray;
                var release   = releases?.FirstOrDefault();
                var releaseId = release?["id"]?.ToString() ?? "";
                var album     = release?["title"]?.ToString() ?? "";
                var dateStr   = release?["date"]?.ToString() ?? "";
                int.TryParse(dateStr.Length >= 4 ? dateStr[..4] : dateStr, out var year);

                var media    = release?["media"] as JArray;
                var mediaObj = media?.FirstOrDefault();
                var tracks   = mediaObj?["track"] as JArray;
                var trackObj = tracks?.FirstOrDefault();
                int.TryParse(trackObj?["number"]?.ToString(), out var trackNum);

                results.Add(new MusicBrainzResult(
                    recId, recTitle, recArtist, album,
                    releaseId, year, trackNum, score));
            }

            return (results, null);
        }
        catch (TaskCanceledException)
        {
            return ([], "Request timed out. Check your internet connection.");
        }
        catch (HttpRequestException ex)
        {
            return ([], $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MB] Exception: {ex}");
            return ([], $"Error: {ex.Message}");
        }
    }

    // ── Cover Art ─────────────────────────────────────────────────────────
    public async Task<(byte[]? Art, string? Error)> GetCoverArtAsync(string releaseId)
    {
        if (string.IsNullOrEmpty(releaseId))
            return (null, "No release ID.");
        try
        {
            var url      = $"https://coverartarchive.org/release/{releaseId}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return (null, $"No art found ({(int)response.StatusCode}).");

            var body   = await response.Content.ReadAsStringAsync();
            var obj    = JObject.Parse(body);
            var images = obj["images"] as JArray;
            if (images == null || images.Count == 0)
                return (null, "No images in release.");

            var front  = images.FirstOrDefault(i => i["front"]?.Value<bool>() == true)
                      ?? images.First();

            var imgUrl = front["thumbnails"]?["500"]?.ToString()
                      ?? front["thumbnails"]?["large"]?.ToString()
                      ?? front["image"]?.ToString();

            if (string.IsNullOrEmpty(imgUrl))
                return (null, "No image URL found.");

            var bytes = await _http.GetByteArrayAsync(imgUrl);
            return (bytes, null);
        }
        catch (Exception ex)
        {
            return (null, $"Art fetch failed: {ex.Message}");
        }
    }

    // ── Genre from release ────────────────────────────────────────────────
    public async Task<string?> GetGenreAsync(string releaseId)
    {
        if (string.IsNullOrEmpty(releaseId)) return null;
        try
        {
            var url  = $"https://musicbrainz.org/ws/2/release/{releaseId}?inc=genres+tags&fmt=json";
            var json = await _http.GetStringAsync(url);
            var obj  = JObject.Parse(json);

            var genre = (obj["genres"] as JArray)?.FirstOrDefault()?["name"]?.ToString()
                     ?? (obj["tags"]   as JArray)?.FirstOrDefault()?["name"]?.ToString();

            return genre is null ? null
                : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(genre);
        }
        catch { return null; }
    }

    private static string QuoteLucene(string s)
    {
        // Escape special Lucene chars then wrap in quotes
        s = s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{s}\"";
    }
}
