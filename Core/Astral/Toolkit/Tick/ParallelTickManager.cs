using Astral.Diagnostics;
using Astral.Logging;
using Astral.Tools;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Astral.Tick;

public readonly struct ParallelTickHandle
{
    internal readonly long Id;
    internal ParallelTickHandle(long Id) { this.Id = Id; }
    public bool IsValid() { return Id != 0; }
}

public static class ParallelTickManager
{
    struct TickActionWrapper
    {
        public int Group;
        public long HzTicks;
        public long DeadlineTicks;
        public WeakAction? Action;
        //public TickActionWrapper(int Group, long DeltaTicks, long TicksNow, WeakAction Action)
        //{
        //	this.Group = Group;
        //	this.HzTicks = DeltaTicks;
        //	this.DeadlineTicks = TicksNow + DeltaTicks;
        //	this.Action = Action;
        //}
        public TickActionWrapper(TickRegisterForm Form, long TicksNow)
        {
            this.Group = Form.Group;
            this.HzTicks = Form.HzTicks;
            this.DeadlineTicks = TicksNow + HzTicks;
            this.Action = Form.Action;
        }
        public TickActionWrapper(ParallelTickRegisterForm Form, long TicksNow)
        {
            this.Group = Form.Group;
            this.HzTicks = Form.HzTicks;
            this.DeadlineTicks = TicksNow + HzTicks;
            this.Action = Form.Action;
        }
    }
    struct Worker
    {
        public Thread Thread;
        public int NumActions;
        public TickActionWrapper[] ActionWrappers;
    }

    readonly struct WorkerActionIndex
    {
        public readonly int WorkerIndex;
        public readonly int ActionIndex;
        public WorkerActionIndex(int Worker, int Action) { WorkerIndex = Worker; ActionIndex = Action; }
    }
    readonly struct TickRegisterForm
    {
        public readonly int WorkerIndex;
        public readonly long HzTicks;
        public readonly int Group;
        public readonly WeakAction Action;

        public TickRegisterForm(int Index, double Hz, int Grp, WeakAction Act)
        {
            WorkerIndex = Index;
            HzTicks = (long)(Context.ClockFrequency / Hz);
            Group = Grp;
            Action = Act;
        }
    }

    readonly struct ParallelTickRegisterForm
    {
        public readonly long HzTicks;
        public readonly int Group;
        public readonly WeakAction Action;

        public ParallelTickRegisterForm(double Hz, int Grp, WeakAction Act)
        {
            HzTicks = (long)(Context.ClockFrequency / Hz);
            Group = Grp;
            Action = Act;
        }
    }

    readonly struct TickRemovalForm
    {
        public readonly int WorkerIndex;
        public readonly WeakAction Action;

        public TickRemovalForm(int Index, WeakAction Act)
        {
            WorkerIndex = Index;
            Action = Act;
        }
    }

    private static Thread? TickThread;
    private static CancellationTokenSource? Cts;

    private static readonly int PrivateWorkerCount;
    private static readonly int MasterProcessorIndex;
    private static readonly int WorkerProcessorStartIndex;
    public static int WorkerCount { get => PrivateWorkerCount; }

    public static object Lock { get; private set; } = new();

    static Worker[] Workers;

    static public int PendingChangesCount { get; private set; } = 0;

    private static int ParallelCount;
    private static TickActionWrapper[] ParallelActions = new TickActionWrapper[1024];
    private static int ThisTickParallelCount;
    private static TickActionWrapper[] ThisTickParallelActions = new TickActionWrapper[1024];

    public static int TickRate { get; private set; } = 1000;
    private static double TickIntervalTicks = Context.ClockFrequency / (double)TickRate;

    static Barrier StartBarrier;
    static Barrier EndBarrier;
    private static int FinishedCount = 0;
    private static readonly ManualResetEventSlim StartSignal = new ManualResetEventSlim(false);
    private static readonly ManualResetEventSlim AllFinishedSignal = new ManualResetEventSlim(false);

    public static long NumTicks { get; private set; }
    public static long LastTick { get; private set; }

