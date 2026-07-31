using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using RocoPilot.Core;
using RocoPilot.Settings;

namespace RocoPilot.Loop;

public sealed class SceneStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ISceneImageEncoder _encoder;
    private readonly int _keep;
    private readonly object _gate = new();

    public SceneStore(string scenesRoot, ISceneImageEncoder encoder, int keep = SceneRetention.DefaultKeepScenes)
    {
        ArgumentException.ThrowIfNullOrEmpty(scenesRoot);
        ScenesRoot = scenesRoot;
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _keep = keep;
    }

    public string ScenesRoot { get; }

    public string? Save(string trigger, ToolEvent cause, FrameSnapshot frame, IReadOnlyDictionary<string, object?> sidecar)
    {
        ArgumentException.ThrowIfNullOrEmpty(trigger);
        ArgumentNullException.ThrowIfNull(cause);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(sidecar);

        lock (_gate)
        {
            try
            {
                var sceneDir = Path.Combine(ScenesRoot, SceneDirectoryName(cause.Timestamp, trigger));
                Directory.CreateDirectory(sceneDir);
                File.WriteAllBytes(Path.Combine(sceneDir, "keyframe.png"), _encoder.EncodeKeyframe(frame));
                File.WriteAllBytes(Path.Combine(sceneDir, "overlay.png"), _encoder.EncodeOverlay(frame));
                File.WriteAllText(
                    Path.Combine(sceneDir, "scene.json"), JsonSerializer.Serialize(sidecar, JsonOptions));
                SceneRetention.PruneScenes(ScenesRoot, _keep);
                return sceneDir;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"失败现场落盘失败（trigger={trigger}）：{ex.GetBaseException().Message}");
                return null;
            }
        }
    }

    private static string SceneDirectoryName(DateTimeOffset timestamp, string trigger)
    {
        var safe = new string(trigger.Where(char.IsLetterOrDigit).ToArray());
        return $"{timestamp:HHmmssfff}-{(safe.Length == 0 ? "scene" : safe)}";
    }
}
