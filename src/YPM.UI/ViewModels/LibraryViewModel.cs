using System.Collections.ObjectModel;
using YPM.Core.Models;
using YPM.Core.Mvvm;
using YPM.Core.Services;

namespace YPM.UI.ViewModels;

public sealed class LibraryViewModel : ObservableObject
{
    private readonly ILikedSongsService? _likedService;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private int _selectedTabIndex;

    public ObservableCollection<PlaylistSummary> Playlists { get; } = [];
    public ObservableCollection<AlbumDetail> Albums { get; } = [];
    public ObservableCollection<ArtistSummary> Artists { get; } = [];
    public ObservableCollection<MvInfo> Mvs { get; } = [];
    public ObservableCollection<TrackInfo> CloudDiskTracks { get; } = [];
    public LibraryViewModel()
    {
        _likedService = App.LikedSongsService;
        if (_likedService is not null)
        {
            _likedService.Refreshed += OnLikedRefreshed;
        }
    }

    public ObservableCollection<RecordItem> HistoryWeek { get; } = [];
    public ObservableCollection<RecordItem> HistoryAll { get; } = [];

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

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public UserProfile? CurrentUser => App.Settings.CurrentUser;
    public bool IsLoggedIn => CurrentUser is not null;

    public string UserName => CurrentUser?.Nickname ?? "未登录";
    public string UserAvatar => CurrentUser?.AvatarUrl ?? "";

    private long LikedSongsCount { get; set; }
    public string LikedSongsPlaylistId { get; private set; } = "";
    public string LikedSongsText => $"我喜欢的音乐 ({LikedSongsCount:N0} 首)";
    public bool HasLikedSongs => LikedSongsCount > 0;
    public bool HasLikedSongsCovers => LikedSongsCovers.Count > 0;
    public ObservableCollection<string> LikedSongsCovers { get; } = [];
    public string LikedSongsLatestCoverUrl { get; private set; } = "";

