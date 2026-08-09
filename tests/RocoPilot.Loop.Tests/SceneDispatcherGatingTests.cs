using System.Buffers;
using System.Collections.Concurrent;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;

namespace RocoPilot.Loop.Tests;

public class SceneDispatcherGatingTests
{
    private const int DebounceFrames = 3;

    [Fact]
    public void SingleDoubtFrameSuspendsSensingWithoutSwitchingScene()
    {
        var harness = new Harness();
        harness.EnqueueFrames(sceneMarker: 1, count: DebounceFrames);
        harness.Start();

        WaitUntil(() => harness.OpenWorldHandler.ActivateCount == 1);
        harness.ClearEvents();

        harness.EnqueueFrames(sceneMarker: 2, count: 1);
        WaitUntil(() => harness.OpenWorldHandler.SuspendCount == 1);
        Assert.Equal(0, harness.OpenWorldHandler.DeactivateCount);

        harness.EnqueueFrames(sceneMarker: 1, count: 1);
        WaitUntil(() => harness.OpenWorldHandler.ResumeCount == 1);
        Assert.Equal(0, harness.OpenWorldHandler.DeactivateCount);

        harness.Stop();

        Assert.Contains("sensing_suspended", harness.EventNames);
        Assert.Contains("sensing_resumed", harness.EventNames);
        Assert.DoesNotContain("scene_changed", harness.EventNames);
    }

    [Fact]
    public void ConfirmedSceneChangeDeactivatesWithoutResumingSensing()
    {
        var harness = new Harness();
        harness.EnqueueFrames(sceneMarker: 1, count: DebounceFrames);
        harness.Start();

        WaitUntil(() => harness.OpenWorldHandler.ActivateCount == 1);

        harness.EnqueueFrames(sceneMarker: 2, count: DebounceFrames);
        WaitUntil(() => harness.WorldMapHandler.ActivateCount == 1);

        Assert.Equal(1, harness.OpenWorldHandler.SuspendCount);
        Assert.Equal(0, harness.OpenWorldHandler.ResumeCount);
        Assert.Equal(1, harness.OpenWorldHandler.DeactivateCount);

        harness.Stop();
    }

    [Fact]
    public void SustainedDoubtFramesSuspendSensingOnlyOnce()
    {
        var harness = new Harness();
        harness.EnqueueFrames(sceneMarker: 1, count: DebounceFrames);
        harness.Start();

        WaitUntil(() => harness.OpenWorldHandler.ActivateCount == 1);

        var consumedBefore = harness.Source.ConsumedFrames;
        harness.EnqueueFrames(sceneMarker: 2, count: 2);
        WaitUntil(() => harness.Source.ConsumedFrames >= consumedBefore + 2);
        Assert.Equal(1, harness.OpenWorldHandler.SuspendCount);

        harness.Stop();
    }

