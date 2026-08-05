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
    AnchorNotRegistered,
    PoiTemplateMissing,
    CalibrationTemplateMissing,
    SceneTemplateMissing,
    MapNotConfirmed,
    CalibrationFailed,
    PoiNotFound,
    TeleportTemplateMissing,
    TeleportButtonNotFound,
    LandingTimeout,
}

public sealed record PoiTeleportResult(bool Succeeded, PoiTeleportFailure Failure, string Message)
{
    public static PoiTeleportResult Landed(string message) => new(true, PoiTeleportFailure.None, message);
}

public sealed record AnchorScanHit(double X, double Y, double Score);

public sealed record PoiScanResult(
    bool Succeeded,
    PoiTeleportFailure Failure,
    string Message,
    IReadOnlyList<AnchorScanHit> Positions)
{
    public static PoiScanResult Found(IReadOnlyList<AnchorScanHit> positions)
        => new(true, PoiTeleportFailure.None, $"扫描到 {positions.Count} 个魔力之源", positions);
}

public sealed class PoiTeleportGuideOptions
{
    public string PoiTemplatePath { get; init; } = "assets/templates/map/poi/魔力之源.png";

    public string HomelandTemplatePath { get; init; } = "assets/templates/map/homeland.png";

    public string HomelandCloseTemplatePath { get; init; } = "assets/templates/map/homeland-close.png";

    public string SceneTemplateRoot { get; init; } = "assets/templates/scene";

    public TimeSpan MapConfirmTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan CalibrationStepTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public int ZoomOutTicks { get; init; } = 24;

    public int ZoomTickIntervalMs { get; init; } = 80;

    public int ZoomOutRolling { get; init; } = -120;

    public TimeSpan ZoomSettle { get; init; } = TimeSpan.FromMilliseconds(800);