    public async Task LoadAsync()
    {
        if (IsLoading || !IsLoggedIn) return;
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var uid = CurrentUser!.UserId;
            var errors = new List<string>();

            await Task.WhenAll(
                LoadSectionAsync(() => LoadPlaylistsAsync(uid), "歌单", errors),
                LoadSectionAsync(LoadAlbumsAsync, "专辑", errors),
                LoadSectionAsync(LoadArtistsAsync, "艺人", errors),
                LoadSectionAsync(LoadMvsAsync, "MV", errors),
                LoadSectionAsync(LoadCloudDiskAsync, "云盘", errors),
                LoadSectionAsync(() => LoadHistoryAsync(uid), "听歌排行", errors));

            OnPropertyChanged(nameof(LikedSongsText));
            OnPropertyChanged(nameof(HasLikedSongs));

            if (errors.Count > 0)
            {
                ErrorMessage = string.Join("；", errors);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static async Task LoadSectionAsync(Func<Task> loader, string sectionName, List<string> errors)
    {
        try
        {
            await loader();
        }
        catch (Exception ex)
        {
            lock (errors)
            {
                errors.Add($"{sectionName}加载失败: {ex.Message}");
            }
        }
    }

    private async Task LoadPlaylistsAsync(long uid)
    {
        var result = await App.ApiClient.GetUserPlaylistAsync(uid);
        Playlists.Clear();
        if (result.Playlist.Count > 0)
        {
            LikedSongsPlaylistId = result.Playlist[0].Id.ToString();

            foreach (var item in result.Playlist)
                Playlists.Add(item);

            await RefreshLikedSongsAsync();
        }
    }

    private void OnLikedRefreshed(object? sender, EventArgs e)
    {
        // Only for like-button state changes — do NOT update count/covers from cache.
        // Those always come from the API via LoadPlaylistsAsync.
    }

    public async Task RefreshLikedSongsAsync()
    {
        if (string.IsNullOrEmpty(LikedSongsPlaylistId)) return;

        try
        {
            var likedPlaylist = await App.ApiClient.GetPlaylistDetailAsync(long.Parse(LikedSongsPlaylistId));
            LikedSongsCount = likedPlaylist?.TrackCount ?? 0;
            OnPropertyChanged(nameof(LikedSongsText));
            OnPropertyChanged(nameof(HasLikedSongs));

            LikedSongsCovers.Clear();
            LikedSongsLatestCoverUrl = "";

            if (likedPlaylist?.Tracks is { Count: > 0 })
            {
                // First track is the most recently liked song
                var latestTrack = likedPlaylist.Tracks[0];
                LikedSongsLatestCoverUrl = latestTrack.Album?.CoverUrl ?? "";
                OnPropertyChanged(nameof(LikedSongsLatestCoverUrl));

                // Populate 2x2 grid with up to 4 most recent tracks' album covers
                foreach (var t in likedPlaylist.Tracks.Take(4))
                {
                    if (!string.IsNullOrWhiteSpace(t.Album?.CoverUrl))
                        LikedSongsCovers.Add(t.Album.CoverUrl);
                }
            }

            OnPropertyChanged(nameof(HasLikedSongsCovers));
            OnPropertyChanged(nameof(LikedSongsCovers));
        }
        catch { }
    }

    private async Task LoadAlbumsAsync()
    {
        var albums = await App.ApiClient.GetSublistAlbumsAsync(limit: 30);
        Albums.Clear();
        foreach (var album in albums)
            Albums.Add(album);
    }

    private async Task LoadArtistsAsync()
    {
        var artists = await App.ApiClient.GetSublistArtistsAsync(limit: 30);
        Artists.Clear();
        foreach (var artist in artists)
            Artists.Add(artist);
    }

    private async Task LoadMvsAsync()
    {
        var mvs = await App.ApiClient.GetSublistMvsAsync(limit: 20);
        Mvs.Clear();
        foreach (var mv in mvs)
            Mvs.Add(mv);
    }

    private async Task LoadCloudDiskAsync()
    {
        var result = await App.ApiClient.GetUserCloudAsync(limit: 50);
        CloudDiskTracks.Clear();
        if (result.Data is { Count: > 0 })
        {
            foreach (var track in result.Data)
                CloudDiskTracks.Add(track);
        }
    }

    private async Task LoadHistoryAsync(long uid)
    {
        var allTask = App.ApiClient.GetUserRecordAsync(uid, type: 0);
        var weekTask = App.ApiClient.GetUserRecordAsync(uid, type: 1);

        await Task.WhenAll(allTask, weekTask);

        var all = await allTask;
        var week = await weekTask;

        HistoryWeek.Clear();
        foreach (var item in week.WeekData.Where(i => i.Song is not null))
            HistoryWeek.Add(item);

        HistoryAll.Clear();
        foreach (var item in all.AllData.Where(i => i.Song is not null))
            HistoryAll.Add(item);
    }

    public async Task PlayLikedSongsAsync()
    {
        if (string.IsNullOrEmpty(LikedSongsPlaylistId) || App.AudioPlayer is null) return;

        var playlist = await App.ApiClient.GetPlaylistDetailAsync(long.Parse(LikedSongsPlaylistId));
        if (playlist?.Tracks is { Count: > 0 })
        {
            App.AudioPlayer.SetQueue(playlist.Tracks, 0);
            await App.AudioPlayer.PlayAsync(0);
        }
    }

    public async Task PlayTrackAsync(TrackInfo track)
    {
        if (App.AudioPlayer is null) return;
        var result = await App.ApiClient.GetTrackDetailAsync(track.Id.ToString());
        var tracks = result.Songs;
        if (tracks.Count == 0) return;
        App.AudioPlayer.SetQueue(tracks, 0);
        await App.AudioPlayer.PlayAsync(0);
    }

    public async Task PlayHistoryAsync(ObservableCollection<RecordItem> records, int startIndex)
    {
        if (App.AudioPlayer is null || records.Count == 0) return;
        var tracks = records.Select(r => r.Song).Where(s => s is not null).Cast<TrackInfo>().ToList();
        if (tracks.Count == 0) return;
        App.AudioPlayer.SetQueue(tracks, Math.Min(startIndex, tracks.Count - 1));
        await App.AudioPlayer.PlayAsync(Math.Min(startIndex, tracks.Count - 1));
    }

    public async Task PlayCloudTrackAsync(TrackInfo track)
    {
        if (App.AudioPlayer is null) return;
        App.AudioPlayer.SetQueue(new List<TrackInfo> { track }, 0);
        await App.AudioPlayer.PlayAsync(0);
    }
}
