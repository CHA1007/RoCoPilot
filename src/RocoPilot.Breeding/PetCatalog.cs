using System.Reflection;
using System.Text.Json;

namespace RocoPilot.Breeding;

public sealed class PetCatalog
{
    public const string NoBreedGroup = "未发现";

    public const int MaxFilterGroups = 2;

    private static readonly string[] CanonicalGroups =
    [
        NoBreedGroup, "动物组", "拟人组", "巨灵组", "魔力组", "天空组", "两栖组", "植物组",
        "大地组", "妖精组", "昆虫组", "软体组", "机械组", "海洋组", "飞龙组",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, IReadOnlyList<Pet>> _byGroup;

    public IReadOnlyList<Pet> Pets { get; }

    public IReadOnlyList<string> Groups { get; }

    private PetCatalog(IReadOnlyList<Pet> pets)
    {
        Pets = pets;
        Groups = CanonicalGroups.Where(g => pets.Any(p => p.EggGroups.Contains(g)))
            .Concat(pets.SelectMany(p => p.EggGroups).Except(CanonicalGroups).Distinct())
            .ToList();
        _byGroup = Groups.ToDictionary(g => g, g => (IReadOnlyList<Pet>)pets.Where(p => p.EggGroups.Contains(g)).ToList());
    }

    public static PetCatalog LoadEmbedded()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RocoPilot.Breeding.Data.pets.json")
            ?? throw new InvalidOperationException("内置精灵数据缺失");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

    public static PetCatalog Load(string json)
    {
        var pets = JsonSerializer.Deserialize<List<Pet>>(json, JsonOptions) ?? [];
        var normalized = pets
            .Select(p => p with { EggGroups = p.EggGroups.Select(g => g == "龙组" ? "飞龙组" : g).ToList() })
            .OrderBy(p => p.Number)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Shiny)
            .ToList();
        return new PetCatalog(normalized);
    }

    public IReadOnlyList<Pet> PetsInGroup(string group) =>
        _byGroup.TryGetValue(group, out var pets) ? pets : [];

    public IReadOnlyList<string> SharedBreedGroups(Pet left, Pet right)
    {
        if (!left.Breedable || !right.Breedable) return [];
        return left.EggGroups.Where(g => g != NoBreedGroup && right.EggGroups.Contains(g)).ToList();
    }

    public Pet? Find(string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return null;

        if (int.TryParse(trimmed, out var number))
        {
            Pet? best = null;
            foreach (var pet in Pets)
            {
                if (pet.Number != number) continue;
                if (best is null || (!best.Breedable && pet.Breedable) || (best.Shiny && !pet.Shiny))
                {
                    best = pet;
                }
            }
            if (best is not null) return best;
        }

        foreach (var pet in Pets)
        {
            if (pet.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
                || pet.BaseName.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return pet;
            }
        }

        return null;
    }

    public IReadOnlyList<Pet> Suggest(string query, int limit = 8)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return [];

        var byNumber = int.TryParse(trimmed, out _);
        var seen = new HashSet<string>();
        var matches = new List<Pet>();
        foreach (var pet in Pets)
        {
            var hit = pet.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || pet.BaseName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || (byNumber && (pet.Number.ToString("D3").StartsWith(trimmed)
                    || pet.Number.ToString().StartsWith(trimmed)));
            if (hit && seen.Add(pet.DisplayName))
            {
                matches.Add(pet);
                if (matches.Count >= limit) break;
            }
        }
        return matches;
    }
}
