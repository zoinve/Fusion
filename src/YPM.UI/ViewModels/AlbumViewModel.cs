using System.Collections.ObjectModel;
using YPM.Core.Models;
using YPM.Core.Mvvm;
using YPM.Core.Services;
using YPM.UI.Helpers;

namespace YPM.UI.ViewModels;

public sealed class AlbumViewModel : ObservableObject, IDisposable
{
    private readonly ILikedSongsService? _likedService;
    private long _albumId;
    private AlbumDetail? _album;
    private bool _isLoading;
    private bool _isSubscribed;
    private string _errorMessage = string.Empty;

    public AlbumDetail? Album
    {
        get => _album;
        private set => SetProperty(ref _album, value);
    }

    public ObservableCollection<TrackGroup> DiscGroups { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
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

    public string PublishDateText => _album?.PublishTime is > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(_album.PublishTime).ToString("yyyy-MM-dd")
        : string.Empty;

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

    public bool HasLongDescription => (_album?.Description?.Length ?? 0) > 80;

    public string DescriptionExpandText => IsDescriptionExpanded ? "收起" : "展开";

    public bool HasDescription => !string.IsNullOrWhiteSpace(_album?.Description);

    public string AlbumInfo => $"{PublishDateText} · {_album?.Size ?? 0} 首歌" + (string.IsNullOrEmpty(_album?.Company) ? "" : $" · {_album!.Company}");

    public AlbumViewModel()
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

    public async Task LoadAsync(long albumId)
    {
        if (IsLoading) return;
        _albumId = albumId;
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var albumTask = App.ApiClient.GetAlbumDetailAsync(albumId);
            var dynamicTask = App.ApiClient.GetAlbumDynamicAsync(albumId);

            await Task.WhenAll(albumTask, dynamicTask);

            var album = await albumTask;
            var dynamic = await dynamicTask;

            if (album is null)
            {
                ErrorMessage = "专辑未找到";
                return;
            }

            Album = album;
            IsSubscribed = album.Subscribed;

            if (dynamic is not null)
            {
                IsSubscribed = dynamic.IsSub;
            }

            BuildDiscGroups(album.Tracks);
            OnPropertyChanged(nameof(SubscribeGlyph));
            OnPropertyChanged(nameof(SubscribeText));
            OnPropertyChanged(nameof(PublishDateText));
            OnPropertyChanged(nameof(AlbumInfo));
            OnPropertyChanged(nameof(HasDescription));
            OnPropertyChanged(nameof(HasLongDescription));
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

    public async Task ToggleSubscribeAsync()
    {
        try
        {
            var newState = !IsSubscribed;
            await App.ApiClient.SubscribeAlbumAsync(_albumId, newState);
            IsSubscribed = newState;
            if (_album is not null) _album.Subscribed = newState;
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
        if (_album?.Tracks is not { Count: > 0 }) return;

        if (App.AudioPlayer is { } player)
        {
            player.SetQueue(_album.Tracks, 0);
            await player.PlayAsync(0);
        }
    }

    public async Task PlayTrackAsync(TrackInfo track)
    {
        if (_album?.Tracks is not { Count: > 0 } tracks)
        {
            return;
        }

        var index = tracks.FindIndex(item => item.Id == track.Id);
        if (index < 0 || App.AudioPlayer is not { } player)
        {
            return;
        }

        player.SetQueue(tracks, index);
        await player.PlayAsync(index);
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

    private void BuildDiscGroups(List<TrackInfo> tracks)
    {
        DiscGroups.Clear();
        if (tracks.Count == 0) return;

        var groups = tracks
            .OrderBy(t => t.DiscNumber)
            .ThenBy(t => t.TrackNumber)
            .GroupBy(t => t.DiscNumber)
            .Select(g =>
            {
                var index = 1;
                foreach (var track in g)
                {
                    track.DisplayIndex = index++;
                    track.IsLiked = _likedService?.IsLiked(track.Id) ?? track.IsLiked;
                }
                return new TrackGroup
                {
                    DiscNumber = g.Key,
                    Tracks = new ObservableCollection<TrackInfo>(g),
                };
            });

        foreach (var group in groups)
            DiscGroups.Add(group);
    }

    private void OnTrackLikedChanged(object? sender, long trackId)
    {
        foreach (var group in DiscGroups)
        {
            foreach (var track in group.Tracks)
            {
                if (track.Id == trackId)
                {
                    track.IsLiked = _likedService?.IsLiked(trackId) ?? track.IsLiked;
                    return;
                }
            }
        }
    }

    private void OnLikedRefreshed(object? sender, EventArgs e)
    {
        foreach (var group in DiscGroups)
        {
            foreach (var track in group.Tracks)
            {
                track.IsLiked = _likedService?.IsLiked(track.Id) ?? track.IsLiked;
            }
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

public sealed class TrackGroup : ObservableObject
{
    private int _discNumber;
    private ObservableCollection<TrackInfo> _tracks = [];

    public int DiscNumber
    {
        get => _discNumber;
        set
        {
            if (SetProperty(ref _discNumber, value))
            {
                OnPropertyChanged(nameof(Header));
                OnPropertyChanged(nameof(HasHeader));
            }
        }
    }

    public ObservableCollection<TrackInfo> Tracks
    {
        get => _tracks;
        set
        {
            if (SetProperty(ref _tracks, value))
            {
                OnPropertyChanged(nameof(Header));
                OnPropertyChanged(nameof(HasHeader));
            }
        }
    }

    public string Header => Tracks.Count > 1 || DiscNumber > 1 ? $"Disc {DiscNumber}" : "";

    public bool HasHeader => !string.IsNullOrWhiteSpace(Header);
}
