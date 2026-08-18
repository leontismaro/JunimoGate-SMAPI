namespace StardewModdingAPI.Mobile;

/// <summary>Owns Android world-entry and save-loader transitions while startup dependencies settle.</summary>
internal sealed class AndroidWorldEntryGate
{
    private readonly object syncRoot = new();
    private WorldEntryState state;

    public WorldEntryState State
    {
        get
        {
            lock (this.syncRoot)
                return this.state;
        }
    }

    public void Request()
    {
        lock (this.syncRoot)
        {
            if (this.state == WorldEntryState.Idle)
                this.state = WorldEntryState.EntryRequested;
        }
    }

    public bool BeginLoading()
    {
        lock (this.syncRoot)
        {
            WorldEntryState previous = this.state;
            if (previous is WorldEntryState.LoaderPending
                or WorldEntryState.LoaderWaitingForAudio
                or WorldEntryState.Loading)
                return false;
            this.state = previous == WorldEntryState.EntryWaitingForAudio
                ? WorldEntryState.LoaderWaitingForAudio
                : WorldEntryState.LoaderPending;
            return true;
        }
    }

    public WorldEntryObservation ObserveDependency(bool isReady)
    {
        lock (this.syncRoot)
        {
            WorldEntryTransition transition = WorldEntryTransition.None;
            switch (this.state)
            {
                case WorldEntryState.EntryRequested when !isReady:
                    this.state = WorldEntryState.EntryWaitingForAudio;
                    transition = WorldEntryTransition.StartedWaiting;
                    break;

                case WorldEntryState.EntryRequested:
                    this.state = WorldEntryState.Idle;
                    break;

                case WorldEntryState.EntryWaitingForAudio when isReady:
                    this.state = WorldEntryState.Idle;
                    transition = WorldEntryTransition.DependencyReady;
                    break;

                case WorldEntryState.LoaderPending when !isReady:
                    this.state = WorldEntryState.LoaderWaitingForAudio;
                    transition = WorldEntryTransition.StartedWaiting;
                    break;

                case WorldEntryState.LoaderPending:
                    this.state = WorldEntryState.Loading;
                    transition = WorldEntryTransition.LoadingStarted;
                    break;

                case WorldEntryState.LoaderWaitingForAudio when isReady:
                    this.state = WorldEntryState.Loading;
                    transition = WorldEntryTransition.DependencyReady;
                    break;
            }

            bool shouldBlock = this.state is WorldEntryState.EntryWaitingForAudio
                or WorldEntryState.LoaderWaitingForAudio;
            return new WorldEntryObservation(
                this.state,
                transition,
                shouldBlock,
                this.state == WorldEntryState.Loading);
        }
    }

    public void CompleteLoading()
    {
        lock (this.syncRoot)
            this.state = WorldEntryState.Idle;
    }

    public void Reset()
        => this.CompleteLoading();

    internal enum WorldEntryState
    {
        Idle,
        EntryRequested,
        EntryWaitingForAudio,
        LoaderPending,
        LoaderWaitingForAudio,
        Loading,
    }

    internal enum WorldEntryTransition
    {
        None,
        StartedWaiting,
        DependencyReady,
        LoadingStarted,
    }

    internal readonly record struct WorldEntryObservation(
        WorldEntryState State,
        WorldEntryTransition Transition,
        bool ShouldBlock,
        bool ShouldAdvanceLoader);
}
