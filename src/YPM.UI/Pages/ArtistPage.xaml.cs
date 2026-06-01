using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using YPM.Core.Models;
using YPM.Core.Services;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class ArtistPage : Page
{
    public ArtistViewModel ViewModel { get; } = new();
    private double _scrollOffset;
    private bool _scrollRestored;
    private long _currentArtistId;

    public ArtistPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not long artistId)
        {
            return;
        }

        if (e.NavigationMode == NavigationMode.Back && artistId == _currentArtistId)
        {
            RestoreScrollPosition();
            return;
        }

        _currentArtistId = artistId;
        _scrollOffset = 0;
        _scrollRestored = false;
        await ViewModel.LoadAsync(artistId);
        PrepareTrackDisplayIndexes();
        RestoreScrollPosition();
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        _scrollOffset = MainScrollViewer.VerticalOffset;
    }

    private void RestoreScrollPosition()
    {
        if (_scrollRestored || _scrollOffset <= 0)
        {
            return;
        }

        _scrollRestored = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            MainScrollViewer.ChangeView(null, _scrollOffset, null, disableAnimation: true);
        });
    }

    private void PrepareTrackDisplayIndexes()
    {
        var index = 1;
        foreach (var track in ViewModel.PopularTracks)
        {
            track.DisplayIndex = index++;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        App.NavigationService?.GoBack();
    }

    private async void OnPlayPopularClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.PlayPopularSongsAsync();
    }

    private async void OnFollowClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleFollowAsync();
    }

    private void OnToggleDescriptionClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsDescriptionExpanded = !ViewModel.IsDescriptionExpanded;
    }

    private async void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackInfo track)
        {
            await ViewModel.PlayTrackAsync(track);
        }
    }

    private void OnAlbumTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long albumId } && albumId > 0)
        {
            App.NavigationService?.Navigate(PageRoute.AlbumDetail, albumId);
        }
    }

    private void OnLatestReleaseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LatestRelease is { Id: > 0 } album)
        {
            App.NavigationService?.Navigate(PageRoute.AlbumDetail, album.Id);
        }
    }
}
