using System.Collections.ObjectModel;
using YPM.Core.Models;
using YPM.Core.Mvvm;

namespace YPM.UI.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private const int HomeSectionItemCount = 14;

    private bool _isLoading;
    private string _errorMessage = string.Empty;

    public ObservableCollection<PlaylistSummary> RecommendedPlaylists { get; } = [];

    public ObservableCollection<AlbumSummary> NewAlbums { get; } = [];

    public ObservableCollection<ArtistSummary> TopArtists { get; } = [];

    public ObservableCollection<PlaylistSummary> TopLists { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var area = App.Settings.MusicLanguage;
            var playlistsTask = LoadSectionAsync(
                async () => await App.ApiClient.GetRecommendedPlaylistsAsync(HomeSectionItemCount),
                RecommendedPlaylists,
                "推荐歌单");
            var albumsTask = LoadSectionAsync(
                async () => await App.ApiClient.GetNewAlbumsAsync(area, HomeSectionItemCount),
                NewAlbums,
                "新碟");
            var artistsTask = LoadSectionAsync(
                async () => (await App.ApiClient.GetTopArtistsAsync(MapArtistArea(area))).Take(HomeSectionItemCount).ToList(),
                TopArtists,
                "推荐艺人");
            var topListsTask = LoadSectionAsync(
                async () => (await App.ApiClient.GetTopListsAsync()).Take(HomeSectionItemCount).ToList(),
                TopLists,
                "排行榜");

            var results = await Task.WhenAll(playlistsTask, albumsTask, artistsTask, topListsTask);
            ErrorMessage = string.Join(Environment.NewLine, results.Where(static message => !string.IsNullOrWhiteSpace(message)));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static int? MapArtistArea(string area) => area.ToUpperInvariant() switch
    {
        "ZH" => 1,
        "EA" => 2,
        "KR" => 3,
        "JP" => 4,
        _ => null,
    };

    private static void ReplaceItems<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private static async Task<string?> LoadSectionAsync<T>(
        Func<Task<IReadOnlyList<T>>> loader,
        ObservableCollection<T> target,
        string sectionName)
    {
        try
        {
            var items = await loader();
            ReplaceItems(target, items);
            return null;
        }
        catch (Exception ex)
        {
            target.Clear();
            return $"{sectionName}加载失败: {ex.Message}";
        }
    }
}
