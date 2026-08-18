using StardewModdingAPI.Framework.Input;

var tests = new (string Name, Action Run)[]
{
    ("first read samples", FirstReadSamples),
    ("repeated reads reuse snapshot", RepeatedReadsReuseSnapshot),
    ("frame without SMAPI sampling lets the game sample once", FrameWithoutSmapiSamplingLetsGameSampleOnce),
    ("skipped game update resets next frame", SkippedGameUpdateResetsNextFrame),
    ("press and release edges remain stable", PressAndReleaseEdgesRemainStable),
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

Console.WriteLine($"Passed {tests.Length} Android input snapshot tests.");

static void FirstReadSamples()
{
    var gate = new AndroidInputSnapshotGate();
    gate.BeginFrame();

    Equal(true, gate.TryBeginSampling());
}

static void RepeatedReadsReuseSnapshot()
{
    var gate = new AndroidInputSnapshotGate();
    gate.BeginFrame();

    Equal(true, gate.TryBeginSampling());
    Equal(false, gate.TryBeginSampling());
    Equal(false, gate.TryBeginSampling());
}

static void FrameWithoutSmapiSamplingLetsGameSampleOnce()
{
    var gate = new AndroidInputSnapshotGate();
    gate.BeginFrame();

    Equal(true, gate.TryBeginSampling());
    Equal(false, gate.TryBeginSampling());
}

static void SkippedGameUpdateResetsNextFrame()
{
    var gate = new AndroidInputSnapshotGate();
    gate.BeginFrame();
    Equal(true, gate.TryBeginSampling());

    gate.BeginFrame();
    Equal(true, gate.TryBeginSampling());
}

static void PressAndReleaseEdgesRemainStable()
{
    var input = new SimulatedInput();

    input.PhysicalIsDown = true;
    input.BeginFrame();
    input.Read();
    Equal(true, input.SnapshotIsDown);

    input.PhysicalIsDown = false;
    input.Read();
    Equal(true, input.SnapshotIsDown);
    Equal(1, input.SampleCount);

    input.BeginFrame();
    input.Read();
    Equal(false, input.SnapshotIsDown);
    Equal(2, input.SampleCount);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', but found '{actual}'.");
}

internal sealed class SimulatedInput
{
    private readonly AndroidInputSnapshotGate Gate = new();

    public bool PhysicalIsDown { get; set; }

    public bool SnapshotIsDown { get; private set; }

    public int SampleCount { get; private set; }

    public void BeginFrame()
    {
        this.Gate.BeginFrame();
    }

    public void Read()
    {
        if (!this.Gate.TryBeginSampling())
            return;

        this.SnapshotIsDown = this.PhysicalIsDown;
        this.SampleCount++;
    }
}
