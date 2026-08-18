namespace MetarViewer.Services;

/// <summary>
/// Settings shared by the outbound API clients.
///
/// The timeout and user agent were previously written out separately for each of the three
/// HTTP clients, so changing them meant finding and editing every copy.
/// </summary>
public sealed class MetarViewerOptions
{
    /// <summary>
    /// How long to wait for an API response before giving up.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The User-Agent sent with every request. Weather APIs ask callers to identify themselves.
    /// </summary>
    public string UserAgent { get; set; } = "MetarViewer/1.0";

    /// <summary>
    /// How long a retrieved report is reused before the station is queried again.
    /// </summary>
    public TimeSpan MetarCacheLifetime { get; set; } = CachingMetarService.DefaultLifetime;
}
