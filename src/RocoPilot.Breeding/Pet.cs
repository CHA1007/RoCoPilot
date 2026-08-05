namespace RocoPilot.Breeding;

public sealed record Pet(
    int Id,
    int Number,
    string Name,
    string BaseName,
    IReadOnlyList<string> EggGroups,
    int Stage,
    bool Shiny,
    bool Breedable,
    string? GlowFrom,
    string? GlowTo,
    string? ImageUrl)
{
    public string DisplayName => Shiny ? Name + "（异色）" : Name;
}
