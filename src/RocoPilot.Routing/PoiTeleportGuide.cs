using System.Diagnostics;
using System.IO;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;
using RocoPilot.Tools.FastTravel;

namespace RocoPilot.Routing;

public enum PoiTeleportFailure
{
    None,
    Cancelled,
    PoiTemplateMissing,
    SceneTemplateMissing,
    MapNotConfirmed,
    PoiNotFound,
    TeleportTemplateMissing,
    TeleportButtonNotFound,
    LandingTimeout,
}

public sealed record PoiTeleportResult(bool Succeeded, PoiTeleportFailure Failure, string Message)
{
    public static PoiTeleportResult Landed(string message) => new(true, PoiTeleportFailure.None, message);
}

public sealed class PoiTeleportGuideOptions
{
    public string PoiTemplateRoot { get; init; } = "assets/templates/map/poi";

    public string SceneTemplateRoot { get; init; } = "assets/templates/scene";

    public TimeSpan MapConfirmTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan PoiMatchTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan TeleportClickTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan LandingTimeout { get; init; } = TimeSpan.FromSeconds(40);

    public int ConfirmConsecutiveHits { get; init; } = 2;

    public int LandingConsecutiveHits { get; init; } = 3;

    public int PollIntervalMs { get; init; } = 250;
}

public sealed class PoiTeleportGuide
{
    private readonly Func<CapturedFrame?> _grabFrame;
    private readonly IInputDriver _inputDriver;
    private readonly TeleportSensor? _teleportSensor;
    private readonly FastTravelSettings _teleportSettings;
    private readonly Func<int, int, (int X, int Y)> _frameToScreen;
    private readonly Func<bool>? _isGameForeground;
    private readonly Action<ToolEvent>? _emitEvent;
    private readonly PoiTeleportGuideOptions _options;
    private readonly Random _random = new();

    public PoiTeleportGuide(
        Func<CapturedFrame?> grabFrame,
        IInputDriver inputDriver,
        TeleportSensor? teleportSensor,
        FastTravelSettings teleportSettings,
        Func<int, int, (int X, int Y)>? frameToScreen = null,
        Func<bool>? isGameForeground = null,
        Action<ToolEvent>? emitEvent = null,
        PoiTeleportGuideOptions? options = null)
    {
        _grabFrame = grabFrame ?? throw new ArgumentNullException(nameof(grabFrame));
        _inputDriver = inputDriver ?? throw new ArgumentNullException(nameof(inputDriver));
        _teleportSensor = teleportSensor;
        _teleportSettings = teleportSettings ?? throw new ArgumentNullException(nameof(teleportSettings));
        _frameToScreen = frameToScreen ?? ((x, y) => (x, y));
        _isGameForeground = isGameForeground;
        _emitEvent = emitEvent;
        _options = options ?? new PoiTeleportGuideOptions();
    }

    public static string PoiTemplatePath(string poiName, string poiTemplateRoot)
        => Path.Combine(poiTemplateRoot, poiName + ".png");

