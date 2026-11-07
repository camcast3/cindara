using System.Text.Json;
using Cindara.Desktop.Input;

namespace Cindara.Desktop.Tests.Input;

public sealed class ControllerProfileStoreTests
{
    [Theory]
    [InlineData("{\"Profiles\":null}")]
    [InlineData("{\"Profiles\":{\"Xbox:TEST\":null}}")]
    public void NullProfileDataIsRejectedAsJsonError(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<JsonException>(() => new ControllerProfileStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileReturnsNoProfiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cindara-controller-{Guid.NewGuid():N}", "profiles.json");
        var store = new ControllerProfileStore(path);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void SaveRoundTripsValidatedProfiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-controller-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "profiles.json");
            var store = new ControllerProfileStore(path);
            var profile = new ControllerProfile
            {
                DeadZone = 12_000,
                InvertVerticalAxis = true,
                Accept = ControllerButton.West,
                Back = ControllerButton.North,
            };

            store.Save(new Dictionary<string, ControllerProfile> { ["Xbox:TEST"] = profile });

            var loaded = Assert.Single(store.Load());
            Assert.Equal("Xbox:TEST", loaded.Key);
            Assert.Equal(profile, loaded.Value);
            Assert.DoesNotContain(".pending", string.Join('|', Directory.GetFiles(directory)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsUnknownOrInvalidSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-controller-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "profiles.json");
            File.WriteAllText(
                path,
                """
                {
                  "Profiles": {
                    "Xbox:TEST": {
                      "DeadZone": 1,
                      "InvertHorizontalAxis": false,
                      "InvertVerticalAxis": false,
                      "NavigateUp": "DPadUp",
                      "NavigateDown": "DPadDown",
                      "NavigateLeft": "DPadLeft",
                      "NavigateRight": "DPadRight",
                      "Accept": "South",
                      "Back": "East",
                      "Menu": "Start"
                    }
                  }
                }
                """);

            Assert.Throws<JsonException>(() => new ControllerProfileStore(path).Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
