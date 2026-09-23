using System.Text.Json;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Libraries;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cindara.Desktop.ViewModels;

public enum LibraryLayoutSurface { Sidebar, Home }

public sealed class LibraryLayoutChoice(MediaLibrary library, bool included)
{
    public MediaLibrary Library { get; } = library;
    public bool Included { get; set; } = included;
}

public sealed class LibraryLayoutViewModel : ObservableObject
{
    private readonly SessionProfile _profile;
    private readonly LibraryLayoutSettingsStore _store;
    private readonly LocalDiagnostics? _diagnostics;
    private readonly IReadOnlyList<MediaLibrary> _libraries;
    private LibraryLayoutPreferences _preferences;
    private string _status = string.Empty;
    private bool _hasLoadError;

    public LibraryLayoutViewModel(SessionProfile profile, IReadOnlyList<MediaLibrary> libraries,
        LibraryLayoutSettingsStore store, LocalDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(store);
        _profile = profile;
        _store = store;
        _diagnostics = diagnostics;
        _libraries = libraries.Where(library => library.IsSupportedVideoLibrary).DistinctBy(library => library.Id).ToArray();
        var defaults = _libraries
            .OrderBy(library => library.Name.Contains("anime", StringComparison.OrdinalIgnoreCase)
                ? 2 : library.CollectionType == "tvshows" ? 0 : 1)
            .Select(library => library.Id).ToArray();
        _preferences = new LibraryLayoutPreferences(defaults, defaults.ToArray());
        using var operation = diagnostics?.Begin(DiagnosticArea.Storage, DiagnosticAction.LoadLibraryLayout);
        try
        {
            _preferences = store.Load(profile) ?? _preferences;
            operation?.Complete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            operation?.Fail(exception);
            _hasLoadError = true;
            _status = Loc.Get("LibraryLayout.LoadFailed");
        }
    }

    public event EventHandler? Applied;
    public IReadOnlyList<string> SidebarIds => _preferences.SidebarLibraryIds;
    public IReadOnlyList<string> HomeIds => _preferences.HomeLibraryIds;
    public IReadOnlyList<MediaLibrary> SidebarLibraries => Resolve(SidebarIds);
    public IReadOnlyList<MediaLibrary> HomeLibraries => Resolve(HomeIds);
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasLoadError { get => _hasLoadError; private set => SetProperty(ref _hasLoadError, value); }
    public string HomeWarning => HasLoadError ? Loc.Get("LibraryLayout.LoadFailed") : string.Empty;

    public List<LibraryLayoutChoice> CreateDraft(LibraryLayoutSurface surface)
    {
        if (!Enum.IsDefined(surface))
        {
            throw new ArgumentOutOfRangeException(nameof(surface));
        }

        var selected = surface == LibraryLayoutSurface.Sidebar ? SidebarIds : HomeIds;
        var ordered = Resolve(selected);
        return ordered.Select(library => new LibraryLayoutChoice(library, true))
            .Concat(_libraries.Where(library => !selected.Contains(library.Id))
                .Select(library => new LibraryLayoutChoice(library, false))).ToList();
    }

    public bool Save(LibraryLayoutSurface surface, IReadOnlyList<LibraryLayoutChoice> draft)
    {
        if (!Enum.IsDefined(surface))
        {
            throw new ArgumentOutOfRangeException(nameof(surface));
        }

        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Select(choice => choice.Library.Id).Distinct(StringComparer.Ordinal).Count() != draft.Count
            || draft.Any(choice => !_libraries.Contains(choice.Library)))
        {
            throw new ArgumentException("Library choices must belong to this account and be unique.", nameof(draft));
        }

        var selected = draft.Where(choice => choice.Included).Select(choice => choice.Library.Id).ToArray();
        var preferences = surface == LibraryLayoutSurface.Sidebar
            ? _preferences with { SidebarLibraryIds = selected }
            : _preferences with { HomeLibraryIds = selected };
        using var operation = _diagnostics?.Begin(DiagnosticArea.Storage, DiagnosticAction.SaveLibraryLayout);
        try
        {
            _store.Save(_profile, preferences);
            _preferences = preferences;
            HasLoadError = false;
            Status = Loc.Get("LibraryLayout.Saved");
            OnPropertyChanged(nameof(SidebarIds));
            OnPropertyChanged(nameof(HomeIds));
            OnPropertyChanged(nameof(HomeWarning));
            OnPropertyChanged(nameof(SidebarLibraries));
            OnPropertyChanged(nameof(HomeLibraries));
            Applied?.Invoke(this, EventArgs.Empty);
            operation?.Complete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            operation?.Fail(exception);
            Status = Loc.Get("LibraryLayout.SaveFailed");
            return false;
        }
    }

    private MediaLibrary[] Resolve(IReadOnlyList<string> ids) =>
        ids.Select(id => _libraries.FirstOrDefault(library => library.Id == id)).OfType<MediaLibrary>().ToArray();
}
