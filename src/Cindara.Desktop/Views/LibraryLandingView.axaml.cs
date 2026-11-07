using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Navigation;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public sealed record LibraryBrowseRequest(MediaLibrary Library, MediaLibraryFilter Filter);

public partial class LibraryLandingView : UserControl
{
    private MainViewModel? _model;
    private MediaLibrary? _selected;

    public LibraryLandingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
                _model.PropertyChanged -= OnModelChanged;
            _model = DataContext as MainViewModel;
            if (_model is not null)
                _model.PropertyChanged += OnModelChanged;
            Reset();
        };
    }

    public event EventHandler<LibraryBrowseRequest>? BrowseRequested;

    public MediaLibrary? SelectedLibrary => _selected;

    public Control InitialFocus => LibrarySelector.GetVisualDescendants().OfType<Button>()
        .FirstOrDefault(button => Equals(button.DataContext, _selected)) ?? (Control)this;

    public bool TryMove(NavigationDirection direction)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Button;
        var selectors = LibrarySelector.GetVisualDescendants().OfType<Button>().ToArray();
        Button[] actions = [BrowseLibraryButton, LibraryFavoritesButton, LibraryUnwatchedButton];
        var selectorIndex = Array.IndexOf(selectors, focused);
        var actionIndex = Array.IndexOf(actions, focused);
        Control? target = null;
        if (selectorIndex >= 0)
        {
            if (direction == NavigationDirection.Down)
                target = BrowseLibraryButton;
            else if (direction is NavigationDirection.Left or NavigationDirection.Right)
            {
                var next = selectorIndex + (direction == NavigationDirection.Right ? 1 : -1);
                if (next >= 0 && next < selectors.Length)
                    target = selectors[next];
            }
        }
        else if (actionIndex >= 0)
        {
            if (direction == NavigationDirection.Up)
                target = InitialFocus;
            else if (direction is NavigationDirection.Left or NavigationDirection.Right)
            {
                var next = actionIndex + (direction == NavigationDirection.Right ? 1 : -1);
                if (next >= 0 && next < actions.Length)
                    target = actions[next];
            }
        }
        if (target is null || !target.Focus(NavigationMethod.Directional))
            return false;
        target.BringIntoView();
        return true;
    }

    public void Reset()
    {
        _selected = null;
        OverviewScroll.Offset = default;
        RefreshLibraries();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.LibraryBrowser))
            RefreshLibraries();
    }

    private void RefreshLibraries()
    {
        var libraries = _model?.LibraryBrowser?.Libraries;
        _selected = libraries?.FirstOrDefault(library => library.Id == _selected?.Id)
            ?? (libraries is { Count: > 0 } ? libraries[0] : null);
        EmptyMessage.IsVisible = _selected is null;
        LibraryOverview.IsVisible = _selected is not null;
        SelectedLibraryTitle.Text = _selected?.Name;
        Dispatcher.UIThread.Post(UpdateSelection, DispatcherPriority.Loaded);
    }

    private void OnLibrarySelected(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaLibrary library })
        {
            _selected = library;
            RefreshLibraries();
        }
    }

    private void UpdateSelection()
    {
        foreach (var button in LibrarySelector.GetVisualDescendants().OfType<Button>())
        {
            var selected = Equals(button.DataContext, _selected);
            button.Classes.Set("selected", selected);
            AutomationProperties.SetItemStatus(button,
                selected ? Localization.Loc.Get("State.Selected") : string.Empty);
        }
    }

    private void Browse(MediaLibraryFilter filter)
    {
        if (_selected is { } library)
            BrowseRequested?.Invoke(this, new LibraryBrowseRequest(library, filter));
    }

    private void OnBrowseClicked(object? sender, RoutedEventArgs args) => Browse(MediaLibraryFilter.All);
    private void OnFavoritesClicked(object? sender, RoutedEventArgs args) => Browse(MediaLibraryFilter.Favorites);
    private void OnUnwatchedClicked(object? sender, RoutedEventArgs args) => Browse(MediaLibraryFilter.Unwatched);
}
