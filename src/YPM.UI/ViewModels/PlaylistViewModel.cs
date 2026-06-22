using System.Collections.ObjectModel;
using YPM.Core.Models;
using YPM.Core.Mvvm;
using YPM.Core.Services;
using YPM.UI.Helpers;

namespace YPM.UI.ViewModels;

public sealed class PlaylistViewModel : ObservableObject, IDisposable
{
    private readonly ILikedSongsService? _likedService;
    private readonly List<TrackInfo> _displayTracks = [];
    private long _playlistId;
    private PlaylistDetail? _playlist;
    private bool _isLoading;
    private bool _isSubscribed;
    private string _errorMessage = string.Empty;
    private string _searchQuery = string.Empty;
    private bool _isLoadingMore;
    private int _visibleTrackCount;
    private long _lastCachedTrackCount;
    private const int LoadMorePageSize = 50;

    public PlaylistDetail? Playlist
    {
        get => _playlist;
        private set => SetProperty(ref _playlist, value);
    }

    public ObservableCollection<TrackInfo> FilteredTracks { get; } = [];

    public ObservableCollection<TrackInfo> AllTracks { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        set => SetProperty(ref _isLoadingMore, value);
    }

    public bool IsSubscribed
    {
        get => _isSubscribed;
        set => SetProperty(ref _isSubscribed, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(HasMoreTracks));
            }
        }
    }

    public bool HasMoreTracks => string.IsNullOrWhiteSpace(SearchQuery) && _visibleTrackCount < _displayTracks.Count;

    public PlaylistViewModel()
    {
        _likedService = App.LikedSongsService;
        if (_likedService is not null)
        {
            _likedService.TrackLiked += OnTrackLikedChanged;
            _likedService.TrackUnliked += OnTrackLikedChanged;
            _likedService.Refreshed += OnLikedRefreshed;
        }
    }

    public string SubscribeGlyph => IsSubscribed ? IconGlyph.HeartSolid : IconGlyph.Heart;
    public string SubscribeText => IsSubscribed ? "已收藏" : "收藏";

    private bool _isDescriptionExpanded;

    public bool IsDescriptionExpanded
    {
        get => _isDescriptionExpanded;
        set
        {
            if (SetProperty(ref _isDescriptionExpanded, value))
            {
                OnPropertyChanged(nameof(DescriptionMaxLines));
                OnPropertyChanged(nameof(DescriptionExpandText));
            }
        }
    }

    public int DescriptionMaxLines => IsDescriptionExpanded ? 100 : 1;

    public bool HasLongDescription => (_playlist?.Description?.Length ?? 0) > 80;

    public string DescriptionExpandText => IsDescriptionExpanded ? "收起" : "展开";

    public bool HasCreator => _playlist?.Creator is not null;
    public bool HasDescription => !string.IsNullOrWhiteSpace(_playlist?.Description);

    public string PlayCountText => _playlist?.PlayCount switch
    {
        > 100000000 => $"{_playlist.PlayCount / 100000000.0:F1}亿",
        > 10000 => $"{_playlist.PlayCount / 10000.0:F0}万",
        _ => _playlist?.PlayCount.ToString() ?? "0",
    };

    public async Task LoadAsync(long playlistId)
    {
        if (IsLoading) return;
        _playlistId = playlistId;
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // Step 1: Show cached playlist header immediately (fast path from cache)
            var cachedPlaylist = await App.ApiClient.GetPlaylistDetailAsync(playlistId);
            var showedCached = false;

            if (cachedPlaylist is not null)
            {
                DisplayPlaylistHeader(cachedPlaylist);
                _lastCachedTrackCount = cachedPlaylist.TrackCount;
                showedCached = true;

                // Step 2: Try to load cached tracks (now cacheable)
                var cachedTracks = await App.ApiClient.GetPlaylistAllTracksAsync(playlistId);
                if (cachedTracks.Count > 0)
                {
                    ReplaceAllTracks(cachedTracks);
                }
                else if (cachedPlaylist.Tracks.Count > 0)
                {
                    ReplaceAllTracks(cachedPlaylist.Tracks.ToList());
                }
            }

            // Step 3: Fetch fresh playlist detail to check for changes
            var freshPlaylist = await App.ApiClient.GetPlaylistDetailAsync(playlistId, skipCache: true);
            if (freshPlaylist is null)
            {
                if (!showedCached)
                    ErrorMessage = "歌单未找到";
                return;
            }

            // Step 4: Check if track count changed since cached version
            var needRefreshTracks = !showedCached || freshPlaylist.TrackCount != _lastCachedTrackCount;

            // Update header with fresh data
            DisplayPlaylistHeader(freshPlaylist);

            // Step 5: Re-fetch tracks only if track count changed or no cached data
            if (needRefreshTracks)
            {
                var tracks = await App.ApiClient.GetPlaylistAllTracksAsync(playlistId, skipCache: true);
                if (tracks.Count == 0 && freshPlaylist.Tracks.Count > 0)
                    tracks = freshPlaylist.Tracks.ToList();
                ReplaceAllTracks(tracks);
            }
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

    private void DisplayPlaylistHeader(PlaylistDetail playlist)
    {
        Playlist = playlist;
        IsSubscribed = playlist.Subscribed;

        OnPropertyChanged(nameof(PlayCountText));
        OnPropertyChanged(nameof(HasCreator));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(HasLongDescription));
        OnPropertyChanged(nameof(SubscribeGlyph));
        OnPropertyChanged(nameof(SubscribeText));
    }

    private void ReplaceAllTracks(List<TrackInfo> tracks)
    {
        AllTracks.Clear();
        _displayTracks.Clear();
        _visibleTrackCount = 0;

        AppendTracks(tracks);
        RevealMoreTracks();
        ApplyFilter();
        OnPropertyChanged(nameof(HasMoreTracks));
    }

    public Task LoadMoreAsync()
    {
        if (!HasMoreTracks || IsLoadingMore) return Task.CompletedTask;
        IsLoadingMore = true;

        try
        {
            RevealMoreTracks();
            ApplyFilter();
            OnPropertyChanged(nameof(HasMoreTracks));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingMore = false;
        }

        return Task.CompletedTask;
    }

    public async Task ToggleSubscribeAsync()
    {
        try
        {
            var newState = !IsSubscribed;
            await App.ApiClient.SubscribePlaylistAsync(_playlistId, newState);
            IsSubscribed = newState;
            if (_playlist is not null) _playlist.Subscribed = newState;
            OnPropertyChanged(nameof(SubscribeGlyph));
            OnPropertyChanged(nameof(SubscribeText));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task PlayAllAsync()
    {
        if (AllTracks.Count == 0) return;

        if (App.AudioPlayer is { } player)
        {
            player.SetQueue(AllTracks.ToList(), 0);
            await player.PlayAsync(0);
        }
    }

    public async Task PlayTrackAsync(TrackInfo track)
    {
        var index = AllTracks.IndexOf(track);
        if (index < 0 || App.AudioPlayer is not { } player)
        {
            return;
        }

        player.SetQueue(AllTracks.ToList(), index);
        await player.PlayAsync(index);
    }

    private void ApplyFilter()
    {
        FilteredTracks.Clear();
        var query = _searchQuery?.Trim() ?? "";
        var index = 1;
        var sourceTracks = string.IsNullOrEmpty(query)
            ? _displayTracks.Take(_visibleTrackCount)
            : AllTracks.Where(track =>
                track.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                track.ArtistsText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (track.Album?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var track in sourceTracks)
        {
            EnsureCoverLoaded(track);
            track.DisplayIndex = index++;
            FilteredTracks.Add(track);
        }
    }

    public async Task ToggleTrackLikeAsync(TrackInfo track)
    {
        if (_likedService is null) return;

        var isLiked = _likedService.IsLiked(track.Id);
        if (isLiked)
            await _likedService.UnlikeAsync(track.Id);
        else
            await _likedService.LikeAsync(track.Id);
    }

    private void AppendTracks(IEnumerable<TrackInfo> tracks)
    {
        foreach (var track in tracks)
        {
            if (AllTracks.Any(existing => existing.Id == track.Id))
            {
                continue;
            }

            if (_likedService is not null)
                track.IsLiked = _likedService.IsLiked(track.Id);

            track.ListCoverUrl = string.Empty;
            AllTracks.Add(track);
            _displayTracks.Add(track);
        }
    }

    private void RevealMoreTracks()
    {
        if (_displayTracks.Count == 0)
        {
            _visibleTrackCount = 0;
            return;
        }

        var nextVisibleCount = Math.Min(_visibleTrackCount + LoadMorePageSize, _displayTracks.Count);
        for (var i = _visibleTrackCount; i < nextVisibleCount; i++)
        {
            EnsureCoverLoaded(_displayTracks[i]);
        }

        _visibleTrackCount = nextVisibleCount;
    }

    private static void EnsureCoverLoaded(TrackInfo track)
    {
        if (!string.IsNullOrWhiteSpace(track.ListCoverUrl))
        {
            return;
        }

        track.ListCoverUrl = track.Album?.CoverUrl ?? string.Empty;
    }

    private void OnTrackLikedChanged(object? sender, long trackId)
    {
        foreach (var track in AllTracks)
        {
            if (track.Id == trackId)
            {
                track.IsLiked = _likedService?.IsLiked(trackId) ?? track.IsLiked;
                break;
            }
        }
    }

    private void OnLikedRefreshed(object? sender, EventArgs e)
    {
        foreach (var track in AllTracks)
        {
            track.IsLiked = _likedService?.IsLiked(track.Id) ?? track.IsLiked;
        }
    }

    public void Dispose()
    {
        if (_likedService is not null)
        {
            _likedService.TrackLiked -= OnTrackLikedChanged;
            _likedService.TrackUnliked -= OnTrackLikedChanged;
            _likedService.Refreshed -= OnLikedRefreshed;
        }
    }
}
