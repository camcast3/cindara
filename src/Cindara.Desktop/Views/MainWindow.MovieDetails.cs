using Avalonia.Controls;
using Avalonia.Threading;
using Cindara.Desktop.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class MainWindow
{
    private MovieDetailsViewModel? _activeMovieDetails;
    private Control? _detailsReturnFocus;
    private MovieCreditsView? _creditsView;

    private async void ShowMediaItem(MediaPreviewCardViewModel item, bool continueWatching = false)
    {
        if (ModalOverlay.IsVisible || MovieDetails.IsVisible) return;
        if (item.MediaType != "Movie" || continueWatching || _viewModel?.MovieDetails is not { } details)
        {
            ShowMediaSummary(item);
            return;
        }

        _navigation.Remember();
        _detailsReturnFocus = FocusManager?.GetFocusedElement() as Control;
        GalleryView.SuspendFocusMemory();
        Shell.LibraryView.SuspendFocusMemory();
        Shell.SearchView.SuspendFocusMemory();
        _activeMovieDetails = details;
        MovieDetails.IsVisible = true;
        MainSurface.IsEnabled = false;
        MovieDetails.ResetPosition();
        _navigation.Forget("movie-details");
        _navigation.SetScope(MovieDetails, MovieDetails.BackAction, "movie-details");
        await details.OpenAsync(item.Id, item.Name);
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
        if (!MovieDetails.IsVisible || !force && _activeMovieDetails?.CanClose is false) return;
        var details = _activeMovieDetails;
        _activeMovieDetails = null;
        if (details is not null)
        {
            details.Close();
        }
        MovieDetails.IsVisible = false;
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
            if (_viewModel?.IsDesignGalleryVisible is true && !MovieDetails.IsVisible)
                GalleryView.RestoreHomeFocus();
        }, DispatcherPriority.Loaded);
    }
}