    [Fact]
    public void HandlerHoldingActivationSurvivesSceneChange()
    {
        var harness = new Harness();
        harness.OpenWorldHandler.HoldOnScene = GameScene.WorldMap;
        harness.EnqueueFrames(sceneMarker: 1, count: DebounceFrames);
        harness.Start();

        WaitUntil(() => harness.OpenWorldHandler.ActivateCount == 1);

        harness.EnqueueFrames(sceneMarker: 2, count: DebounceFrames);
        WaitUntil(() => harness.Source.ConsumedFrames >= DebounceFrames * 2);
        Thread.Sleep(200);

        Assert.Equal(0, harness.OpenWorldHandler.DeactivateCount);
        Assert.Equal(0, harness.WorldMapHandler.ActivateCount);

        harness.Stop();
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("等待条件超时");
            Thread.Sleep(10);
        }
    }

    private sealed class Harness
    {
        public readonly ScriptedCaptureSource Source = new();
        public readonly RecordingHandler OpenWorldHandler = new(GameScene.OpenWorld);
        public readonly RecordingHandler WorldMapHandler = new(GameScene.WorldMap);
        public readonly List<string> EventNames = [];

        private readonly CancellationTokenSource _cts = new();
        private readonly SceneDispatcher _dispatcher;
        private readonly Thread _thread;

        public Harness()
        {
            var context = new SceneContext
            {
                InputDriver = new NoopDriver(),
                EmitEvent = _ => { },
                IsGameForeground = () => true,
            };

            _dispatcher = new SceneDispatcher(
                Source,
                new ISceneDetector[]
                {
                    new ByteSceneDetector(GameScene.OpenWorld, marker: 1),
                    new ByteSceneDetector(GameScene.WorldMap, marker: 2),
                },
                new Dictionary<GameScene, ISceneHandler>
                {
                    [GameScene.OpenWorld] = OpenWorldHandler,
                    [GameScene.WorldMap] = WorldMapHandler,
                },
                context,
                pollIntervalMs: 50,
                debounceFrames: DebounceFrames);

            _dispatcher.EventRaised += (_, e) =>
            {
                lock (EventNames) EventNames.Add(e.Name);
            };

            _thread = new Thread(() => _dispatcher.Run(_cts.Token)) { IsBackground = true };
        }

        public void Start() => _thread.Start();

        public void ClearEvents()
        {
            lock (EventNames) EventNames.Clear();
        }

        public void EnqueueFrames(int sceneMarker, int count)
        {
            for (var i = 0; i < count; i++)
                Source.Enqueue(sceneMarker);
        }

        public void Stop()
        {
            _cts.Cancel();
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ScriptedCaptureSource : ICaptureSource
    {
        private readonly ConcurrentQueue<CapturedFrame> _frames = new();
        private long _sequence;
        private long _consumed;

        public int ConsumedFrames => (int)Volatile.Read(ref _consumed);

        public string BackendName => "scripted";

        public string SourceDescription => "scripted";

        public int FrameWidth => 1;

        public int FrameHeight => 1;

        public double FramesPerSecond => 0;

        public long FramesDelivered => Volatile.Read(ref _consumed);

        public event EventHandler? FrameArrived { add { } remove { } }

        public event EventHandler<CaptureStoppedEventArgs>? Stopped { add { } remove { } }

        public void Enqueue(int sceneMarker)
        {
            var pixels = ArrayPool<byte>.Shared.Rent(4);
            pixels[0] = (byte)sceneMarker;
            _frames.Enqueue(new CapturedFrame(
                pixels, 1, 1, Interlocked.Increment(ref _sequence), DateTimeOffset.Now));
        }

        public bool TryGrabLatest(out CapturedFrame? frame)
        {
            if (_frames.TryDequeue(out frame))
            {
                Interlocked.Increment(ref _consumed);
                return true;
            }

            return false;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Stop() { }

        public void Dispose() { }
    }

    private sealed class ByteSceneDetector : ISceneDetector
    {
        private readonly byte _marker;

        public ByteSceneDetector(GameScene scene, byte marker)
        {
            Scene = scene;
            _marker = marker;
        }

        public GameScene Scene { get; }

        public float Detect(ReadOnlySpan<byte> bgraPixels, int width, int height)
            => bgraPixels.Length > 0 && bgraPixels[0] == _marker ? 0.9f : 0f;
    }

    private sealed class RecordingHandler : ISceneHandler
    {
        public RecordingHandler(GameScene scene) => Scene = scene;

        public GameScene Scene { get; }

        public bool IsEnabled { get; set; } = true;

        public int ActivateCount;

        public int DeactivateCount;

        public int SuspendCount;

        public int ResumeCount;

        public GameScene? HoldOnScene;

        public void Activate(SceneContext context) => Interlocked.Increment(ref ActivateCount);

        public bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height) => true;

        public void Deactivate() => Interlocked.Increment(ref DeactivateCount);

        public bool HoldActivation(GameScene nextScene) => nextScene == HoldOnScene;

        public void SuspendSensing() => Interlocked.Increment(ref SuspendCount);

        public void ResumeSensing() => Interlocked.Increment(ref ResumeCount);
    }

    private sealed class NoopDriver : IInputDriver
    {
        public string BackendName => "noop";

        public void Arm() { }

        public void MoveRelative(int dx, int dy) { }

        public void KeyDown(InputKey key) { }

        public void KeyUp(InputKey key) { }

        public void SendRawStroke(ReceivedStroke stroke) { }

        public void StartStrokeRelay(Action<ReceivedStroke> onStroke) { }

        public void StopStrokeRelay() { }

        public void Dispose() { }
    }
}
