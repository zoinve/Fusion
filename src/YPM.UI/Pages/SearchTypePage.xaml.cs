using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using YPM.Core.Services;
using YPM.Core.Models;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class SearchTypePage : Page
{
    public SearchTypeViewModel ViewModel { get; } = new();

    public SearchTypePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchTypeParams p)
        {
            await ViewModel.LoadAsync(p.Keyword, p.TypeKey);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        App.NavigationService?.GoBack();
    }

    private void OnItemTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: long id } || id <= 0) return;

        var typeKey = ViewModel.TypeKey;
        switch (typeKey)
        {
            case "albums":
                App.NavigationService?.Navigate(PageRoute.AlbumDetail, id);
                break;
            case "playlists":
                App.NavigationService?.Navigate(PageRoute.PlaylistDetail, id);
                break;
            case "artists":
                App.NavigationService?.Navigate(PageRoute.ArtistDetail, id);
                break;
            case "tracks":
                PlayTrack(id);
                break;
        }
    }

    private async void PlayTrack(long trackId)
    {
        var player = App.AudioPlayer;
        if (player is null) return;

        var allTracks = new List<TrackInfo>();
        TrackInfo? targetTrack = null;

        foreach (var item in ViewModel.Results)
        {
            if (item.Type != SearchItemType.Song) continue;

            var track = new TrackInfo
            {
                Id = item.Id,
                Name = item.Name,
            };

            if (!string.IsNullOrWhiteSpace(item.CoverUrl))
            {
                track.Album = new AlbumSummary
                {
                    CoverUrl = item.CoverUrl,
                };
            }

            allTracks.Add(track);

            if (item.Id == trackId)
                targetTrack = track;
        }

        if (targetTrack is null && allTracks.Count > 0)
            targetTrack = allTracks[0];

        if (targetTrack is null) return;

        var index = allTracks.FindIndex(t => t.Id == trackId);
        if (index < 0) index = 0;

        player.SetQueue(allTracks, index);
        await player.PlayAsync(index);
    }

    private void OnLoadMoreClick(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadMoreCommand.Execute(null);
    }
}
