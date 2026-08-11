namespace GameReminders.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string InstanceName = @"Local\GameReminders.SingleInstance.v1";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private int _disposed;

    private SingleInstanceCoordinator(
        Mutex mutex,
        EventWaitHandle activationEvent,
        Action activationRequested)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut && Volatile.Read(ref _disposed) == 0)
                {
                    activationRequested();
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public static SingleInstanceCoordinator? TryStart(
        bool activateExisting,
        Action activationRequested) =>
        TryStart(InstanceName, activateExisting, activationRequested);

    internal static SingleInstanceCoordinator? TryStart(
        string instanceName,
        bool activateExisting,
        Action activationRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentNullException.ThrowIfNull(activationRequested);

        var mutex = new Mutex(initiallyOwned: false, instanceName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                mutex.Dispose();
                if (activateExisting)
                {
                    SignalExisting(instanceName);
                }

                return null;
            }

            var activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivationEventName(instanceName));
            return new SingleInstanceCoordinator(mutex, activationEvent, activationRequested);
        }
        catch
        {
            if (ownsMutex)
            {
                mutex.ReleaseMutex();
            }

            mutex.Dispose();
            throw;
        }
    }

    private static void SignalExisting(string instanceName)
    {
        const int attempts = 10;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName(instanceName));
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) when (attempt < attempts - 1)
            {
                Thread.Sleep(20);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return;
            }
        }
    }

    private static string ActivationEventName(string instanceName) => $"{instanceName}.Activate";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _activationRegistration?.Unregister(null);
        _activationRegistration = null;
        _activationEvent.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
