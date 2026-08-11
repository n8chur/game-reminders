namespace GameReminders.App.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void ManualSecondLaunchSignalsOwnerAndDoesNotBecomeAnotherOwner()
    {
        var instanceName = UniqueInstanceName();
        using var activationRequested = new ManualResetEventSlim();
        using var owner = SingleInstanceCoordinator.TryStart(
            instanceName,
            activateExisting: true,
            activationRequested.Set);

        var duplicateStarted = TryStartDuplicate(instanceName, activateExisting: true);

        Assert.NotNull(owner);
        Assert.False(duplicateStarted);
        Assert.True(activationRequested.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void HiddenLoginSecondLaunchExitsWithoutShowingOwner()
    {
        var instanceName = UniqueInstanceName();
        using var activationRequested = new ManualResetEventSlim();
        using var owner = SingleInstanceCoordinator.TryStart(
            instanceName,
            activateExisting: true,
            activationRequested.Set);

        var duplicateStarted = TryStartDuplicate(instanceName, activateExisting: false);

        Assert.NotNull(owner);
        Assert.False(duplicateStarted);
        Assert.False(activationRequested.Wait(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void InstanceCanStartAfterOwnerExits()
    {
        var instanceName = UniqueInstanceName();
        var owner = SingleInstanceCoordinator.TryStart(instanceName, activateExisting: true, () => { })
            ?? throw new InvalidOperationException("The first instance did not acquire ownership.");
        owner.Dispose();

        using var replacement = SingleInstanceCoordinator.TryStart(
            instanceName,
            activateExisting: true,
            () => { });
        Assert.NotNull(replacement);
    }

    private static string UniqueInstanceName() =>
        $@"Local\GameReminders.Tests.{Guid.NewGuid():N}";

    private static bool TryStartDuplicate(string instanceName, bool activateExisting) =>
        Task.Run(() =>
        {
            using var duplicate = SingleInstanceCoordinator.TryStart(
                instanceName,
                activateExisting,
                () => { });
            return duplicate is not null;
        }).GetAwaiter().GetResult();
}
