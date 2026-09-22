using System.Text.Json;
using Cindara.Desktop.Accessibility;

namespace Cindara.Desktop.Tests.Accessibility;

public sealed class PresentationPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory, "presentation-test-artifacts", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsAreExplicitAndImmutable()
    {
        var preferences = new PresentationPreferences();
        Assert.Equal(1, preferences.TextScale);
        Assert.False(preferences.HighContrast);
        Assert.False(preferences.ReducedMotion);
        Assert.All(typeof(PresentationPreferences).GetProperties(), property =>
            Assert.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit),
                property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void SupportedPreferencesRoundTripAndCanBeReset(double scale)
    {
        var path = Path.Combine(_directory, "nested", "presentation.json");
        var store = new PresentationSettingsStore(path);
        var preferences = new PresentationPreferences(scale, true, true);
        store.Save(preferences);
        Assert.Equal(preferences, store.Load());

        store.Save(new PresentationPreferences());
        Assert.Equal(new PresentationPreferences(), store.Load());
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(path)!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.1)]
    [InlineData(2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTextScaleIsRejected(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PresentationPreferences(scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PresentationPreferences() with { TextScale = scale });
    }

    [Fact]
    public void MissingFileDefaultsWithoutCreatingAnything()
    {
        Assert.Equal(new PresentationPreferences(),
            new PresentationSettingsStore(Path.Combine(_directory, "presentation.json")).Load());
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{broken")]
    [InlineData("""{"TextScale":2,"HighContrast":false,"ReducedMotion":false}""")]
    [InlineData("""{"TextScale":"1","HighContrast":false,"ReducedMotion":false}""")]
    [InlineData("""{"TextScale":1,"HighContrast":false}""")]
    [InlineData("""{"TextScale":1,"HighContrast":false,"ReducedMotion":false,"Unexpected":true}""")]
    [InlineData("""{"TextScale":1,"TextScale":1.5,"HighContrast":false,"ReducedMotion":false}""")]
    public void CorruptSettingsAreNotSilentlyReset(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "presentation.json");
        File.WriteAllText(path, json);
        Assert.Throws<JsonException>(() => new PresentationSettingsStore(path).Load());
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void UnreadableFileIsNotTreatedAsMissing()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "presentation.json");
        using var locked = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => new PresentationSettingsStore(path).Load());
    }

    [Fact]
    public void SaveFailureIsExplicitAndCleansPendingFiles()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "presentation.json");
        Directory.CreateDirectory(path);
        var exception = Record.Exception(() => new PresentationSettingsStore(path).Save(new PresentationPreferences()));
        Assert.True(exception is IOException or UnauthorizedAccessException);
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
