using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; } = new();
    private double _scrollOffset;
    private int _historyTabIndex;
    private readonly List<Button> _tabButtons = [];

    public LibraryPage()
    {
        InitializeComponent();
        CollectTabButtons();
    }

    private void CollectTabButtons()
    {
        foreach (var child in TabBar.Children)
        {
            if (child is Button btn)
                _tabButtons.Add(btn);
        }
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (!ViewModel.IsLoggedIn)
        {
            LoginPrompt.Visibility = Visibility.Visible;
            return;
        }

        LoginPrompt.Visibility = Visibility.Collapsed;

        if (ViewModel.Playlists.Count == 0)
        {
            await ViewModel.LoadAsync();
        }
        else
        {
            // Always refresh liked songs (count, latest cover) from API on re-entry
            await ViewModel.RefreshLikedSongsAsync();
        }

        if (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back)
        {
            RestoreScrollPosition();
        }

        // Ensure a tab is selected and its section is visible
        SelectTab(ViewModel.SelectedTabIndex);
        UpdateHistoryTabSelection();
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

    private void SelectTab(int index)
    {
        PlaylistsSection.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        AlbumsSection.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ArtistsSection.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        MvsSection.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        CloudSection.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;

        for (var i = 0; i < _tabButtons.Count && i < 6; i++)
        {
            _tabButtons[i].Opacity = i == index ? 1.0 : 0.45;
        }

        ViewModel.SelectedTabIndex = index;
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
        {
            SelectTab(index);
        }
    }

    private void OnHistoryTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out _historyTabIndex))
        {
            UpdateHistoryTabSelection();
        }
    }

    private void UpdateHistoryTabSelection()
    {
        WeekTabButton.Opacity = _historyTabIndex == 0 ? 1.0 : 0.45;
        AllTabButton.Opacity = _historyTabIndex == 1 ? 1.0 : 0.45;
        HistoryList.ItemsSource = _historyTabIndex == 0 ? ViewModel.HistoryWeek : ViewModel.HistoryAll;
    }

    private void OnLikedSongsClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.LikedSongsPlaylistId))
        {
            App.NavigationService?.Navigate(Core.Services.PageRoute.PlaylistDetail, long.Parse(ViewModel.LikedSongsPlaylistId));
        }
    }

    private void OnPlaylistTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long playlistId })
        {
            App.NavigationService?.Navigate(Core.Services.PageRoute.PlaylistDetail, playlistId);
        }
    }

    private void OnAlbumTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long albumId })
        {
            App.NavigationService?.Navigate(Core.Services.PageRoute.AlbumDetail, albumId);
        }
    }

    private async void OnCloudTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is YPM.Core.Models.TrackInfo track)
        {
            await ViewModel.PlayCloudTrackAsync(track);
        }
    }

    private async void OnHistoryTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is YPM.Core.Models.RecordItem { Song: not null } record)
        {
            var source = _historyTabIndex == 0 ? ViewModel.HistoryWeek : ViewModel.HistoryAll;
            var index = source.IndexOf(record);
            await ViewModel.PlayHistoryAsync(source, index >= 0 ? index : 0);
        }
    }
}
