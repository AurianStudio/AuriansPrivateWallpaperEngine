using Lively.Models.Gallery.API;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Lively.Gallery.Client
{
    public class VSthemesGalleryService
    {
        private const string BaseUrl = "https://vsthemes.org";
        private const string GalleryUrl = "https://vsthemes.org/en/wallpapers/";

        private readonly HttpClient _client;
        private readonly Dictionary<string, string> _detailUrlCache = new();
        private readonly Dictionary<string, string> _downloadUrlCache = new();

        public VSthemesGalleryService(IHttpClientFactory httpClientFactory)
        {
            _client = httpClientFactory.CreateClient();
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Aurian/1.0");
            _client.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<Page<WallpaperDto>> SearchWallpapersAsync(int page, int perPage, string? query = null, CancellationToken ct = default)
        {
            var url = page <= 1 ? GalleryUrl : $"{GalleryUrl}?page={page}";
            if (!string.IsNullOrWhiteSpace(query))
                url += (url.Contains('?') ? "&" : "?") + $"q={Uri.EscapeDataString(query)}";

            var html = await FetchHtmlAsync(url, ct);
            var items = ParseListing(html);

            var start = 0;
            var pageItems = items.Skip(start).Take(perPage).ToList();
            foreach (var item in pageItems)
            {
                _detailUrlCache[item.Id] = item.Contact;
            }

            return new Page<WallpaperDto>
            {
                Data = pageItems,
                Number = page,
                NextPageAvailable = pageItems.Count >= perPage
            };
        }

        public string? GetCachedDownloadExtension(string id)
        {
            if (_downloadUrlCache.TryGetValue(id, out var url))
            {
                var ext = Path.GetExtension(url.Split('?')[0]);
                if (!string.IsNullOrEmpty(ext))
                    return ext;
            }
            return ".zip";
        }

        public async Task<string?> ResolveDownloadUrlAsync(string id, CancellationToken ct = default)
        {
            if (_downloadUrlCache.TryGetValue(id, out var cached))
                return cached;

            if (!_detailUrlCache.TryGetValue(id, out var detailUrl) || string.IsNullOrEmpty(detailUrl))
                return null;

            var html = await FetchHtmlAsync(detailUrl, ct);

            var patterns = new[]
            {
                @"href=""(https?://[^""]+\.(?:zip|mp4|webm|mpkg))""",
                @"href=""(/[^""]+\.(?:zip|mp4|webm|mpkg))""",
                @"data-url=""(https?://[^""]+\.(?:zip|mp4|webm|mpkg))""",
                @"download[^>]+href=""(https?://[^""]+)""",
                @"href=""(https?://[^""]*vsthemes[^""]*download[^""]*)""",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var link = match.Groups[1].Value;
                    var resolved = link.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? link : BaseUrl + link;
                    _downloadUrlCache[id] = resolved;
                    return resolved;
                }
            }

            return null;
        }

        public async Task DownloadFileAsync(string url, string fileName, CancellationToken ct, Action<float, float, float>? progressCallback = null)
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var totalRead = 0L;
            var buffer = new byte[8192];
            var length = response.Content.Headers.ContentLength ?? 0;
            progressCallback?.Invoke(0, 0, length);

            int read;
            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;
                if (length > 0 && progressCallback != null && totalRead % 32768 == 0)
                    progressCallback((float)(totalRead / (double)length * 100f), totalRead, length);
            }

            progressCallback?.Invoke(100, length > 0 ? length : totalRead, length > 0 ? length : totalRead);
        }

        private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
        {
            using var response = await _client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }

        private static List<WallpaperDto> ParseListing(string html)
        {
            var results = new List<WallpaperDto>();
            var seen = new HashSet<string>();

            var linkPattern = @"href=""(/en/wallpapers/[^""]+?/(\d+)-[^""]+\.html)""";
            foreach (Match match in Regex.Matches(html, linkPattern, RegexOptions.IgnoreCase))
            {
                var path = match.Groups[1].Value;
                var id = match.Groups[2].Value;
                if (!seen.Add(id))
                    continue;

                var slug = path.Split('/').LastOrDefault()?.Replace(".html", "") ?? id;
                var title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slug.Split('-').Skip(1).DefaultIfEmpty(slug).Aggregate((a, b) => $"{a} {b}"));
                var detailUrl = BaseUrl + path;

                var thumb = ExtractThumbnailNear(html, path, id);

                results.Add(new WallpaperDto
                {
                    Id = id,
                    Title = title,
                    Description = "Aurian Gallery — vsthemes.org",
                    Thumbnail = thumb,
                    Preview = thumb,
                    IsPreviewAvailable = !string.IsNullOrEmpty(thumb),
                    Contact = detailUrl,
                    AppVersion = "1.0",
                    Tags = new List<string> { "vsthemes", "aurian" }
                });
            }

            return results;
        }

        private static string? ExtractThumbnailNear(string html, string path, string id)
        {
            var pathIndex = html.IndexOf(path, StringComparison.OrdinalIgnoreCase);
            if (pathIndex < 0)
                return null;

            var windowStart = Math.Max(0, pathIndex - 800);
            var window = html.Substring(windowStart, Math.Min(1600, html.Length - windowStart));
            var imgMatch = Regex.Match(window, @"src=""(https?://[^""]+\.(?:jpg|jpeg|png|webp|gif))""", RegexOptions.IgnoreCase);
            if (imgMatch.Success)
                return imgMatch.Groups[1].Value;

            imgMatch = Regex.Match(window, @"src=""(/[^""]+\.(?:jpg|jpeg|png|webp|gif))""", RegexOptions.IgnoreCase);
            return imgMatch.Success ? BaseUrl + imgMatch.Groups[1].Value : null;
        }
    }
}
