using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using YPM.Core.Services;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; } = new();
    private double _scrollOffset;

    public HomePage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back || ViewModel.RecommendedPlaylists.Count > 0)
        {
            RestoreScrollPosition();
            return;
        }

        await ViewModel.LoadAsync();
        RestoreScrollPosition();
    }

    protected override void OnNavigatingFrom(Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        _scrollOffset = MainScrollViewer.VerticalOffset;
    }

    private void RestoreScrollPosition()
    {
        if (_scrollOffset <= 0) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            MainScrollViewer.ChangeView(null, _scrollOffset, null, disableAnimation: true);
        });
    }

    private void OnPlaylistTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long playlistId })
        {
            App.NavigationService?.Navigate(PageRoute.PlaylistDetail, playlistId);
        }
    }

    private void OnAlbumTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long albumId })
        {
            App.NavigationService?.Navigate(PageRoute.AlbumDetail, albumId);
        }
    }

    private void OnArtistTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long artistId })
        {
            App.NavigationService?.Navigate(PageRoute.ArtistDetail, artistId);
        }
    }
}
