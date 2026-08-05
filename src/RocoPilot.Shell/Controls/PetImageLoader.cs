using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace RocoPilot.Shell.Controls;

internal static class PetImageLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim Gate = new(8);
    private static readonly ConcurrentDictionary<string, Task<BitmapImage?>> Cache = new();

    public static Task<BitmapImage?> GetOrCreateAsync(string url) => Cache.GetOrAdd(url, LoadAsync);

    private static async Task<BitmapImage?> LoadAsync(string url)
    {
        await Gate.WaitAsync();
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 128;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