    public TimeSpan PoiMatchTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public double MaxAnchorDistance { get; init; } = 0.08;

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
    private readonly Func<CancellationToken, Task<IReadOnlyList<AnchorEntry>>>? _anchorListProvider;
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
        Func<CancellationToken, Task<IReadOnlyList<AnchorEntry>>>? anchorListProvider = null,
        PoiTeleportGuideOptions? options = null)
    {
        _grabFrame = grabFrame ?? throw new ArgumentNullException(nameof(grabFrame));
        _inputDriver = inputDriver ?? throw new ArgumentNullException(nameof(inputDriver));
        _teleportSensor = teleportSensor;
        _teleportSettings = teleportSettings ?? throw new ArgumentNullException(nameof(teleportSettings));
        _frameToScreen = frameToScreen ?? ((x, y) => (x, y));
        _isGameForeground = isGameForeground;
        _emitEvent = emitEvent;
        _anchorListProvider = anchorListProvider;
        _options = options ?? new PoiTeleportGuideOptions();
    }

    public async Task<PoiTeleportResult> TeleportAsync(string anchorName, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AnchorEntry> anchors;
        try
        {
            anchors = _anchorListProvider is null ? [] : await _anchorListProvider(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(PoiTeleportFailure.AnchorNotRegistered, $"锚点名单加载失败：{ex.Message}");
        }

        var entry = anchors.FirstOrDefault(candidate => candidate.Name == anchorName);
        if (entry is null)
            return Fail(PoiTeleportFailure.AnchorNotRegistered, $"锚点「{anchorName}」不在魔力之源锚点名单中");

        return await Task.Run(() => Teleport(entry, cancellationToken), CancellationToken.None);
    }

    public PoiTeleportResult Teleport(AnchorEntry anchor, CancellationToken cancellationToken = default)
    {
        Emit("anchor_teleport", new Dictionary<string, object?> { ["anchor"] = anchor.Name, ["phase"] = "started" });

        if (CheckTemplates() is { } templateFailure)
            return templateFailure;

        var matched = MatchOnCalibratedMap(cancellationToken);
        if (!matched.Succeeded)
            return new PoiTeleportResult(false, matched.Failure!.Value, matched.Message);

        var (clickX, clickY) = SelectNearest(matched, anchor);
        if (clickX < 0)
        {
            return Fail(
                PoiTeleportFailure.PoiNotFound,
                $"最小缩放全图上未找到与锚点「{anchor.Name}」（{anchor.X:P0}, {anchor.Y:P0}）足够接近的魔力之源");
        }

        _inputDriver.ClickAt(clickX + _random.Next(-3, 4), clickY + _random.Next(-3, 4));
        Emit("poi_click", new Dictionary<string, object?>
        {
            ["anchor"] = anchor.Name,
            ["x"] = clickX,
            ["y"] = clickY,
        });

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

        Trace.TraceInformation($"[PoiTeleportGuide] 锚点「{anchor.Name}」落地确认");
        Emit("anchor_teleport", new Dictionary<string, object?> { ["anchor"] = anchor.Name, ["phase"] = "landed" });
        return PoiTeleportResult.Landed($"锚点「{anchor.Name}」传送落地成功");
    }

    public PoiScanResult ScanAnchors(CancellationToken cancellationToken = default)
    {
        if (CheckTemplates() is { } templateFailure)
            return new PoiScanResult(false, templateFailure.Failure, templateFailure.Message, []);

        var matched = MatchOnCalibratedMap(cancellationToken);
        if (!matched.Succeeded)
            return new PoiScanResult(false, matched.Failure!.Value, matched.Message, []);

        var positions = matched.Hits
            .Select(hit => new AnchorScanHit(hit.X / (double)matched.Width, hit.Y / (double)matched.Height, hit.Score))
            .ToList();

        Emit("anchor_scan", new Dictionary<string, object?> { ["count"] = positions.Count });
        return PoiScanResult.Found(positions);
    }

    private PoiTeleportResult? CheckTemplates()
    {
        if (!File.Exists(_options.PoiTemplatePath))
            return Fail(PoiTeleportFailure.PoiTemplateMissing, $"魔力之源图标模板缺失：{_options.PoiTemplatePath}");

        foreach (var path in new[] { _options.HomelandTemplatePath, _options.HomelandCloseTemplatePath })
        {
            if (!File.Exists(path))
                return Fail(PoiTeleportFailure.CalibrationTemplateMissing, $"地图校准模板缺失：{path}");
        }

        return null;
    }

    private MatchedMap MatchOnCalibratedMap(CancellationToken cancellationToken)
    {
        using var worldMapDetector = SceneDetectors.CreateWorldMap(_options.SceneTemplateRoot);
        if (worldMapDetector is null)
            return MatchedMap.Fail(Fail(PoiTeleportFailure.SceneTemplateMissing, "WorldMap 场景模板缺失（map-close.png），无法确认地图已打开"));

        if (!PollConsecutiveHits(_options.MapConfirmTimeout, worldMapDetector, _options.ConfirmConsecutiveHits, cancellationToken, out var mapCanceled))
        {
            return MatchedMap.Fail(mapCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "等待开图期间被取消")
                : Fail(PoiTeleportFailure.MapNotConfirmed, "超时未确认世界地图场景"));
        }

        if (CalibrateMap(cancellationToken) is { } calibrationFailure)
            return MatchedMap.Fail(calibrationFailure);

        using var poiMatcher = PoiIconMatcher.TryCreate(_options.PoiTemplatePath);
        if (poiMatcher is null)
            return MatchedMap.Fail(Fail(PoiTeleportFailure.PoiTemplateMissing, $"魔力之源图标模板无法解码：{_options.PoiTemplatePath}"));

        (int X, int Y, double Score)[]? hits = null;
        var frameWidth = 0;
        var frameHeight = 0;
        if (!PollUntil(_options.PoiMatchTimeout, cancellationToken, (pixels, w, h) =>
            {
                var found = poiMatcher.FindAll(pixels, w, h);
                if (found.Count == 0) return false;

                hits = [.. found];
                frameWidth = w;
                frameHeight = h;
                return true;
            }, out var matchCanceled))
        {
            return MatchedMap.Fail(matchCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "匹配魔力之源期间被取消")
                : Fail(PoiTeleportFailure.PoiNotFound, "最小缩放全图上未匹配到任何魔力之源图标"));
        }

        return new MatchedMap(hits!, frameWidth, frameHeight);
    }

    private PoiTeleportResult? CalibrateMap(CancellationToken cancellationToken)
    {
        using var homelandMatcher = PoiIconMatcher.TryCreate(_options.HomelandTemplatePath);
        if (homelandMatcher is null)
            return Fail(PoiTeleportFailure.CalibrationTemplateMissing, $"家园按钮模板无法解码：{_options.HomelandTemplatePath}");

        var homelandHit = MatchWithin(homelandMatcher, _options.CalibrationStepTimeout, cancellationToken, out var homelandCanceled);
        if (homelandHit is not (int hx, int hy, _))
        {
            return homelandCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消")
                : Fail(PoiTeleportFailure.CalibrationFailed, "超时未找到地图右下角「家园」按钮，地图校准失败");
        }

        var (homelandX, homelandY) = _frameToScreen(hx, hy);
        _inputDriver.ClickAt(homelandX + _random.Next(-2, 3), homelandY + _random.Next(-2, 3));
        Emit("map_calibrate", new Dictionary<string, object?> { ["phase"] = "homeland_opened" });

        using var closeMatcher = PoiIconMatcher.TryCreate(_options.HomelandCloseTemplatePath);
        if (closeMatcher is null)
            return Fail(PoiTeleportFailure.CalibrationTemplateMissing, $"家园关闭按钮模板无法解码：{_options.HomelandCloseTemplatePath}");

        var closeHit = MatchWithin(closeMatcher, _options.CalibrationStepTimeout, cancellationToken, out var closeCanceled);
        if (closeHit is not (int cx, int cy, _))
        {
            return closeCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消")
                : Fail(PoiTeleportFailure.CalibrationFailed, "家园界面未打开或超时未找到右上角关闭按钮，地图校准失败");
        }

        var (closeX, closeY) = _frameToScreen(cx, cy);
        _inputDriver.ClickAt(closeX + _random.Next(-2, 3), closeY + _random.Next(-2, 3));

        using var reconfirmDetector = SceneDetectors.CreateWorldMap(_options.SceneTemplateRoot);
        if (reconfirmDetector is null)
            return Fail(PoiTeleportFailure.SceneTemplateMissing, "WorldMap 场景模板缺失（map-close.png），无法确认已回到地图");

        if (!PollConsecutiveHits(_options.CalibrationStepTimeout, reconfirmDetector, _options.ConfirmConsecutiveHits, cancellationToken, out var reconfirmCanceled))
        {
            return reconfirmCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消")
                : Fail(PoiTeleportFailure.CalibrationFailed, "关闭家园后超时未回到世界地图，地图校准失败");
        }

        Emit("map_calibrate", new Dictionary<string, object?> { ["phase"] = "zoom_out" });
        return ZoomOutToMinimum(cancellationToken);
    }

    private PoiTeleportResult? ZoomOutToMinimum(CancellationToken cancellationToken)
    {
        using var sizingFrame = _grabFrame();
        if (sizingFrame is null || sizingFrame.Pixels.IsEmpty)
            return Fail(PoiTeleportFailure.CalibrationFailed, "无法抓取画面，缩放校准失败");

        var (centerX, centerY) = _frameToScreen(sizingFrame.Width / 2, sizingFrame.Height / 2);
        _inputDriver.MoveTo(centerX, centerY);

        var intervalMs = Math.Max(20, _options.ZoomTickIntervalMs);
        for (var i = 0; i < _options.ZoomOutTicks; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消");

            _inputDriver.Wheel(_options.ZoomOutRolling);
            Thread.Sleep(intervalMs);
        }

        Thread.Sleep(Math.Max(0, (int)_options.ZoomSettle.TotalMilliseconds));
        return null;
    }

    private (int ScreenX, int ScreenY) SelectNearest(MatchedMap matched, AnchorEntry anchor)
    {
        var bestDistance = double.MaxValue;
        (int X, int Y)? best = null;

        foreach (var hit in matched.Hits)
        {
            var dx = hit.X / (double)matched.Width - anchor.X;
            var dy = hit.Y / (double)matched.Height - anchor.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (hit.X, hit.Y);
            }
        }

        if (best is null || bestDistance > _options.MaxAnchorDistance)
            return (-1, -1);

        Trace.TraceInformation($"[PoiTeleportGuide] 锚点「{anchor.Name}」选中命中（{best.Value.X},{best.Value.Y}），归一化距离 {bestDistance:F4}");
        return _frameToScreen(best.Value.X, best.Value.Y);
    }

    private (int X, int Y, double Score)? MatchWithin(
        PoiIconMatcher matcher,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        out bool canceled)
    {
        (int X, int Y, double Score)? found = null;
        PollUntil(timeout, cancellationToken, (pixels, w, h) =>
        {
            found = matcher.Find(pixels, w, h);
            return found is not null;
        }, out canceled);
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

    private sealed record MatchedMap(IReadOnlyList<(int X, int Y, double Score)> Hits, int Width, int Height)
    {
        public bool Succeeded => Failure is null;

        public PoiTeleportFailure? Failure { get; private init; }

        public string Message { get; private init; } = string.Empty;

        public static MatchedMap Fail(PoiTeleportResult failure) => new([], 0, 0)
        {
            Failure = failure.Failure,
            Message = failure.Message,
        };
    }
}
