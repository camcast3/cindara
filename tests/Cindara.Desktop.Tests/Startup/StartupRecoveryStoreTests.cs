using Cindara.Desktop.Startup;

namespace Cindara.Desktop.Tests.Startup;

public sealed class StartupRecoveryStoreTests
{
    [Fact]
    public void RepeatedIncompleteStartsEnableSafeMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-startup-{Guid.NewGuid():N}");
        try
        {
            var store = new StartupRecoveryStore(Path.Combine(directory, "recovery.json"));

            Assert.False(store.Begin().UseSafeMode);
            var firstFailure = store.Begin();
            Assert.False(firstFailure.UseSafeMode);
            Assert.Equal(1, firstFailure.ConsecutiveFailedStarts);
            var secondFailure = store.Begin();
            Assert.True(secondFailure.UseSafeMode);
            Assert.Equal(2, secondFailure.ConsecutiveFailedStarts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CompletedStartupResetsFailureCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-startup-{Guid.NewGuid():N}");
        try
        {
            var store = new StartupRecoveryStore(Path.Combine(directory, "recovery.json"));
            store.Begin();
            store.Begin();
            store.Complete();

            var nextStart = store.Begin();

            Assert.False(nextStart.UseSafeMode);
            Assert.Equal(0, nextStart.ConsecutiveFailedStarts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ForcedSafeModeDoesNotRequirePriorFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-startup-{Guid.NewGuid():N}");
        try
        {
            var store = new StartupRecoveryStore(Path.Combine(directory, "recovery.json"));

            var decision = store.Begin(forceSafeMode: true);

            Assert.True(decision.UseSafeMode);
            Assert.Equal(0, decision.ConsecutiveFailedStarts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CorruptStateFailsSafeWithVisibleError()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-startup-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "recovery.json");
            File.WriteAllText(path, "{not-json");

            var decision = new StartupRecoveryStore(path).Begin();

            Assert.True(decision.UseSafeMode);
            Assert.NotNull(decision.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
