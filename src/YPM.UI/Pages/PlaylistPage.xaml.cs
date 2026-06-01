using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class PlaylistPage : Page
{
    public PlaylistViewModel ViewModel { get; } = new();

    public PlaylistPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is long playlistId)
        {
            await ViewModel.LoadAsync(playlistId);
        }
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

    private async void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is YPM.Core.Models.TrackInfo track)
        {
            await ViewModel.PlayTrackAsync(track);
        }
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
            var track = ViewModel.AllTracks.FirstOrDefault(t => t.Id == trackId);
            if (track is not null)
                await ViewModel.ToggleTrackLikeAsync(track);
        }
    }

    private async void OnMainScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ViewModel.IsLoading || ViewModel.IsLoadingMore || !ViewModel.HasMoreTracks)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ViewModel.SearchQuery))
        {
            return;
        }

        if (sender is ScrollViewer scrollViewer &&
            scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset <= 320)
        {
            await ViewModel.LoadMoreAsync();
        }
    }
}