    public static long PrivateThisTickTicks;
    public static long ThisTickTicks { get => Volatile.Read(ref PrivateThisTickTicks); }

    public static long PrivateCurrentDeltaTicks;
    public static long CurrentDeltaTicks { get => Volatile.Read(ref PrivateCurrentDeltaTicks); }

    private static readonly Dictionary<long, WorkerActionIndex> IdToIndex = new();
    private static readonly List<TickRegisterForm> ToAdd = new();
    private static readonly List<long> ToRemoveIds = new();

    private static readonly Dictionary<long, int> ParallelIdToIndex = new();
    private static readonly List<ParallelTickRegisterForm> ParallelToAdd = new();
    private static readonly List<long> ParallelToRemoveIds = new();


    public static Action? OnMasterInit { get; set; }

    public delegate void WorkerInitHandler(int WorkerIndex);
    public static WorkerInitHandler? OnWorkerInit { get; set; }

    public static Action<string>? OnWarn { get; set; }
    public static Action<string>? OnError { get; set; }
    public static Action<string>? OnFault { get; set; }

    [ThreadStatic]
    static int IWorkerIndex = -1;

    static public int WorkerIndex { get => IWorkerIndex; }

    static bool bInitialized = false;

    static ParallelTickManager()
    {
        if (Context.IsSmtEnabled)
        {
            if (Context.LogicalProcessorCount < 4) // If less than 2 cores
            {
                PrivateWorkerCount = 1;
                MasterProcessorIndex = 1;
                WorkerProcessorStartIndex = 1;
            }
            else if (Context.LogicalProcessorCount < 6) // If less than 3 cores
            {
                PrivateWorkerCount = 1;
                MasterProcessorIndex = 3;
                WorkerProcessorStartIndex = 3;
            }
            else
            {
                PrivateWorkerCount = Context.LogicalProcessorCount - 4;
                MasterProcessorIndex = 3;
                WorkerProcessorStartIndex = 4;
            }
        }
        else
        {
            if (Context.LogicalProcessorCount < 2) // If less than 2 cores
            {
                PrivateWorkerCount = 1;
                MasterProcessorIndex = 0;
                WorkerProcessorStartIndex = 0;
            }
            else if (Context.LogicalProcessorCount < 3) // If less than 3 cores
            {
                PrivateWorkerCount = 1;
                MasterProcessorIndex = 1;
                WorkerProcessorStartIndex = 1;
            }
            else
            {
                PrivateWorkerCount = Context.LogicalProcessorCount - 2;
                MasterProcessorIndex = 1;
                WorkerProcessorStartIndex = 2;
            }
        }

        StartBarrier = new Barrier(PrivateWorkerCount + 1);
        EndBarrier = new Barrier(PrivateWorkerCount + 1);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int sched_setaffinity(int pid, IntPtr cpusetsize, ref ulong cpuset);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    public static void SetThreadAffinity(int coreId)
    {
        // 1. Hard-bind to the specific CPU core
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ulong mask = 1UL << coreId;
            sched_setaffinity(0, (IntPtr)128, ref mask);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IntPtr mask = new IntPtr(1L << coreId);
            int managedId = GetCurrentThreadId();

            foreach (ProcessThread pt in Process.GetCurrentProcess().Threads)
            {
                if (pt.Id == managedId)
                {
                    pt.ProcessorAffinity = mask;
                    break;
                }
            }
        }

        // 2. Set Highest Priority
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
    }

