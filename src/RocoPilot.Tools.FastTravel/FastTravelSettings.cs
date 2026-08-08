namespace RocoPilot.Tools.FastTravel;

public enum FastTravelTriggerMode
{
    Auto,

    KeyPress,
}

public sealed class FastTravelSettings
{
    public int ClickCooldownMs { get; set; } = 5000;

    public FastTravelTriggerMode TriggerMode { get; set; } = FastTravelTriggerMode.Auto;

    public string TriggerKey { get; set; } = "F";

    public void SanitizeInPlace()
    {
        ClickCooldownMs = (int)Math.Clamp(ClickCooldownMs, 500, 30_000);
        if (!Enum.IsDefined(TriggerMode)) TriggerMode = FastTravelTriggerMode.Auto;
    }
}
