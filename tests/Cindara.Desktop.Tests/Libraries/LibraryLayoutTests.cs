using System.Text.Json;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Libraries;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.Tests.ViewModels;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.Libraries;

[Collection(LocalizationTestGroup.Name)]
public sealed class LibraryLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"cindara-layout-{Guid.NewGuid():N}");
    private static readonly SessionProfile Profile = new(
        new ServerIdentity("server", new Uri("https://media.example/jellyfin/"), "Media", "10.11", "Linux"),
        "viewer", "Viewer");
    private static readonly MediaLibrary[] Libraries =
    [
        new("anime", "Anime", "tvshows"), new("people", "People", "people"),
        new("movies", "Movies", "movies"), new("tv", "TV", "tvshows"),
        new("collections", "Collections", "boxsets"),
    ];

    [Fact]
    public void DefaultsMatchTvMoviesAnimeAndDraftsDoNotChangeTheAppliedLayout()
    {
        var model = Model();
        Assert.Equal(["tv", "movies", "anime"], model.SidebarIds);
        Assert.Equal(["tv", "movies", "anime"], model.HomeIds);
        var draft = model.CreateDraft(LibraryLayoutSurface.Sidebar);
        Assert.Equal(["tv", "movies", "anime"], draft.Select(choice => choice.Library.Id));
        draft[0].Included = false;
        draft.Reverse();
        Assert.Equal(["tv", "movies", "anime"], model.SidebarIds);
        Assert.Null(new LibraryLayoutSettingsStore(_directory).Load(Profile));
    }

    [Fact]
    public void SidebarAndHomeVisibilityAndOrderPersistIndependently()
    {
        var model = Model();
        var sidebar = model.CreateDraft(LibraryLayoutSurface.Sidebar);
        sidebar.Single(choice => choice.Library.Id == "movies").Included = false;
        var anime = sidebar.Single(choice => choice.Library.Id == "anime");
        sidebar.Remove(anime);
        sidebar.Insert(0, anime);
        Assert.True(model.Save(LibraryLayoutSurface.Sidebar, sidebar));
        Assert.Equal(["anime", "tv"], model.SidebarIds);
        Assert.Equal(["tv", "movies", "anime"], model.HomeIds);

        var home = model.CreateDraft(LibraryLayoutSurface.Home);
        foreach (var choice in home) { choice.Included = choice.Library.Id == "movies"; }
        Assert.True(model.Save(LibraryLayoutSurface.Home, home));
        var restored = Model();
        Assert.Equal(["anime", "tv"], restored.SidebarIds);
        Assert.Equal(["movies"], restored.HomeIds);
        Assert.Equal(["Anime", "TV"], restored.SidebarLibraries.Select(library => library.Name));
        Assert.Single(Directory.GetFiles(_directory, "*.json"));
        Assert.Empty(Directory.GetFiles(_directory, "*.pending"));
    }

    [Fact]
    public void SavedLayoutsAreBoundToUserServerIdentityAndCanonicalAddressButNotUsername()
    {
        var store = new LibraryLayoutSettingsStore(_directory);
        var preferences = new LibraryLayoutPreferences(["anime"], ["movies"]);
        store.Save(Profile, preferences);
        Assert.Equal(["anime"], store.Load(Profile with { Username = "Renamed viewer" })!.SidebarLibraryIds);
        Assert.Null(store.Load(Profile with { UserId = "another-user" }));
        Assert.Null(store.Load(Profile with { Server = Profile.Server with { Id = "replacement-server" } }));
        Assert.Null(store.Load(Profile with { Server = Profile.Server with { BaseUri = new Uri("https://media.example/another/") } }));
        var second = Profile with { UserId = "another-user" };
        store.Save(second, new LibraryLayoutPreferences(["tv"], []));
        Assert.Equal(["tv"], store.Load(second)!.SidebarLibraryIds);
        Assert.Equal(["anime"], store.Load(Profile)!.SidebarLibraryIds);
        var files = Directory.GetFiles(_directory);
        Assert.Equal(2, files.Length);
        Assert.All(files, file =>
        {
            Assert.Equal(64, Path.GetFileNameWithoutExtension(file).Length);
            var json = File.ReadAllText(file);
            Assert.DoesNotContain(Profile.UserId, json, StringComparison.Ordinal);
            Assert.DoesNotContain(Profile.Server.BaseUri.Host, json, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SavingAnEmptyListDoesNotRestoreDefaultsOrAffectTheOtherSurface()
    {
        var model = Model();
        var home = model.CreateDraft(LibraryLayoutSurface.Home);
        foreach (var choice in home) { choice.Included = false; }
        Assert.True(model.Save(LibraryLayoutSurface.Home, home));
        var restored = Model();
        Assert.Empty(restored.HomeIds);
        Assert.Equal(["tv", "movies", "anime"], restored.SidebarIds);
        Assert.All(restored.CreateDraft(LibraryLayoutSurface.Home), choice => Assert.False(choice.Included));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"SidebarLibraryIds":[],"HomeLibraryIds":null}""")]
    [InlineData("""{"SidebarLibraryIds":["tv","tv"],"HomeLibraryIds":[]}""")]
    [InlineData("""{"SidebarLibraryIds":[""],"HomeLibraryIds":[]}""")]
    [InlineData("""{"SidebarLibraryIds":[],"HomeLibraryIds":[],"Extra":true}""")]
    public void CorruptLayoutsAreExplicitAndRequireAnIntentionalSaveToReplace(string json)
    {
        var store = new LibraryLayoutSettingsStore(_directory);
        store.Save(Profile, new LibraryLayoutPreferences(["tv"], ["movies"]));
        var file = Assert.Single(Directory.GetFiles(_directory));
        File.WriteAllText(file, json);
        Assert.Throws<JsonException>(() => store.Load(Profile));
        var model = Model();
        Assert.True(model.HasLoadError);
        Assert.Contains("could not be read", model.HomeWarning, StringComparison.Ordinal);
        Assert.Equal(json, File.ReadAllText(file));
        Assert.True(model.Save(LibraryLayoutSurface.Sidebar, model.CreateDraft(LibraryLayoutSurface.Sidebar)));
        Assert.False(model.HasLoadError);
        Assert.Empty(model.HomeWarning);
    }

    [Fact]
    public void SaveFailureKeepsThePreviousAppliedLayout()
    {
        var model = Model();
        File.WriteAllText(_directory, "This path is not a directory.");
        var draft = model.CreateDraft(LibraryLayoutSurface.Home);
        foreach (var choice in draft) { choice.Included = false; }
        Assert.False(model.Save(LibraryLayoutSurface.Home, draft));
        Assert.Equal(["tv", "movies", "anime"], model.HomeIds);
        Assert.Contains("previous layout is still active", model.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableAndNewLibrariesDoNotSilentlyChangeSavedSelections()
    {
        var store = new LibraryLayoutSettingsStore(_directory);
        store.Save(Profile, new LibraryLayoutPreferences(["temporarily-hidden", "movies"], ["anime", "tv"]));
        var model = new LibraryLayoutViewModel(Profile, Libraries.Append(new MediaLibrary("new", "New TV", "tvshows")).ToArray(), store);
        Assert.Equal(["movies"], model.SidebarLibraries.Select(library => library.Id));
        Assert.False(model.CreateDraft(LibraryLayoutSurface.Sidebar).Single(choice => choice.Library.Id == "new").Included);
        Assert.Equal(["temporarily-hidden", "movies"], store.Load(Profile)!.SidebarLibraryIds);
    }

    [Fact]
    public void UnsupportedViewsCannotBeReenabledByPreviouslySavedLayouts()
    {
        var store = new LibraryLayoutSettingsStore(_directory);
        store.Save(Profile, new LibraryLayoutPreferences(["people", "tv"], ["collections", "anime"]));
        var model = Model();
        Assert.Equal(["tv"], model.SidebarLibraries.Select(library => library.Id));
        Assert.Equal(["anime"], model.HomeLibraries.Select(library => library.Id));
        Assert.All(model.CreateDraft(LibraryLayoutSurface.Sidebar), choice => Assert.True(choice.Library.IsSupportedVideoLibrary));
        Assert.All(model.CreateDraft(LibraryLayoutSurface.Home), choice => Assert.True(choice.Library.IsSupportedVideoLibrary));
        Assert.Throws<ArgumentException>(() => model.Save(LibraryLayoutSurface.Home,
            [new LibraryLayoutChoice(Libraries.Single(library => library.Id == "collections"), true)]));
    }

    [Fact]
    public void GalleryFiltersAndReordersBothSurfacesWithoutLosingHiddenImageOwnership()
    {
        var decoder = new TestPreviewImageDecoder();
        var rails = Libraries.Select(library => new MediaPreviewRail(library.Id, library.Name,
            [new MediaPreviewItem(library.Id, library.Name, "", "Movie", [1], null, null, "", null)])).ToArray();
        using var gallery = DesignGalleryViewModel.Create(new MediaPreviewHome(null, [], rails) { Libraries = Libraries }, decoder.Decode);
        var resources = decoder.Resources.Count;

        gallery.ApplyLibraryLayout(["anime", "tv"], ["movies", "anime"], string.Empty);

        Assert.Equal(["anime", "tv"], gallery.SidebarLibraries.Select(library => library.Id));
        Assert.Equal(["movies", "anime"], gallery.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
        Assert.Equal(["movies", "anime"], gallery.Libraries.Select(library => library.Id));
        Assert.Equal("movies", gallery.Featured?.Id);
        Assert.All(decoder.Resources, resource => Assert.Equal(0, resource.DisposeCount));
        gallery.ApplyLibraryLayout(["tv"], ["tv", "movies", "anime"], string.Empty);
        Assert.Equal(resources, decoder.Resources.Count);
        gallery.Dispose();
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
    }

    private LibraryLayoutViewModel Model() => new(Profile, Libraries, new LibraryLayoutSettingsStore(_directory));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
        else if (File.Exists(_directory)) { File.Delete(_directory); }
    }
}
