using Avalonia.Controls;
using Avalonia.Threading;
using Cindara.Desktop.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class MainWindow
{
    private MediaDetailsViewModel? _activeMovieDetails;
    private Control? _detailsReturnFocus;
    private Control? _browserReturnFocus;
    private bool _browserFromSeries;
    private bool _browserParentPreloaded;
    private MovieCreditsView? _creditsView;
    private bool IsDetailsVisible => MovieDetails.IsVisible || SeriesOverview.IsVisible || SeasonBrowser.IsVisible;
    private Control DetailsSurface => SeasonBrowser.IsVisible ? SeasonBrowser
        : SeriesOverview.IsVisible ? SeriesOverview : MovieDetails;
    private Button DetailsBackAction => SeasonBrowser.IsVisible ? SeasonBrowser.BackAction
        : SeriesOverview.IsVisible ? SeriesOverview.BackAction : MovieDetails.BackAction;

    private async void ShowMediaItem(MediaPreviewCardViewModel item, bool continueWatching = false, bool fromHome = false)
    {
        if (ModalOverlay.IsVisible || IsDetailsVisible) return;
        if (!continueWatching && item.MediaType is "Season" or "Episode"
            && !string.IsNullOrWhiteSpace(item.SeriesId)
            && (item.MediaType == "Season" || !string.IsNullOrWhiteSpace(item.SeasonId)))
        {
            ShowSeasonBrowser(item.SeriesId, item.MediaType == "Season" ? item.Id : item.SeasonId,
                item.HeroName, item.MediaType == "Episode" ? item.Id : null);
            return;
        }
        var seriesId = item.MediaType == "Series" ? item.Id
            : fromHome && item.MediaType is "Season" or "Episode" ? item.SeriesId : null;
        if (!string.IsNullOrWhiteSpace(seriesId) && !continueWatching && _viewModel?.SeriesOverview is { } series)
        {
            RememberDetailsSource();
            _activeMovieDetails = series.Summary;
            SeriesOverview.IsVisible = true;
            MainSurface.IsEnabled = false;
            SeriesOverview.ResetPosition();
            _navigation.Forget("series-overview");
            _navigation.SetScope(SeriesOverview, SeriesOverview.BackAction, "series-overview");
            await series.OpenAsync(seriesId, item.HeroName);
            return;
        }
        if (item.MediaType != "Movie" || continueWatching || _viewModel?.MovieDetails is not { } details)
        {
            ShowMediaSummary(item);
            return;
        }

        RememberDetailsSource();
        _activeMovieDetails = details;
        MovieDetails.IsVisible = true;
        MainSurface.IsEnabled = false;
        MovieDetails.ResetPosition();
        _navigation.Forget("movie-details");
        _navigation.SetScope(MovieDetails, MovieDetails.BackAction, "movie-details");
        await details.OpenAsync(item.Id, item.Name);
    }

    private void RememberDetailsSource()
    {
        _navigation.Remember();
        _detailsReturnFocus = FocusManager?.GetFocusedElement() as Control;
        GalleryView.SuspendFocusMemory();
        Shell.LibraryView.SuspendFocusMemory();
        Shell.SearchView.SuspendFocusMemory();
    }

    private async void ShowSeasonBrowser(string? seriesId, string? seasonId, string title, string? episodeId = null)
    {
        if (ModalOverlay.IsVisible || SeasonBrowser.IsVisible || string.IsNullOrWhiteSpace(seriesId)
            || string.IsNullOrWhiteSpace(seasonId) || _viewModel?.SeasonBrowser is not { } browser) return;
        _browserFromSeries = SeriesOverview.IsVisible;
        if (!_browserFromSeries) RememberDetailsSource();
        _browserReturnFocus = FocusManager?.GetFocusedElement() as Control;
        Task? preload = null;
        _browserParentPreloaded = !_browserFromSeries && _viewModel?.SeriesOverview is { };
        if (_browserParentPreloaded && _viewModel?.SeriesOverview is { } parent)
        {
            SeriesOverview.ResetPosition();
            SeriesOverview.IsVisible = true;
            _activeMovieDetails = parent.Summary;
            preload = parent.OpenAsync(seriesId, title);
        }
        SeasonBrowser.ResetPosition();
        SeasonBrowser.IsVisible = true;
        SeriesOverview.IsEnabled = false;
        MainSurface.IsEnabled = false;
        _navigation.Forget("season-browser");
        _navigation.SetScope(SeasonBrowser, SeasonBrowser.BackAction, "season-browser");
        await browser.OpenAsync(seriesId, seasonId, title, episodeId);
        if (SeasonBrowser.IsVisible && browser.IsOpen && episodeId is not null)
            Dispatcher.UIThread.Post(() => SeasonBrowser.FocusSelectedEpisode(episodeId), DispatcherPriority.Loaded);
        if (preload is not null)
        {
            await preload;
            if (!SeasonBrowser.IsVisible && SeriesOverview.IsVisible
                && _viewModel?.SeriesOverview?.Summary.Details?.Id == seriesId)
                Dispatcher.UIThread.Post(() =>
                {
                    if (SeriesOverview.IsVisible && !SeasonBrowser.IsVisible
                        && FocusManager?.GetFocusedElement() == SeriesOverview.BackAction)
                        SeriesOverview.FocusSeason(seasonId);
                }, DispatcherPriority.Loaded);
        }
    }

    private void CloseSeasonBrowser(bool force = false)
    {
        if (!SeasonBrowser.IsVisible) return;
        var seasonId = _viewModel?.SeasonBrowser?.SelectedSeason?.Id;
        _viewModel?.SeasonBrowser?.Close();
        SeasonBrowser.IsVisible = false;
        SeriesOverview.IsEnabled = true;
        var focus = _browserReturnFocus;
        _browserReturnFocus = null;
        var fromSeries = (_browserFromSeries || _browserParentPreloaded) && SeriesOverview.IsVisible;
        var preloaded = _browserParentPreloaded;
        _browserFromSeries = false;
        _browserParentPreloaded = false;
        if (force) return;
        if (!fromSeries)
        {
            MainSurface.IsEnabled = true;
            _navigation.SetScope(ActiveSurface, key: _screen ?? "gallery");
            if (focus is not null) _navigation.Focus(focus);
            _detailsReturnFocus = null;
            Shell.LibraryView.ResumeFocusMemory();
            Shell.SearchView.ResumeFocusMemory();
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel?.IsDesignGalleryVisible is true && !IsDetailsVisible)
                    GalleryView.RestoreHomeFocus();
            }, DispatcherPriority.Loaded);
            return;
        }
        _navigation.SetScope(SeriesOverview, SeriesOverview.BackAction, "series-overview");
        if (preloaded)
        {
            if (seasonId is null || !SeriesOverview.FocusSeason(seasonId))
                _navigation.Focus(SeriesOverview.BackAction);
        }
        else if (focus is not null) _navigation.Focus(focus);
    }

    private void ShowMovieCredits(MovieCreditViewModel? selected = null)
    {
        if (ModalOverlay.IsVisible || _activeMovieDetails is not { HasDetails: true, CanClose: true } details) return;
        BeginFullScreenModal(Loc.Get("Details.Credits"));
        _creditsView = new MovieCreditsView();
        _creditsView.BackRequested += (_, _) => DismissModal();
        ResizeCredits();
        ModalActions.Children.Add(_creditsView);
        _creditsView.ShowCredits(details, selected);
        FocusModal(_creditsView.BackAction);
    }

    private void ResizeCredits()
    {
        if (_creditsView is null) return;
        var density = Cindara.Desktop.DesignSystem.ResponsiveDensityProfile.Create(ClientSize.Width, ClientSize.Height);
        var margin = Cindara.Desktop.DesignSystem.AdaptiveLayoutProfile.Create(ClientSize.Width, ClientSize.Height).SafeMargin;
        _creditsView.Height = Math.Max(120, ClientSize.Height - margin * 2 - 100 * density.TypeScale - 64);
    }

    private void ShowMovieInformation()
    {
        if (ModalOverlay.IsVisible || _activeMovieDetails is not { HasDetails: true, CanClose: true } details) return;
        BeginFullScreenModal(Loc.Get("Details.MoreInformation"));
        var text = new[] { details.Title, details.Metadata, details.Genres, details.Ratings, details.DirectorSummary,
                details.Overview, details.Trailers, details.ArtworkMessage, details.Message };
        var content = new StackPanel { Spacing = 16 };
        foreach (var paragraph in text.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            content.Children.Add(new TextBlock
            {
                Text = paragraph,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
        }
        var scroll = new ScrollViewer
        {
            Content = content,
            Height = Math.Max(120, ClientSize.Height - 260),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var controls = new WrapPanel();
        Button AddAction(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Avalonia.Thickness(0, 0, 12, 0) };
            button.Click += (_, _) => action();
            controls.Children.Add(button);
            return button;
        }
        var back = AddAction(Loc.Get("Action.Back"), DismissModal);
        void Scroll(bool down) => scroll.Offset = new Avalonia.Vector(0, Math.Clamp(
            scroll.Offset.Y + (down ? 1 : -1) * scroll.Viewport.Height * 0.8,
            0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
        AddAction(Loc.Get("Details.ReadAbove"), () => Scroll(false));
        AddAction(Loc.Get("Details.ReadBelow"), () => Scroll(true));
        ModalActions.Children.Add(controls);
        ModalActions.Children.Add(scroll);
        FocusModal(back);
    }

    private void CloseMovieDetails(bool force = false)
    {
        if (!IsDetailsVisible || !force && _activeMovieDetails?.CanClose is false) return;
        CloseSeasonBrowser(force: true);
        var details = _activeMovieDetails;
        _activeMovieDetails = null;
        if (details is not null)
        {
            details.Close();
        }
        MovieDetails.IsVisible = false;
        if (SeriesOverview.IsVisible) _viewModel?.SeriesOverview?.Close();
        SeriesOverview.IsVisible = false;
        MainSurface.IsEnabled = true;
        var returnFocus = _detailsReturnFocus;
        _detailsReturnFocus = null;
        if (force) return;
        _navigation.SetScope(ActiveSurface, key: _screen ?? "gallery");
        if (returnFocus is not null) _navigation.Focus(returnFocus);
        Shell.LibraryView.ResumeFocusMemory();
        Shell.SearchView.ResumeFocusMemory();
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.IsDesignGalleryVisible is true && !IsDetailsVisible)
                GalleryView.RestoreHomeFocus();
        }, DispatcherPriority.Loaded);
    }
}
