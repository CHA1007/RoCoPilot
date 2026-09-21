using System.IO;
using RocoPilot.Settings;

namespace RocoPilot.Tools.Handpan;

public sealed class HandpanScoreStore
{
    private const string Extension = ".mid";

    private readonly string _root;

    public HandpanScoreStore(string? root = null) => _root = root ?? RocoPaths.ScoresRoot;

    public string Root => _root;

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        return Directory.EnumerateFiles(_root, "*" + Extension)
            .Select(file => Path.GetFileNameWithoutExtension(file)!)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();
    }

    public string? Resolve(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var path = Path.Combine(_root, trimmed + Extension);
        return File.Exists(path) ? path : null;
    }

    public string Import(string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("导入的曲谱没有名称", nameof(sourcePath));
        }

        Directory.CreateDirectory(_root);
        File.Copy(sourcePath, Path.Combine(_root, name + Extension), overwrite: true);
        return name;
    }
}
