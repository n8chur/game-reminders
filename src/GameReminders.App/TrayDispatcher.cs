namespace GameReminders.App;

internal static class TrayDispatcher
{
    public static bool ShouldDispatch(bool shutdownStarted, bool shutdownFinished) =>
        !shutdownStarted && !shutdownFinished;
}
