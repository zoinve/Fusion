using System.Collections.ObjectModel;
using YPM.Core.Models;
using YPM.Core.Mvvm;
using YPM.Core.Services;
using YPM.UI.Helpers;

namespace YPM.UI.ViewModels;

public sealed class PlaylistViewModel : ObservableObject, IDisposable
{
    private readonly ILikedSongsService? _likedService;
    private long _playlistId;
    private PlaylistDetail? _playlist;
    private bool _isLoading;
    private bool _isSubscribed;
    private string _errorMessage = string.Empty;
    private string _searchQuery = string.Empty;
    private bool _isLoadingMore;
    private int _loadedTrackCount;
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
                ApplyFilter();
        }
    }

    public bool HasMoreTracks => _playlist is not null && _loadedTrackCount < _playlist.TrackCount;

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
            var playlist = await App.ApiClient.GetPlaylistDetailAsync(playlistId);
            if (playlist is null)
            {
                ErrorMessage = "歌单未找到";
                return;
            }

            Playlist = playlist;
            IsSubscribed = playlist.Subscribed;

            AllTracks.Clear();
            _loadedTrackCount = 0;

            var initialTracks = await App.ApiClient.GetPlaylistAllTracksAsync(playlistId, LoadMorePageSize, 0);
            if (initialTracks.Count == 0 && playlist.Tracks.Count > 0)
                initialTracks = playlist.Tracks.Take(LoadMorePageSize).ToList();

            AppendTracks(initialTracks);

            ApplyFilter();
            OnPropertyChanged(nameof(PlayCountText));
            OnPropertyChanged(nameof(HasMoreTracks));
            OnPropertyChanged(nameof(HasCreator));
            OnPropertyChanged(nameof(HasDescription));
            OnPropertyChanged(nameof(HasLongDescription));
            OnPropertyChanged(nameof(SubscribeGlyph));
            OnPropertyChanged(nameof(SubscribeText));
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

    public async Task LoadMoreAsync()
    {
        if (!HasMoreTracks || IsLoadingMore) return;
        IsLoadingMore = true;

        try
        {
            var tracks = await App.ApiClient.GetPlaylistAllTracksAsync(_playlistId, LoadMorePageSize, _loadedTrackCount);
            if (tracks.Count == 0)
            {
                _loadedTrackCount = (int)(_playlist?.TrackCount ?? _loadedTrackCount);
                OnPropertyChanged(nameof(HasMoreTracks));
                return;
            }

            AppendTracks(tracks);

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

        foreach (var track in AllTracks)
        {
            if (string.IsNullOrEmpty(query) ||
                track.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                track.ArtistsText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (track.Album?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                track.DisplayIndex = index++;
                FilteredTracks.Add(track);
            }
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

            AllTracks.Add(track);
        }

        _loadedTrackCount = AllTracks.Count;
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
