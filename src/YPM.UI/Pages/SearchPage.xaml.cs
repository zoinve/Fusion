using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using YPM.Core.Services;
using YPM.UI.Helpers;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; } = new();

    public string SearchGlyph => IconGlyph.Search;

    public SearchPage()
    {
        InitializeComponent();
    }

    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.SearchCommand.Execute(null);
        }
    }

    private async void OnSongTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long songId } && songId > 0)
        {
            var player = App.AudioPlayer;
            if (player is null) return;

            var track = ViewModel.CreateTrackInfoFromSearchResult(songId);
            if (track is null) return;

            var allTracks = ViewModel.GetAllSongTracks();
            if (allTracks.Count == 0)
            {
                allTracks.Add(track);
            }

            var index = allTracks.FindIndex(t => t.Id == songId);
            if (index < 0) index = 0;

            player.SetQueue(allTracks, index);
            await player.PlayAsync(index);
        }
    }

    private void OnAlbumTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long albumId } && albumId > 0)
        {
            App.NavigationService?.Navigate(PageRoute.AlbumDetail, albumId);
        }
    }

    private void OnArtistTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long artistId } && artistId > 0)
        {
            App.NavigationService?.Navigate(PageRoute.ArtistDetail, artistId);
        }
    }

    private void OnPlaylistTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long playlistId } && playlistId > 0)
        {
            App.NavigationService?.Navigate(PageRoute.PlaylistDetail, playlistId);
        }
    }

    private void OnMoreSongsClick(object sender, RoutedEventArgs e)
    {
        var keyword = ViewModel.LastKeyword;
        if (string.IsNullOrWhiteSpace(keyword)) return;
        App.NavigationService?.Navigate(PageRoute.SearchType,
            new SearchTypeParams(keyword, "tracks"));
    }

    private void OnMoreAlbumsClick(object sender, RoutedEventArgs e)
    {
        var keyword = ViewModel.LastKeyword;
        if (string.IsNullOrWhiteSpace(keyword)) return;
        App.NavigationService?.Navigate(PageRoute.SearchType,
            new SearchTypeParams(keyword, "albums"));
    }

    private void OnMoreArtistsClick(object sender, RoutedEventArgs e)
    {
        var keyword = ViewModel.LastKeyword;
        if (string.IsNullOrWhiteSpace(keyword)) return;
        App.NavigationService?.Navigate(PageRoute.SearchType,
            new SearchTypeParams(keyword, "artists"));
    }

    private void OnMorePlaylistsClick(object sender, RoutedEventArgs e)
    {
        var keyword = ViewModel.LastKeyword;
        if (string.IsNullOrWhiteSpace(keyword)) return;
        App.NavigationService?.Navigate(PageRoute.SearchType,
            new SearchTypeParams(keyword, "playlists"));
    }
}

public record SearchTypeParams(string Keyword, string TypeKey);
