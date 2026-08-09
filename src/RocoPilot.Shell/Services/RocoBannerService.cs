using System.IO;
using System.Net.Http;
using RocoPilot.Settings;

namespace RocoPilot.Shell.Services;

public sealed class RocoBannerService
{
    private const string BannerUrl =
        "https://patchwiki.biligame.com/images/rocom/3/32/rfqecc9kp0xhie3q1jl0yxryzbyqmz5.png";
    private const string FileName = "season-banner.png";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _cachePath;

    public RocoBannerService()
    {
        _cachePath = Path.Combine(RocoPaths.CacheDirectory, "banners", FileName);
    }

    public string? GetCachedPath()
    {
        return File.Exists(_cachePath) ? _cachePath : null;
    }

    public async Task<string?> RefreshAsync(CancellationToken ct = default)
    {
        if (File.Exists(_cachePath))
        {
            return _cachePath;
        }

        try
        {
            var bytes = await Http.GetByteArrayAsync(BannerUrl, ct);
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await File.WriteAllBytesAsync(_cachePath, bytes, ct);
            return _cachePath;
        }
        catch
        {
            return null;
        }
    }
}