    public static void Initialize()
    {
        if (bInitialized)
        {
            return;
        }
        bInitialized = true;
        LastTick = Context.Ticks;

        Workers = new Worker[PrivateWorkerCount];

        for (int i = 0; i < PrivateWorkerCount; i++)
        {
            int WorkerIndex = i;
            ref var Info = ref Workers[i];

            Info.ActionWrappers = new TickActionWrapper[WorkerCount];
            Info.Thread = new Thread(() => WorkerLoop(WorkerIndex)) { IsBackground = true, Name = $"PTM-Worker-{WorkerIndex}" };
            Info.Thread.Start();
        }

        Cts = new CancellationTokenSource();

        TickThread = new Thread(() => Tick(Cts.Token))
        {
            Name = "PTM-Master",
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
        TickThread.Start();
    }

    internal static void ResetForTests()
    {
        lock (Lock)
        {
            var Errors = new List<string>();

            // ---------- 1. Pending Adds / Removes ----------
            if (ToAdd.Count > 0)
                Errors.Add($"ToAdd contains {ToAdd.Count} uncommitted entries.");

            if (ToRemoveIds.Count > 0)
                Errors.Add($"ToRemoveIds contains {ToRemoveIds.Count} uncommitted ids.");

            // ---------- 2. Active Actions ----------
            for (int i = 0; i < Workers.Length; i++)
            {
                ref var Worker = ref Workers[i];
                var Count = Workers[i].NumActions;
                for (int i2 = 0; i2 < Count; i2++)
                {
                    var Wrpr = Worker.ActionWrappers[i2];
                    if (Wrpr.Action == null)
                    {
                        Errors.Add($"Actions[{i2}] is null but inside active area (Count={Count}).");
                        continue;
                    }

                    if (!IdToIndex.TryGetValue(Wrpr.Action.Id, out var Mapping) || Mapping.WorkerIndex != i2)
                        Errors.Add($"IdToIndex mismatch for Action Id {Wrpr.Action.Id}, expected index {i2}.");
                }
            }


            // ---------- 3. Check for extra entries in IdToIndex ----------
            foreach (var kv in IdToIndex)
            {
                long id = kv.Key;
                var Mapping = kv.Value;

                ref var Worker = ref Workers[Mapping.WorkerIndex];

                if (Mapping.ActionIndex >= Worker.NumActions)
                    Errors.Add($"IdToIndex[{id}] points to index {Mapping} outside active range (Count={Worker.NumActions}).");

                var act = Mapping.ActionIndex < Worker.NumActions ? Worker.ActionWrappers[Mapping.ActionIndex].Action : null;
                if (act == null)
                    Errors.Add($"IdToIndex[{id}] points to null action at index {Mapping}.");
                else if (act.Id != id)
                    Errors.Add($"IdToIndex[{id}] – slot contains different action id {act.Id}.");
            }

            // ---------- 4. Throw if errors ----------
            if (Errors.Count > 0)
                throw new InvalidOperationException(
                    "ParallelTickManager static state is not clean:\n" +
                    string.Join("\n", Errors)
                );

            // ---------- 5. Clear everything ----------
            //Array.Clear(Actions, 0, Actions.Length);
            //Count = 0;
            Array.Clear(Workers, 0, Workers.Length);
            ToAdd.Clear();
            ToRemoveIds.Clear();
            IdToIndex.Clear();

            // Optional: reset barriers and handlers
            //StartBarrier = new Barrier(WorkerCount + 1);
            //EndBarrier = new Barrier(WorkerCount + 1);

            //OnWarn = null;
            //OnError = null;
            //OnFault = null;
        }
    }


    public static void SetTickRate(int TickHz)
    {
        if (TickHz < 1) TickHz = 1;
        else if (TickHz > 1000) TickHz = 1000;

        TickRate = TickHz;
        TickIntervalTicks = Context.ClockFrequency / (double)TickRate;
    }


    public static long Register(Action Action, double Hz = 60, int Group = 60, int WorkerIndex = -1)
    {
        var Weak = new WeakAction(Action);
        var Id = Weak.Id;
        if (Hz < 0.0)
        {
            return -1;
        }
        lock (Lock)
        {
#if !RELEASE
            foreach (var Elem in ToAdd)
            {
                if (Elem.Equals(Action))
                {
                    OnWarn?.Invoke($"Registering an already queued method. Obj: [{Action.Target} Method: [{Action.Method.Name}]");
                }
            }
            PendingChangesCount++;
#endif
            ToAdd.Add(new TickRegisterForm(WorkerIndex, Hz, Group, Weak));
        }
        return Id;
    }


    public static void Unregister(long Id)
    {
        lock (Lock)
        {
            // 1. Remove from pending adds
            int ToAddIndex = ToAdd.FindIndex(f => f.Action.Id == Id);
            if (ToAddIndex >= 0)
            {
                ToAdd.RemoveAt(ToAddIndex);
                Guard.Assert(!IdToIndex.ContainsKey(Id));
                return;
            }

            if (ToRemoveIds.Contains(Id))
            {
                OnWarn?.Invoke($"Tried to unregister tick [{Id}] but it's already marked for removal.");
                return;
            }

            // 2. Mark for removal if committed
            if (IdToIndex.ContainsKey(Id))
            {
                ToRemoveIds.Add(Id);
                return;
            }

            // 3. If not in ToAdd or IdToIndex, warn
            OnWarn?.Invoke($"Tried to unregister tick [{Id}] but it was not registered.");
        }
    }
    public static void Unregister<T>(long Id)
    {
        lock (Lock)
        {
            // 1. Remove from pending adds
            int ToAddIndex = ToAdd.FindIndex(f => f.Action.Id == Id);
            if (ToAddIndex >= 0)
            {
                ToAdd.RemoveAt(ToAddIndex);
                Guard.Assert(!IdToIndex.ContainsKey(Id), $"{typeof(T).Name}");
                return;
            }

            if (ToRemoveIds.Contains(Id))
            {
                OnWarn?.Invoke($"{typeof(T).Name} tried to unregister tick [{Id}] but it's already marked for removal.");
                return;
            }

            // 2. Mark for removal if committed
            if (IdToIndex.ContainsKey(Id))
            {
                ToRemoveIds.Add(Id);
                return;
            }

            // 3. If not in ToAdd or IdToIndex, warn
            OnWarn?.Invoke($"{typeof(T).Name} tried to unregister tick [{Id}] but it was not registered.");
        }
    }




    /// <summary>
    /// This action will be called on every availble worker every tick. <br/>
    /// Example: if there are 10 threads, The action will be called 10 times every tick.
    /// </summary>
    /// <param name="Action"></param>
    /// <returns></returns>
    public static ParallelTickHandle RegisterParallelTick(Action Action, double Hz = 60, int Group = 60)
    {
        var Weak = new WeakAction(Action);
        var Id = Weak.Id;
        lock (Lock)
        {
#if !RELEASE
            foreach (var Elem in ParallelToAdd)
            {
                if (Elem.Equals(Action))
                {
                    OnWarn?.Invoke($"Registering an already queued method. Obj: [{Action.Target} Method: [{Action.Method.Name}]");
                }
            }
            PendingChangesCount++;
#endif
            ParallelToAdd.Add(new ParallelTickRegisterForm(Hz, Group, Weak));
        }
        return new ParallelTickHandle(Id);
    }


    public static void UnregisterParallelTick(ParallelTickHandle Handle)
    {
        lock (Lock)
        {
            var Id = Handle.Id;
            // 1. Remove from pending adds
            int ToAddPrarallelIndex = ParallelToAdd.FindIndex(w => w.Action.Id == Id);
            if (ToAddPrarallelIndex >= 0)
            {
                ParallelToAdd.RemoveAt(ToAddPrarallelIndex);
                Guard.Assert(!ParallelIdToIndex.ContainsKey(Id));
                return;
            }

            if (ParallelToRemoveIds.Contains(Id))
            {
                OnWarn?.Invoke($"Tried to unregister parallel tick [{Id}] but it's already marked for removal.");
                return;
            }

            // 2. Mark for removal if committed
            if (ParallelIdToIndex.ContainsKey(Id))
            {
                ParallelToRemoveIds.Add(Id);
                return;
            }

            // 3. If not in ToAdd or IdToIndex, warn
            OnWarn?.Invoke($"Tried to unregister parallel tick [{Id}] but it was not registered.");
        }
    }

    static int ThreadIndexCounter = 0;
    static void CommitPendingChanges()
    {
        lock (Lock)
        {
            foreach (var Form in ToAdd)
            {
                var Index = Form.WorkerIndex;
                if (Index < 0)
                {
                    Index = ThreadIndexCounter++;
                    if (ThreadIndexCounter >= PrivateWorkerCount) ThreadIndexCounter = 0;
                }
                ref var Worker = ref Workers[Index];

                if (Worker.NumActions >= Worker.ActionWrappers.Length)
                {
                    Array.Resize(ref Worker.ActionWrappers, Worker.ActionWrappers.Length * 2);
                }

                ref var Wrapper = ref Worker.ActionWrappers[Worker.NumActions];
                Wrapper = new TickActionWrapper(Form, PrivateThisTickTicks);

                IdToIndex[Form.Action.Id] = new WorkerActionIndex(Index, Worker.NumActions);
                Worker.NumActions++;

            }
            ToAdd.Clear();

            foreach (long Id in ToRemoveIds)
            {
                if (!IdToIndex.Remove(Id, out var Mapping))
                {
                    var Msg = $"Invariant violation: tried to remove tick [{Id}] but it was not registered.";
                    if (OnFault == null)
                        Guard.Fail(Msg);
                    else
                    {
                        try { OnFault.Invoke(Msg); }
                        catch (Exception handlerEx)
                        {
                            Guard.Fail($"Tick manager fault handler threw an exception.\nOriginal: {Msg}\nHandlerEx: {handlerEx}");
                        }
                    }
                    continue;
                }

                ref var Worker = ref Workers[Mapping.WorkerIndex];

                while (true)
                {
                    var LastIndex = Worker.NumActions - 1;
                    var LastAction = Worker.ActionWrappers[LastIndex];

                    if (LastAction.Action != null || LastIndex == Mapping.ActionIndex)
                    {
                        if (Mapping.ActionIndex != LastIndex && LastAction.Action != null)
                        {
                            Worker.ActionWrappers[Mapping.ActionIndex] = LastAction;
                            IdToIndex[LastAction.Action.Id] = Mapping;
                        }

                        Worker.ActionWrappers[LastIndex].Action = null;
                        Worker.NumActions--;
                        break;
                    }
                    else
                    {
                        var msg = $"Invariant violation: LastAction at index {LastIndex} is null while removing Id {Id}";
                        if (OnFault == null)
                        {
                            Guard.Fail(msg);
                        }
                        else
                        {
                            try { OnFault.Invoke(msg); }
                            catch (Exception handlerEx)
                            {
                                Guard.Fail($"Tick manager fault handler threw an exception.\nOriginal: {msg}\nHandlerEx: {handlerEx}");
                            }
                        }

                        Worker.ActionWrappers[LastIndex].Action = null;
                        Worker.NumActions--;
                    }
                }
            }
            ToRemoveIds.Clear();


            foreach (var ParallelForm in ParallelToAdd)
            {
                if (ParallelCount >= ParallelActions.Length)
                {
                    Array.Resize(ref ParallelActions, ParallelActions.Length * 2);
                    Array.Resize(ref ThisTickParallelActions, ParallelActions.Length * 2);
                }

                ParallelActions[ParallelCount] = new TickActionWrapper(ParallelForm, PrivateThisTickTicks);
                ParallelIdToIndex[ParallelForm.Action.Id] = ParallelCount;
                ParallelCount++;
            }
            ParallelToAdd.Clear();

            foreach (long ParallelId in ParallelToRemoveIds)
            {
                if (!ParallelIdToIndex.Remove(ParallelId, out int ParallelIndex))
                {
                    var Msg = $"Invariant violation: tried to remove parallel tick [{ParallelId}] but it was not registered.";
                    if (OnFault == null)
                        Guard.Fail(Msg);
                    else
                    {
                        try { OnFault.Invoke(Msg); }
                        catch (Exception handlerEx)
                        {
                            Guard.Fail($"Tick manager fault handler threw an exception.\nOriginal: {Msg}\nHandlerEx: {handlerEx}");
                        }
                    }
                    continue;
                }

                while (true)
                {
                    var LastParallelIndex = ParallelCount - 1;
                    var LastParallelAction = ParallelActions[LastParallelIndex];

                    if (LastParallelAction.Action != null || LastParallelIndex == ParallelIndex)
                    {
                        if (ParallelIndex != LastParallelIndex && LastParallelAction.Action != null)
                        {
                            ParallelActions[ParallelIndex] = LastParallelAction;
                            ParallelIdToIndex[LastParallelAction.Action.Id] = ParallelIndex;
                        }

                        ParallelActions[LastParallelIndex].Action = null;
                        ParallelCount--;
                        break;
                    }
                    else
                    {
                        var msg = $"Invariant violation: LastParallelAction at index {LastParallelIndex} is null while removing Id {ParallelId}";
                        if (OnFault == null)
                        {
                            Guard.Fail(msg);
                        }
                        else
                        {
                            try { OnFault.Invoke(msg); }
                            catch (Exception handlerEx)
                            {
                                Guard.Fail($"Tick manager fault handler threw an exception.\nOriginal: {msg}\nHandlerEx: {handlerEx}");
                            }
                        }

                        ParallelActions[LastParallelIndex].Action = null;
                        ParallelCount--;
                    }
                }
            }
            ParallelToRemoveIds.Clear();
        }
    }

    private static void Wait(long TargetTicks)
    {
        int Stride = 0;
        while (true)
        {
            // Check every 20 iterations (Good balance for PPS)
            if (Stride++ % 100 == 0)
            {
                long Current = Context.Ticks;
                if (Current >= TargetTicks) break;

                long RemainingTicks = TargetTicks - Current;
                double RemainingMs = (RemainingTicks * 1000.0) / Context.ClockFrequency;

                // ONLY Sleep if we have massive room (> 16ms)
                // Anything less than this, and the OS scheduler will blow your deadline.
                if (RemainingMs > 16.0)
                {
                    Thread.Sleep(1);
                }
                else if (RemainingMs > 2.0)
                {
                    // Yield is better for the 2ms-16ms range
                    Thread.Yield();
                }
            }
            else
            {
                // Final micro-precision spin
                Thread.SpinWait(15);
            }

            if (Stride % 1000 == 0 && Context.Clock.IsPaused)
            {
                Thread.Sleep(10);
                break;
            }
        }
    }

    private static void Tick(CancellationToken Token)
    {
        try
        {
            SetThreadAffinity(MasterProcessorIndex);

            try { OnMasterInit?.Invoke(); }
            catch { }

            long MaxExpectedTicks = (long)(TickIntervalTicks * 100); // tolerance threshold

            while (!Token.IsCancellationRequested)
            {
                CommitPendingChanges();
                Volatile.Write(ref PrivateThisTickTicks, Context.Ticks);
                Volatile.Write(ref PrivateCurrentDeltaTicks, PrivateThisTickTicks - LastTick);

                if (PrivateCurrentDeltaTicks > MaxExpectedTicks)
                {
                    double OverdueMs = PrivateCurrentDeltaTicks * Context.Clock.TicksToMs;
                    AstralLoggingCenter.Log("TickManager", ELogLevel.Warning,
                        $"Tick execution exceeded schedule by {OverdueMs:F2}ms");
                }

                ThisTickParallelCount = 0;

                for (int i = 0; i < ParallelCount; i++)
                {
                    ref var ParallelWrapper = ref ParallelActions[i];

                    if (ParallelWrapper.DeadlineTicks >= PrivateThisTickTicks)
                    {
                        continue;
                    }
                    ParallelWrapper.DeadlineTicks = PrivateThisTickTicks + ParallelWrapper.HzTicks;
                    ThisTickParallelActions[ThisTickParallelCount++] = ParallelWrapper;
                }

                

                StartBarrier.SignalAndWait();
                EndBarrier.SignalAndWait();
                //// 1. Reset synchronization state
                //Interlocked.Exchange(ref FinishedCount, 0);
                //AllFinishedSignal.Reset();
                //
                //// 2. Release all workers simultaneously
                //StartSignal.Set();
                //
                //// 3. Wait for workers to report back
                //// ManualResetEventSlim.Wait() is much lighter than Barrier
                //AllFinishedSignal.Wait(Token);
                //
                //StartSignal.Reset(); // Prepare for next tick

                LastTick = PrivateThisTickTicks;

                NumTicks++;

                // Reschedule next tick (drift compensated)
                long TargetTicks = PrivateThisTickTicks + (long)TickIntervalTicks;

                Wait(TargetTicks);
            }
        }
        catch (Exception Ex)
        {
            if (OnFault == null)
            {
                Guard.Fail($"ParallelTickManager: Tick thread exception: {Ex}");
            }
            else
            {
                try
                {
                    OnFault.Invoke($"Tick thread exception: {Ex}");
                }
                catch (Exception HandlerEx)
                {
                    Guard.Fail($"ParallelTickManager: Tick thread threw an exception and the fault handler also threw." +
                        $"\nThreadEx: {Ex}\nHandlerEx: {HandlerEx}");
                }
            }
        }
    }

    private static void WorkerLoop(int WorkerIndex)
    {
        if (Context.IsSmtEnabled)
        {
            //SetThreadAffinity(WorkerProcessorStartIndex + WorkerIndex * 2);
            SetThreadAffinity(WorkerProcessorStartIndex + WorkerIndex);
        }
        else
        {
            SetThreadAffinity(WorkerProcessorStartIndex + WorkerIndex);
        }

        IWorkerIndex = WorkerIndex;

        try { OnWorkerInit?.Invoke(WorkerIndex); }
        catch { }

        while (true)
        {
            try
            {
                //StartSignal.Wait();
                StartBarrier.SignalAndWait();
                for (int i = 0; i < ThisTickParallelCount; i++)
                {
                    ref var ParallelWrapper = ref ThisTickParallelActions[i];

                    if (!ParallelWrapper.Action!.IsValid)
                    {
                        OnWarn?.Invoke($"An invalid parallel action found while ticking, Action Id: {ParallelWrapper.Action.Id}");
                        Unregister(ParallelWrapper.Action.Id);
                    }
                    else
                    {
                        try
                        {
                            ParallelWrapper.Action.Invoke();
                        }
                        catch (Exception Ex)
                        {
                            Unregister(ParallelWrapper.Action.Id);
                            OnError?.Invoke($"ParallelTick for [{ParallelWrapper.Action.Target?.ToString()}.{ParallelWrapper.Action.Method.Name}] threw an exception and has been marked for removal. Ex: {Ex}");
                        }
                    }
                }

                ref var Worker = ref Workers[WorkerIndex];

                for (int i = 0; i < Worker.NumActions; i++)
                {
                    ref var Wrapper = ref Worker.ActionWrappers[i];

                    if (Wrapper.DeadlineTicks >= PrivateThisTickTicks)
                    {
                        continue;
                    }

                    Wrapper.DeadlineTicks = PrivateThisTickTicks + Wrapper.HzTicks;

                    if (!Wrapper.Action!.IsValid)
                    {
                        OnWarn?.Invoke($"An invalid action found while ticking, Action Id: {Wrapper.Action.Id}");
                        Unregister(Wrapper.Action.Id);
                    }
                    else
                    {
                        try
                        {
                            Wrapper.Action.Invoke();
                        }
                        catch (Exception Ex)
                        {
                            Unregister(Wrapper.Action.Id);
                            OnError?.Invoke($"Tick for [{Wrapper.Action.Target?.ToString()}.{Wrapper.Action.Method.Name}] threw an exception and has been marked for removal. Ex: {Ex}");
                        }
                    }
                }

                // Signal completion
                //if (Interlocked.Increment(ref FinishedCount) == PrivateWorkerCount)
                //{
                //    AllFinishedSignal.Set(); // Last thread through the door wakes the Master
                //}
                EndBarrier.SignalAndWait();
            }
            catch (Exception Ex)
            {
                if (OnFault == null)
                {
                    Guard.Fail($"ParallelTickManager - WorkerLoop: Worker exception: {Ex}");
                }
                else
                {
                    try
                    {
                        OnFault.Invoke($"WorkerLoop: Worker exception: {Ex}");
                    }
                    catch (Exception HandlerEx)
                    {
                        Guard.Fail($"ParallelTickManager - WorkerLoop: Worker threw an exception and the fault handler also threw." +
                            $"\nWorkerEx: {Ex}\nHandlerEx: {HandlerEx}");
                    }
                }
            }
        }
    }
}
