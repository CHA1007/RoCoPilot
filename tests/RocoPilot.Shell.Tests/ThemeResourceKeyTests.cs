using System.IO;
using System.Text.RegularExpressions;
using Wpf.Ui.Markup;

namespace RocoPilot.Shell.Tests;

public class ThemeResourceKeyTests
{
    private static readonly Regex Usage = new(@"\{ui:ThemeResource\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);

    [Fact]
    public void Every_theme_resource_key_used_in_xaml_is_known_to_wpf_ui()
    {
        var known = new HashSet<string>(Enum.GetNames(typeof(ThemeResource)));
        var usages = XamlFiles()
            .SelectMany(path => Usage.Matches(File.ReadAllText(path))
                .Select(match => (Path: path, Key: match.Groups[1].Value)))
            .ToList();

        Assert.True(usages.Count > 50, $"只扫到 {usages.Count} 处用法，扫描路径可能失效");

        var unknown = usages
            .Where(usage => !known.Contains(usage.Key))
            .Select(usage => $"{Path.GetFileName(usage.Path)}: {usage.Key}")
            .Distinct()
            .ToList();

        Assert.True(unknown.Count == 0, $"XAML 编译器不校验该枚举，运行期才会崩：{string.Join("、", unknown)}");
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoCoPilot.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 RoCoPilot.sln");
    }
}
