using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class AlbumPage : Page
{
    public AlbumViewModel ViewModel { get; } = new();
    private double _scrollOffset;
    private bool _scrollRestored;
    private long _currentAlbumId;

    public AlbumPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is long albumId)
        {
            if (e.NavigationMode == NavigationMode.Back && albumId == _currentAlbumId)
            {
                RestoreScrollPosition();
                return;
            }

            _currentAlbumId = albumId;
            _scrollOffset = 0;
            _scrollRestored = false;
            await ViewModel.LoadAsync(albumId);
            RestoreScrollPosition();
        }
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        _scrollOffset = MainScrollViewer.VerticalOffset;
    }

    private void RestoreScrollPosition()
    {
        if (_scrollRestored || _scrollOffset <= 0) return;
        _scrollRestored = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            MainScrollViewer.ChangeView(null, _scrollOffset, null, disableAnimation: true);
        });
    }

    private async void OnPlayAllClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.PlayAllAsync();
    }

    private async void OnSubscribeClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleSubscribeAsync();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        App.NavigationService?.GoBack();
    }

    private void OnToggleDescriptionClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsDescriptionExpanded = !ViewModel.IsDescriptionExpanded;
    }

    private async void OnTrackLikeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var trackId = btn.Tag switch
        {
            long id => id,
            string s when long.TryParse(s, out var id) => id,
            _ => 0L,
        };

        if (trackId > 0)
        {
            var track = ViewModel.Album?.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track is not null)
                await ViewModel.ToggleTrackLikeAsync(track);
        }
    }

    private async void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is YPM.Core.Models.TrackInfo track)
        {
            await ViewModel.PlayTrackAsync(track);
        }
    }

    private void OnAlbumCoverImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        var fallback = ViewModel.Album?.Tracks.FirstOrDefault()?.Album?.CoverUrl;
        if (!string.IsNullOrWhiteSpace(fallback) && Uri.TryCreate(fallback, UriKind.Absolute, out var uri))
        {
            image.Source = new BitmapImage(uri);
        }
    }
}
