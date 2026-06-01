using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using YPM.Core.Models;
using YPM.Core.Mvvm;

namespace YPM.UI.ViewModels;

public sealed class SearchItemInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public SearchItemType Type { get; set; }
}

public enum SearchItemType
{
    Song,
    Album,
    Artist,
    Playlist,
}

public sealed class SearchViewModel : ObservableObject
{
    private string _searchText = string.Empty;
    private string _statusText = string.Empty;
    private bool _isLoading;
    private bool _hasSearched;
    private string _lastKeyword = string.Empty;

    public ObservableCollection<SearchItemInfo> Songs { get; } = [];
    public ObservableCollection<SearchItemInfo> Albums { get; } = [];
    public ObservableCollection<SearchItemInfo> Artists { get; } = [];
    public ObservableCollection<SearchItemInfo> Playlists { get; } = [];

    private readonly Dictionary<long, JsonObject> _songDataCache = [];
    private readonly List<JsonObject> _allSongsRaw = [];
    private readonly List<JsonObject> _allAlbumsRaw = [];
    private readonly List<JsonObject> _allArtistsRaw = [];
    private readonly List<JsonObject> _allPlaylistsRaw = [];

    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand ClearSearchCommand { get; }

    public SearchViewModel()
    {
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !string.IsNullOrWhiteSpace(_searchText));
        ClearSearchCommand = new RelayCommand(ClearSearch);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                SearchCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasSearchText));
            }
        }
    }

    public string LastKeyword => _lastKeyword;
    public bool HasSearchText => !string.IsNullOrWhiteSpace(_searchText);

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public bool HasSearched
    {
        get => _hasSearched;
        set
        {
            if (SetProperty(ref _hasSearched, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public bool ShowEmptyState => !_isLoading && !_hasSearched;

    private bool _hasSongs;
    private bool _hasAlbums;
    private bool _hasArtists;
    private bool _hasPlaylists;

    public bool HasSongs
    {
        get => _hasSongs;
        private set => SetProperty(ref _hasSongs, value);
    }

    public bool HasAlbums
    {
        get => _hasAlbums;
        private set => SetProperty(ref _hasAlbums, value);
    }

    public bool HasArtists
    {
        get => _hasArtists;
        private set => SetProperty(ref _hasArtists, value);
    }

    public bool HasPlaylists
    {
        get => _hasPlaylists;
        private set => SetProperty(ref _hasPlaylists, value);
    }

    private bool _hasMoreSongs;
    private bool _hasMoreAlbums;
    private bool _hasMoreArtists;
    private bool _hasMorePlaylists;

    public bool HasMoreSongs
    {
        get => _hasMoreSongs;
        private set => SetProperty(ref _hasMoreSongs, value);
    }

    public bool HasMoreAlbums
    {
        get => _hasMoreAlbums;
        private set => SetProperty(ref _hasMoreAlbums, value);
    }

    public bool HasMoreArtists
    {
        get => _hasMoreArtists;
        private set => SetProperty(ref _hasMoreArtists, value);
    }

    public bool HasMorePlaylists
    {
        get => _hasMorePlaylists;
        private set => SetProperty(ref _hasMorePlaylists, value);
    }

    public bool HasResults =>
        Songs.Count > 0 || Albums.Count > 0 || Artists.Count > 0 || Playlists.Count > 0;

    public async Task SearchAsync()
    {
        var keyword = _searchText?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        IsLoading = true;
        HasSearched = true;
        StatusText = string.Empty;
        ClearResults();

        try
        {
            // Search for all types in parallel
            var songsTask = App.ApiClient.CloudSearchAsync(keyword, type: 1, limit: 16);
            var albumsTask = App.ApiClient.CloudSearchAsync(keyword, type: 10, limit: 16);
            var artistsTask = App.ApiClient.CloudSearchAsync(keyword, type: 100, limit: 16);
            var playlistsTask = App.ApiClient.CloudSearchAsync(keyword, type: 1000, limit: 16);

            await Task.WhenAll(songsTask, albumsTask, artistsTask, playlistsTask);

            var songsResult = await songsTask;
            var albumsResult = await albumsTask;
            var artistsResult = await artistsTask;
            var playlistsResult = await playlistsTask;

            _lastKeyword = keyword;

            PopulateSectionRaw(songsResult.Songs?.Items, _allSongsRaw);
            PopulateSectionRaw(albumsResult.Albums?.Items, _allAlbumsRaw);
            PopulateSectionRaw(artistsResult.Artists?.Items, _allArtistsRaw);
            PopulateSectionRaw(playlistsResult.Playlists?.Items, _allPlaylistsRaw);

            // Populate display collections (first 3 only)
            PopulateDisplaySection(_allSongsRaw, SearchItemType.Song, Songs);
            PopulateDisplaySection(_allAlbumsRaw, SearchItemType.Album, Albums);
            PopulateDisplaySection(_allArtistsRaw, SearchItemType.Artist, Artists);
            PopulateDisplaySection(_allPlaylistsRaw, SearchItemType.Playlist, Playlists);

            // Cache song data for playback
            foreach (var json in _allSongsRaw)
            {
                var id = SafeGetLong(json, "id");
                if (id > 0)
                    _songDataCache[id] = json;
            }

            UpdateSectionFlags();

            var totalCount = (songsResult.Songs?.Total ?? 0) +
                             (albumsResult.Albums?.Total ?? 0) +
                             (artistsResult.Artists?.Total ?? 0) +
                             (playlistsResult.Playlists?.Total ?? 0);
            StatusText = totalCount > 0 ? $"共 {totalCount} 个结果" : "未找到结果";
        }
        catch (Exception ex)
        {
            StatusText = $"搜索失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static void PopulateSectionRaw(List<object>? items, List<JsonObject> target)
    {
        if (items is null) return;
        foreach (var item in items)
        {
            if (item is JsonObject json)
                target.Add(json);
        }
    }

    public TrackInfo? CreateTrackInfoFromSearchResult(long songId)
    {
        if (!_songDataCache.TryGetValue(songId, out var json))
            return null;

        try
        {
            var track = new TrackInfo
            {
                Id = songId,
                Name = SafeGetString(json, "name"),
                Duration = SafeGetLong(json, "dt"),
            };

            if (json.TryGetPropertyValue("al", out var alNode) && alNode is JsonObject alObj)
            {
                track.Album = new AlbumSummary
                {
                    Id = SafeGetLong(alObj, "id"),
                    Name = SafeGetString(alObj, "name"),
                    CoverUrl = SafeGetString(alObj, "picUrl"),
                };
            }

            if (json.TryGetPropertyValue("ar", out var arNode) && arNode is JsonArray arArray)
            {
                foreach (var ar in arArray)
                {
                    if (ar is JsonObject arObj)
                    {
                        track.Artists.Add(new ArtistSummary
                        {
                            Id = SafeGetLong(arObj, "id"),
                            Name = SafeGetString(arObj, "name"),
                        });
                    }
                }
            }

            return track;
        }
        catch
        {
            return null;
        }
    }

    public List<TrackInfo> GetAllSongTracks()
    {
        var tracks = new List<TrackInfo>();
        foreach (var json in _allSongsRaw)
        {
            var id = SafeGetLong(json, "id");
            var track = CreateTrackInfoFromSearchResult(id);
            if (track is not null)
                tracks.Add(track);
        }
        return tracks;
    }

    private void ClearSearch()
    {
        SearchText = string.Empty;
        ClearResults();
        HasSearched = false;
        StatusText = string.Empty;
        _lastKeyword = string.Empty;
    }

    private void ClearResults()
    {
        Songs.Clear();
        Albums.Clear();
        Artists.Clear();
        Playlists.Clear();
        _songDataCache.Clear();
        _allSongsRaw.Clear();
        _allAlbumsRaw.Clear();
        _allArtistsRaw.Clear();
        _allPlaylistsRaw.Clear();
        UpdateSectionFlags();
    }

    private void PopulateDisplaySection(List<JsonObject> rawItems, SearchItemType type, ObservableCollection<SearchItemInfo> target)
    {
        var itemsToShow = rawItems.Take(3);
        foreach (var item in itemsToShow)
        {
            try
            {
                var info = ParseSearchItem(item, type);
                if (info is not null) target.Add(info);
            }
            catch
            {
                // Skip malformed items
            }
        }
    }

    private void UpdateSectionFlags()
    {
        HasSongs = Songs.Count > 0;
        HasAlbums = Albums.Count > 0;
        HasArtists = Artists.Count > 0;
        HasPlaylists = Playlists.Count > 0;
        HasMoreSongs = _allSongsRaw.Count > 3;
        HasMoreAlbums = _allAlbumsRaw.Count > 3;
        HasMoreArtists = _allArtistsRaw.Count > 3;
        HasMorePlaylists = _allPlaylistsRaw.Count > 3;
    }

    private static SearchItemInfo? ParseSearchItem(JsonObject obj, SearchItemType type)
    {
        try
        {
            return type switch
            {
                SearchItemType.Song => new SearchItemInfo
                {
                    Id = SafeGetLong(obj, "id"),
                    Name = SafeGetString(obj, "name"),
                    CoverUrl = NormalizeUrl(SafeGetNestedString(obj, "al", "picUrl")),
                    Subtitle = FormatSongSubtitle(obj),
                    Type = SearchItemType.Song,
                },
                SearchItemType.Album => new SearchItemInfo
                {
                    Id = SafeGetLong(obj, "id"),
                    Name = SafeGetString(obj, "name"),
                    CoverUrl = NormalizeUrl(SafeGetString(obj, "picUrl")),
                    Subtitle = SafeGetNestedString(obj, "artist", "name"),
                    Type = SearchItemType.Album,
                },
                SearchItemType.Artist => new SearchItemInfo
                {
                    Id = SafeGetLong(obj, "id"),
                    Name = SafeGetString(obj, "name"),
                    CoverUrl = NormalizeUrl(SafeGetString(obj, "picUrl")),
                    Subtitle = TryGetAlias(obj),
                    Type = SearchItemType.Artist,
                },
                SearchItemType.Playlist => new SearchItemInfo
                {
                    Id = SafeGetLong(obj, "id"),
                    Name = SafeGetString(obj, "name"),
                    CoverUrl = NormalizeUrl(SafeGetString(obj, "coverImgUrl")),
                    Subtitle = $"{(SafeGetLong(obj, "trackCount"))} 首 · {SafeGetNestedString(obj, "creator", "nickname")}",
                    Type = SearchItemType.Playlist,
                },
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string SafeGetString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) ? node?.GetValue<string>() ?? string.Empty : string.Empty;
    }

    private static long SafeGetLong(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) ? node?.GetValue<long>() ?? 0 : 0;
    }

    private static string SafeGetNestedString(JsonObject obj, string parentKey, string childKey)
    {
        if (obj.TryGetPropertyValue(parentKey, out var parent) && parent is JsonObject parentObj)
        {
            return SafeGetString(parentObj, childKey);
        }
        return string.Empty;
    }

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
            url = "https:" + url;
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url["http://".Length..];
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url.TrimStart('/');
        return url;
    }

    private static string FormatSongSubtitle(JsonObject obj)
    {
        var album = SafeGetNestedString(obj, "al", "name");
        var artists = FormatArtists(obj["ar"]?.AsArray());
        if (string.IsNullOrEmpty(album) && string.IsNullOrEmpty(artists))
            return string.Empty;
        if (string.IsNullOrEmpty(album))
            return artists;
        if (string.IsNullOrEmpty(artists))
            return album;
        return $"{artists} · {album}";
    }

    private static string TryGetAlias(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("alias", out var node) && node is JsonArray arr && arr.Count > 0)
        {
            return string.Join(" / ", arr.Select(a => a?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0));
        }
        return string.Empty;
    }

    private static string FormatArtists(JsonArray? ar)
    {
        if (ar is null || ar.Count == 0) return string.Empty;
        return string.Join(" / ", ar.Select(a =>
        {
            if (a is JsonObject artistObj && artistObj.TryGetPropertyValue("name", out var nameNode))
            {
                return nameNode?.GetValue<string>() ?? string.Empty;
            }
            return string.Empty;
        }).Where(s => s.Length > 0));
    }
}
