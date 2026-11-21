namespace MLPlatform.Api.Commands;

/// <summary>`mlp-api probe &lt;url&gt;`: healthcheck for the chiseled image (no curl); exits 0 on HTTP 2xx.</summary>
public static class ProbeCommand
{
    public static async Task RunIfRequestedAsync(string[] args)
    {
        if (args is not ["probe", var url]) return;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        int code;
        try { code = (await http.GetAsync(url)).IsSuccessStatusCode ? 0 : 1; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { code = 1; }
        Environment.Exit(code);
    }
}