    public static IReadOnlyList<string> AvailablePoiNames(string poiTemplateRoot)
    {
        if (!Directory.Exists(poiTemplateRoot))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(poiTemplateRoot, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    public PoiTeleportResult Teleport(string poiName, CancellationToken cancellationToken = default)
    {
        Emit("anchor_teleport", new Dictionary<string, object?> { ["poi"] = poiName, ["phase"] = "started" });

        var templatePath = PoiTemplatePath(poiName, _options.PoiTemplateRoot);
        if (!File.Exists(templatePath))
            return Fail(PoiTeleportFailure.PoiTemplateMissing, $"POI 模板缺失：{templatePath}");

        using var matcher = PoiIconMatcher.TryCreate(templatePath);
        if (matcher is null)
            return Fail(PoiTeleportFailure.PoiTemplateMissing, $"POI 模板无法解码：{templatePath}");

        var worldMapDetector = SceneDetectors.CreateWorldMap(_options.SceneTemplateRoot);
        if (worldMapDetector is null)
            return Fail(PoiTeleportFailure.SceneTemplateMissing, "WorldMap 场景模板缺失（map-close.png），无法确认地图已打开");
        using (worldMapDetector)
        {
            if (!PollConsecutiveHits(_options.MapConfirmTimeout, worldMapDetector, _options.ConfirmConsecutiveHits, cancellationToken, out var canceled))
                return canceled
                    ? Fail(PoiTeleportFailure.Cancelled, "等待开图期间被取消")
                    : Fail(PoiTeleportFailure.MapNotConfirmed, "超时未确认世界地图场景");
        }

        var poiHit = MatchPoi(matcher, cancellationToken);
        if (poiHit is (int px, int py, _))
        {
            var (clickX, clickY) = _frameToScreen(px, py);
            _inputDriver.ClickAt(clickX + _random.Next(-4, 5), clickY + _random.Next(-4, 5));
            Emit("poi_click", new Dictionary<string, object?>
            {
                ["poi"] = poiName,
                ["x"] = clickX,
                ["y"] = clickY,
            });
        }
        else
        {
            return Fail(PoiTeleportFailure.PoiNotFound, $"当前地图视野内未匹配到 POI「{poiName}」");
        }

        if (_teleportSensor is null)
            return Fail(PoiTeleportFailure.TeleportTemplateMissing, "地图快传模板缺失（assets/templates/map/teleport.png），无法点击传送按钮");

        _teleportSettings.SanitizeInPlace();
        var teleportLink = new TeleportButtonLink(_teleportSensor, _inputDriver, _teleportSettings.ClickCooldownMs, _frameToScreen, _emitEvent);
        if (!PollUntil(_options.TeleportClickTimeout, cancellationToken, (pixels, w, h) => teleportLink.TryClick(pixels, w, h), out var teleportCanceled))
            return teleportCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "等待传送按钮期间被取消")
                : Fail(PoiTeleportFailure.TeleportButtonNotFound, "超时未找到传送按钮");

        var openWorldTemplate = Path.Combine(_options.SceneTemplateRoot, "openworld-chat.png");
        if (!File.Exists(openWorldTemplate))
            return Fail(PoiTeleportFailure.SceneTemplateMissing, "OpenWorld 场景模板缺失（openworld-chat.png），无法判定落地");

        using var openWorldDetector = SceneDetectors.CreateOpenWorld(_options.SceneTemplateRoot);
        if (!PollConsecutiveHits(_options.LandingTimeout, openWorldDetector, _options.LandingConsecutiveHits, cancellationToken, out var landingCanceled))
            return landingCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "等待落地期间被取消")
                : Fail(PoiTeleportFailure.LandingTimeout, "传送后超时未检测到开放世界场景，落地判定失败");

        Trace.TraceInformation($"[PoiTeleportGuide] 锚点「{poiName}」落地确认");
        Emit("anchor_teleport", new Dictionary<string, object?> { ["poi"] = poiName, ["phase"] = "landed" });
        return PoiTeleportResult.Landed($"锚点「{poiName}」传送落地成功");
    }

    private (int X, int Y, double Score)? MatchPoi(PoiIconMatcher matcher, CancellationToken cancellationToken)
    {
        (int X, int Y, double Score)? found = null;
        PollUntil(_options.PoiMatchTimeout, cancellationToken, (pixels, w, h) =>
        {
            found = matcher.Find(pixels, w, h);
            return found is not null;
        }, out _);
        return found;
    }

    private bool PollConsecutiveHits(
        TimeSpan timeout,
        TemplateSceneDetector detector,
        int requiredHits,
        CancellationToken cancellationToken,
        out bool canceled)
    {
        var hits = 0;
        return PollUntil(timeout, cancellationToken, (pixels, w, h) =>
        {
            hits = detector.Detect(pixels, w, h) > 0f ? hits + 1 : 0;
            return hits >= requiredHits;
        }, out canceled);
    }

    private bool PollUntil(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<ReadOnlySpan<byte>, int, int, bool> probe,
        out bool canceled)
    {
        var pollMs = Math.Max(50, _options.PollIntervalMs);
        var elapsedMs = 0L;
        var budgetMs = (long)timeout.TotalMilliseconds;

        while (elapsedMs < budgetMs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                return false;
            }

            if (_isGameForeground is not null && !_isGameForeground())
            {
                Thread.Sleep(pollMs);
                continue;
            }

            using var frame = _grabFrame();
            if (frame is not null && !frame.Pixels.IsEmpty && probe(frame.Pixels, frame.Width, frame.Height))
            {
                canceled = false;
                return true;
            }

            Thread.Sleep(pollMs);
            elapsedMs += pollMs;
        }

        canceled = false;
        return false;
    }

    private PoiTeleportResult Fail(PoiTeleportFailure failure, string message)
    {
        Trace.TraceWarning($"[PoiTeleportGuide] {failure}: {message}");
        Emit("anchor_failed", new Dictionary<string, object?>
        {
            ["failure"] = failure.ToString(),
            ["message"] = message,
        });
        return new PoiTeleportResult(false, failure, message);
    }

    private void Emit(string name, IReadOnlyDictionary<string, object?> data)
        => _emitEvent?.Invoke(new ToolEvent(name, data));
}
