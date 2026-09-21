using Cindara.Desktop.Authentication;

namespace Cindara.Desktop.Tests.Authentication;

public sealed class DeviceIdentityProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"cindara-device-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ConcurrentCreationReturnsOneStableIdentity()
    {
        var path = Path.Combine(_directory, "device-id");

        var identities = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => DeviceIdentityProvider.GetOrCreate(path))));

        Assert.Single(identities.Distinct(StringComparer.Ordinal));
        Assert.Equal(identities[0], File.ReadAllText(path));
    }

    [Fact]
    public void InvalidIdentityIsAtomicallyReplaced()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "device-id");
        File.WriteAllText(path, "invalid");

        var identity = DeviceIdentityProvider.GetOrCreate(path);

        Assert.True(Guid.TryParse(identity, out _));
        Assert.Equal(identity, File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
