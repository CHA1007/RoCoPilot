namespace RocoPilot.Tools.FastTravel;

public sealed class FastTravelSettings
{
    public int ClickCooldownMs { get; set; } = 5000;

    public void SanitizeInPlace()
    {
        ClickCooldownMs = (int)Math.Clamp(ClickCooldownMs, 500, 30_000);
    }
}
