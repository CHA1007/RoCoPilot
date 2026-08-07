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
    AnchorUnknown,
    PoiTemplateMissing,
    CalibrationTemplateMissing,
    SceneTemplateMissing,
    MapNotConfirmed,
    CalibrationFailed,
    AlignmentFailed,
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
    public string PoiTemplatePath { get; init; } = "assets/templates/map/poi/魔力之源.png";

    public IReadOnlyList<string> HomelandTemplatePaths { get; init; } = ["assets/templates/map/homeland.png", "assets/templates/map/homeland-egg.png"];

    public string HomelandCloseTemplatePath { get; init; } = "assets/templates/map/homeland-close.png";

    public string ZoomOutButtonTemplatePath { get; init; } = "assets/templates/map/zoom-out.png";

    public string SceneTemplateRoot { get; init; } = "assets/templates/scene";

    public TimeSpan MapConfirmTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public InputKey MapOpenKey { get; init; } = InputKey.Keyboard(0x4D);

    public TimeSpan MapOpenPreCheck { get; init; } = TimeSpan.FromMilliseconds(1200);

    public double MapOpenConfirmThreshold { get; init; } = 0.6;

    public TimeSpan CalibrationStepTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public int ZoomOutClicks { get; init; } = 5;

    public TimeSpan ZoomButtonMatchTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ZoomClickInterval { get; init; } = TimeSpan.FromMilliseconds(300);

    public TimeSpan PoiMatchTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public double MaxAnchorDistance { get; init; } = 0.02;

    public double AlignInlierTolerance { get; init; } = 0.03;

    public int MinAlignInliers { get; init; } = 6;

    public int MaxMapPans { get; init; } = 3;

    public double MapPanMaxFraction { get; init; } = 0.4;

    public double TargetViewMargin { get; init; } = 0.05;

    public TimeSpan MapPanSettle { get; init; } = TimeSpan.FromMilliseconds(600);

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

    public async Task<PoiTeleportResult> TeleportAsync(string anchorName, CancellationToken cancellationToken = default)
    {
        var entry = AnchorCatalog.GroundEntries.FirstOrDefault(candidate => candidate.Name == anchorName);
        if (entry is null)
            return Fail(PoiTeleportFailure.AnchorUnknown, $"锚点「{anchorName}」不在内置魔力之源目录中（地下一层暂不支持）");

        return await Task.Run(() => Teleport(entry, cancellationToken), CancellationToken.None);
    }

    public PoiTeleportResult Teleport(AnchorCatalogEntry anchor, CancellationToken cancellationToken = default)
    {
        Emit("anchor_teleport", new Dictionary<string, object?> { ["anchor"] = anchor.Name, ["phase"] = "started" });

        if (CheckTemplates() is { } templateFailure)
            return templateFailure;

        using var poiMatcher = PoiIconMatcher.TryCreate(_options.PoiTemplatePath);
        if (poiMatcher is null)
            return Fail(PoiTeleportFailure.PoiTemplateMissing, $"魔力之源图标模板无法解码：{_options.PoiTemplatePath}");

        var matched = MatchOnCalibratedMap(poiMatcher, cancellationToken);
        if (!matched.Succeeded)
            return new PoiTeleportResult(false, matched.Failure!.Value, matched.Message);

        if (LocateWithPanning(poiMatcher, matched, anchor, cancellationToken, out var clickPoint) is { } locateFailure)
            return locateFailure;

        var (clickX, clickY) = clickPoint;

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

    private PoiTeleportResult? CheckTemplates()
    {
        if (!File.Exists(_options.PoiTemplatePath))
            return Fail(PoiTeleportFailure.PoiTemplateMissing, $"魔力之源图标模板缺失：{_options.PoiTemplatePath}");

        foreach (var path in _options.HomelandTemplatePaths.Concat([_options.HomelandCloseTemplatePath, _options.ZoomOutButtonTemplatePath]))
        {
            if (!File.Exists(path))
                return Fail(PoiTeleportFailure.CalibrationTemplateMissing, $"地图校准模板缺失：{path}");
        }

        return null;
    }

    private MatchedMap MatchOnCalibratedMap(PoiIconMatcher poiMatcher, CancellationToken cancellationToken)
    {
        using var probe = CreateMapOpenProbe();
        if (probe is null)
            return MatchedMap.Fail(Fail(PoiTeleportFailure.SceneTemplateMissing, "WorldMap 场景模板缺失（map-close.png），无法确认地图已打开"));

        if (EnsureMapOpen(probe, cancellationToken) is { } openFailure)
            return MatchedMap.Fail(openFailure);

        if (CalibrateMap(probe, cancellationToken) is { } calibrationFailure)
            return MatchedMap.Fail(calibrationFailure);

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

    private MapOpenProbe? CreateMapOpenProbe()
    {
        var worldMap = SceneDetectors.CreateWorldMap(_options.SceneTemplateRoot);
        if (worldMap is null) return null;

        TemplateSceneDetector? panel = null;
        var panelPath = Path.Combine(_options.SceneTemplateRoot, "map-panel-close.png");
        if (File.Exists(panelPath))
        {
            panel = new TemplateSceneDetector(
                GameScene.WorldMap, panelPath, (0.90, 0.00, 0.10, 0.14), _options.MapOpenConfirmThreshold);
        }

        var homeland = MultiIconMatcher.TryCreate(_options.HomelandTemplatePaths);
        return new MapOpenProbe(worldMap, panel, homeland);
    }

    private static bool ProbeMapOpen(MapOpenProbe probe, ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (probe.WorldMap.Detect(pixels, width, height) > 0f) return true;
        if (probe.Panel is { } panel && panel.Detect(pixels, width, height) > 0f) return true;
        return probe.Homeland is { } homeland && homeland.Find(pixels, width, height) is not null;
    }

    private PoiTeleportResult? EnsureMapOpen(MapOpenProbe probe, CancellationToken cancellationToken)
    {
        if (PollUntil(_options.MapOpenPreCheck, cancellationToken, (pixels, w, h) => ProbeMapOpen(probe, pixels, w, h), out var preCanceled))
        {
            Trace.TraceInformation("[PoiTeleportGuide] 预检确认地图已打开");
            return null;
        }

        if (preCanceled)
            return Fail(PoiTeleportFailure.Cancelled, "开图前被取消");

        _inputDriver.KeyPress(_options.MapOpenKey);
        Trace.TraceInformation("[PoiTeleportGuide] 预检未开图，按 M 打开世界地图");
        Emit("map_open", new Dictionary<string, object?> { ["key"] = "M" });

        if (!PollUntil(_options.MapConfirmTimeout, cancellationToken, (pixels, w, h) => ProbeMapOpen(probe, pixels, w, h), out var mapCanceled))
        {
            return mapCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "等待开图期间被取消")
                : Fail(PoiTeleportFailure.MapNotConfirmed, "按 M 开图后超时未确认世界地图场景");
        }

        return null;
    }

    private PoiTeleportResult? CalibrateMap(MapOpenProbe probe, CancellationToken cancellationToken)
    {
        using var homelandMatcher = MultiIconMatcher.TryCreate(_options.HomelandTemplatePaths);
        if (homelandMatcher is null)
            return Fail(PoiTeleportFailure.CalibrationTemplateMissing, $"家园按钮模板无法解码：{string.Join("；", _options.HomelandTemplatePaths)}");

        var homelandHit = MatchWithin(homelandMatcher.Find, _options.CalibrationStepTimeout, cancellationToken, out var homelandCanceled);
        if (homelandHit is not (int hx, int hy, _))
        {
            return homelandCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消")
                : Fail(PoiTeleportFailure.CalibrationFailed, "超时未找到地图右下角「家园」按钮，地图校准失败");
        }

        var (homelandX, homelandY) = _frameToScreen(hx, hy);
        _inputDriver.ClickAt(homelandX + _random.Next(-2, 3), homelandY + _random.Next(-2, 3));
        Trace.TraceInformation($"[PoiTeleportGuide] 家园按钮命中（{hx},{hy}），已点击（{homelandX},{homelandY}）");
        Emit("map_calibrate", new Dictionary<string, object?> { ["phase"] = "homeland_opened" });

        using var closeMatcher = PoiIconMatcher.TryCreate(_options.HomelandCloseTemplatePath);
        if (closeMatcher is null)
            return Fail(PoiTeleportFailure.CalibrationTemplateMissing, $"家园关闭按钮模板无法解码：{_options.HomelandCloseTemplatePath}");

        var closeHit = MatchWithin(closeMatcher.Find, _options.CalibrationStepTimeout, cancellationToken, out var closeCanceled);
        if (closeHit is not (int cx, int cy, _))
        {
            return closeCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消")
                : Fail(PoiTeleportFailure.CalibrationFailed, "家园界面未打开或超时未找到右上角关闭按钮，地图校准失败");
        }

        var (closeX, closeY) = _frameToScreen(cx, cy);
        _inputDriver.ClickAt(closeX + _random.Next(-2, 3), closeY + _random.Next(-2, 3));
        Trace.TraceInformation($"[PoiTeleportGuide] 家园关闭按钮命中（{cx},{cy}），已点击（{closeX},{closeY}）");

        if (!PollUntil(_options.CalibrationStepTimeout, cancellationToken, (pixels, w, h) => ProbeMapOpen(probe, pixels, w, h), out var reconfirmCanceled))
        {
            return reconfirmCanceled
                ? Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消")
                : Fail(PoiTeleportFailure.CalibrationFailed, "关闭家园后超时未回到世界地图，地图校准失败");
        }

        Emit("map_calibrate", new Dictionary<string, object?> { ["phase"] = "zoom_out" });
        Trace.TraceInformation("[PoiTeleportGuide] 已回到世界地图，开始缩放到最小");
        return ZoomOutToMinimum(cancellationToken);
    }

    private PoiTeleportResult? ZoomOutToMinimum(CancellationToken cancellationToken)
    {
        using var zoomButtonMatcher = PoiIconMatcher.TryCreate(_options.ZoomOutButtonTemplatePath);
        if (zoomButtonMatcher is null)
            return Fail(PoiTeleportFailure.CalibrationTemplateMissing, $"缩小按钮模板无法解码：{_options.ZoomOutButtonTemplatePath}");

        var button = MatchWithin(zoomButtonMatcher.Find, _options.ZoomButtonMatchTimeout, cancellationToken, out var buttonCanceled);
        if (buttonCanceled)
            return Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消");

        if (button is not (int bx, int by, _))
            return Fail(PoiTeleportFailure.CalibrationFailed, "未找到地图缩小按钮，缩放校准失败");

        var (screenX, screenY) = _frameToScreen(bx, by);
        var intervalMs = Math.Max(50, (int)_options.ZoomClickInterval.TotalMilliseconds);
        for (var i = 0; i < _options.ZoomOutClicks; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return Fail(PoiTeleportFailure.Cancelled, "地图校准期间被取消");

            _inputDriver.ClickAt(screenX + _random.Next(-2, 3), screenY + _random.Next(-2, 3));
            Thread.Sleep(intervalMs);
        }

        Trace.TraceInformation($"[PoiTeleportGuide] 缩小按钮（{bx},{by}）连点 {_options.ZoomOutClicks} 次完成");
        return null;
    }

    private PoiTeleportResult? LocateWithPanning(
        PoiIconMatcher poiMatcher,
        MatchedMap matched,
        AnchorCatalogEntry anchor,
        CancellationToken cancellationToken,
        out (int X, int Y) screen)
    {
        screen = (-1, -1);
        var current = matched;

        for (var pan = 0; ; pan++)
        {
            var normalizedHits = current.Hits
                .Select(hit => (hit.X / (double)current.Width, hit.Y / (double)current.Height))
                .ToList();

            if (normalizedHits.Count < _options.MinAlignInliers)
            {
                return Fail(
                    PoiTeleportFailure.PoiNotFound,
                    $"最小缩放下仅检测到 {normalizedHits.Count} 个魔力之源，不足对齐所需 {_options.MinAlignInliers} 个——缩放可能未生效");
            }

            var alignment = AnchorMapAligner.Align(
                AnchorCatalog.GroundEntries, normalizedHits, _options.AlignInlierTolerance, _options.MinAlignInliers);
            if (alignment is null)
            {
                return Fail(
                    PoiTeleportFailure.AlignmentFailed,
                    $"魔力之源命中（{normalizedHits.Count} 个）与内置目录对齐失败，无法定位锚点");
            }

            Emit("anchor_alignment", new Dictionary<string, object?>
            {
                ["inliers"] = alignment.InlierNames.Count,
                ["hits"] = normalizedHits.Count,
            });

            var (expectedX, expectedY) = alignment.Project(anchor.Lat, anchor.Lng);
            var bestDistance = double.MaxValue;
            (int X, int Y)? best = null;
            foreach (var hit in current.Hits)
            {
                var dx = hit.X / (double)current.Width - expectedX;
                var dy = hit.Y / (double)current.Height - expectedY;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (hit.X, hit.Y);
                }
            }

            if (best is not null && bestDistance <= _options.MaxAnchorDistance)
            {
                Trace.TraceInformation($"[PoiTeleportGuide] 锚点「{anchor.Name}」对齐内点 {alignment.InlierNames.Count} 个，选中命中（{best.Value.X},{best.Value.Y}），归一化距离 {bestDistance:F4}（平移 {pan} 次）");
                screen = _frameToScreen(best.Value.X, best.Value.Y);
                return null;
            }

            var margin = _options.TargetViewMargin;
            var inView = expectedX >= margin && expectedX <= 1 - margin && expectedY >= margin && expectedY <= 1 - margin;
            if (inView)
            {
                return Fail(
                    PoiTeleportFailure.PoiNotFound,
                    $"对齐后的预期位置（{expectedX:P0}, {expectedY:P0}）在视野内但未匹配到锚点「{anchor.Name}」的图标");
            }

            if (pan >= _options.MaxMapPans)
            {
                return Fail(
                    PoiTeleportFailure.PoiNotFound,
                    $"平移 {pan} 次后锚点「{anchor.Name}」的预期位置（{expectedX:P0}, {expectedY:P0}）仍不在视野内");
            }

            if (PanMap(expectedX, expectedY, current.Width, current.Height, cancellationToken) is { } panFailure)
                return panFailure;

            if (ScanHits(poiMatcher, cancellationToken, out var rescanned) is { } scanFailure)
                return scanFailure;

            current = rescanned;
        }
    }

    private PoiTeleportResult? ScanHits(
        PoiIconMatcher poiMatcher,
        CancellationToken cancellationToken,
        out MatchedMap matched)
    {
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
            }, out var canceled))
        {
            matched = new MatchedMap([], 0, 0);
            return canceled
                ? Fail(PoiTeleportFailure.Cancelled, "平移后重新匹配魔力之源期间被取消")
                : Fail(PoiTeleportFailure.PoiNotFound, "平移后未匹配到任何魔力之源图标");
        }

        matched = new MatchedMap(hits!, frameWidth, frameHeight);
        return null;
    }

    private PoiTeleportResult? PanMap(
        double expectedX,
        double expectedY,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Fail(PoiTeleportFailure.Cancelled, "地图平移前被取消");

        var maxFraction = _options.MapPanMaxFraction;
        var dx = Math.Clamp(0.5 - expectedX, -maxFraction, maxFraction);
        var dy = Math.Clamp(0.5 - expectedY, -maxFraction, maxFraction);

        var (startX, startY) = _frameToScreen(width / 2, height / 2);
        var (endX, endY) = _frameToScreen((int)(width * (0.5 + dx)), (int)(height * (0.5 + dy)));

        _inputDriver.MoveTo(startX, startY);
        _inputDriver.KeyDown(InputKey.LeftMouse);
        Thread.Sleep(50);
        _inputDriver.MoveTo(endX, endY);
        Thread.Sleep(50);
        _inputDriver.KeyUp(InputKey.LeftMouse);

        Trace.TraceInformation($"[PoiTeleportGuide] 拖动地图（{dx:F2},{dy:F2}）把预期位置（{expectedX:F2},{expectedY:F2}）拉向视野中心");
        Emit("map_pan", new Dictionary<string, object?> { ["dx"] = dx, ["dy"] = dy });

        Thread.Sleep(Math.Max(0, (int)_options.MapPanSettle.TotalMilliseconds));
        return null;
    }

    private (int X, int Y, double Score)? MatchWithin(
        IconFind find,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        out bool canceled)
    {
        (int X, int Y, double Score)? found = null;
        PollUntil(timeout, cancellationToken, (pixels, w, h) =>
        {
            found = find(pixels, w, h);
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
        var budgetMs = (long)timeout.TotalMilliseconds;
        var watch = Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < budgetMs)
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

    private delegate (int X, int Y, double Score)? IconFind(ReadOnlySpan<byte> pixels, int width, int height);

    private sealed class MultiIconMatcher : IDisposable
    {
        private readonly List<PoiIconMatcher> _matchers;

        private MultiIconMatcher(List<PoiIconMatcher> matchers) => _matchers = matchers;

        public static MultiIconMatcher? TryCreate(IEnumerable<string> templatePaths)
        {
            var matchers = new List<PoiIconMatcher>();
            foreach (var path in templatePaths)
            {
                var matcher = PoiIconMatcher.TryCreate(path);
                if (matcher is null)
                {
                    foreach (var created in matchers)
                        created.Dispose();
                    return null;
                }

                matchers.Add(matcher);
            }

            return matchers.Count > 0 ? new MultiIconMatcher(matchers) : null;
        }

        public (int X, int Y, double Score)? Find(ReadOnlySpan<byte> pixels, int width, int height)
        {
            (int X, int Y, double Score)? best = null;
            foreach (var matcher in _matchers)
            {
                var hit = matcher.Find(pixels, width, height);
                if (hit is not null && (best is null || hit.Value.Score > best.Value.Score))
                    best = hit;
            }

            return best;
        }

        public void Dispose()
        {
            foreach (var matcher in _matchers)
                matcher.Dispose();
        }
    }

    private sealed record MapOpenProbe(TemplateSceneDetector WorldMap, TemplateSceneDetector? Panel, MultiIconMatcher? Homeland) : IDisposable
    {
        public void Dispose()
        {
            WorldMap.Dispose();
            Panel?.Dispose();
            Homeland?.Dispose();
        }
    }

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
