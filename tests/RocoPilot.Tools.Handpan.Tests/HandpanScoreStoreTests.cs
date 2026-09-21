namespace RocoPilot.Tools.Handpan.Tests;

public class HandpanScoreStoreTests
{
    private static string TempRoot() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocopilot-handpan-store-{Guid.NewGuid():N}");

    private static string WriteScore(string root, string fileName)
    {
        Directory.CreateDirectory(root);
        var path = System.IO.Path.Combine(root, fileName);
        File.WriteAllText(path, "score");
        return path;
    }

    [Fact]
    public void An_empty_library_lists_nothing_and_resolves_nothing()
    {
        using var root = new TempRootGuard(TempRoot());
        var store = new HandpanScoreStore(root.Path);

        Assert.Empty(store.List());
        Assert.Null(store.Resolve(null));
        Assert.Null(store.Resolve(""));
        Assert.Null(store.Resolve("  "));
        Assert.Null(store.Resolve("不存在"));
    }

    [Fact]
    public void Importing_copies_the_score_under_its_name()
    {
        using var root = new TempRootGuard(TempRoot());
        var store = new HandpanScoreStore(root.Path);
        using var score = TempScore.WithNotes((0, 72));

        var name = store.Import(score.Path);

        Assert.Equal(score.Name, name);
        Assert.Equal([name], store.List());
        Assert.Equal(System.IO.Path.Combine(root.Path, name + ".mid"), store.Resolve(name));
        Assert.Equal(File.ReadAllBytes(score.Path), File.ReadAllBytes(store.Resolve(name)!));
    }

    [Fact]
    public void Importing_normalizes_the_extension()
    {
        using var root = new TempRootGuard(TempRoot());
        var store = new HandpanScoreStore(root.Path);
        var source = WriteScore(System.IO.Path.GetTempPath(), $"rocopilot-{Guid.NewGuid():N}.midi");

        var name = store.Import(source);
        File.Delete(source);

        Assert.EndsWith(".mid", store.Resolve(name));
        Assert.EndsWith(".midi", source);
    }

    [Fact]
    public void Importing_again_overwrites_the_same_score()
    {
        using var root = new TempRootGuard(TempRoot());
        var store = new HandpanScoreStore(root.Path);
        var source = WriteScore(System.IO.Path.GetTempPath(), $"晴天-{Guid.NewGuid():N}.mid");
        try
        {
            var name = store.Import(source);
            File.WriteAllText(source, "updated");
            store.Import(source);

            Assert.Equal([name], store.List());
            Assert.Equal("updated", File.ReadAllText(store.Resolve(name)!));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public void Importing_a_nameless_score_is_rejected()
    {
        using var root = new TempRootGuard(TempRoot());
        var store = new HandpanScoreStore(root.Path);

        Assert.Throws<ArgumentException>(() => store.Import(".mid"));
    }

    [Fact]
    public void Lists_are_sorted_and_ignores_unrelated_files()
    {
        using var root = new TempRootGuard(TempRoot());
        var store = new HandpanScoreStore(root.Path);
        WriteScore(root.Path, "b.mid");
        WriteScore(root.Path, "a.mid");
        WriteScore(root.Path, "notes.txt");

        Assert.Equal(["a", "b"], store.List());
    }

    private sealed class TempRootGuard(string path) : IDisposable
    {
        public string Path => path;

        public void Dispose()
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
