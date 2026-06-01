using System.Collections.ObjectModel;
using YPM.Core.Models;
using YPM.Core.Mvvm;

namespace YPM.UI.ViewModels;

public sealed class ArtistViewModel : ObservableObject
{
    private long _artistId;
    private ArtistDetail? _artist;
    private AlbumDetail? _latestRelease;
    private bool _isLoading;
    private bool _isFollowed;
    private bool _isDescriptionExpanded;
    private string _errorMessage = string.Empty;
    private string _fullDescription = string.Empty;
    private List<AlbumDetail> _albumDetails = [];
    private List<AlbumDetail> _epDetails = [];

    public ArtistDetail? Artist
    {
        get => _artist;
        private set => SetProperty(ref _artist, value);
    }

    public AlbumDetail? LatestRelease
    {
        get => _latestRelease;
        private set => SetProperty(ref _latestRelease, value);
    }

    public ObservableCollection<TrackInfo> PopularTracks { get; } = [];

    public ObservableCollection<ArtistAlbumCardItem> Albums { get; } = [];

    public ObservableCollection<ArtistAlbumCardItem> EpsAndSingles { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsFollowed
    {
        get => _isFollowed;
        set => SetProperty(ref _isFollowed, value);
    }

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

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string FullDescription
    {
        get => _fullDescription;
        set => SetProperty(ref _fullDescription, value);
    }

    public bool HasArtist => Artist is not null;

    public string ArtistName => Artist?.Name ?? string.Empty;

    public string ArtistCoverUrl => Artist?.CoverUrl ?? string.Empty;

    public bool HasLatestRelease => LatestRelease is not null;

    public string LatestReleaseName => LatestRelease?.Name ?? string.Empty;

    public string LatestReleaseCoverUrl => LatestRelease?.CoverUrl ?? string.Empty;

    public bool HasPopularTracks => PopularTracks.Count > 0;

    public bool HasAlbums => Albums.Count > 0;

    public bool HasEpsAndSingles => EpsAndSingles.Count > 0;

    public bool HasDescription => !string.IsNullOrWhiteSpace(FullDescription);

    public bool HasLongDescription => (FullDescription?.Length ?? 0) > 120;

    public int DescriptionMaxLines => IsDescriptionExpanded ? 100 : 3;

    public string DescriptionExpandText => IsDescriptionExpanded ? "收起" : "展开";

    public string FollowText => IsFollowed ? "已关注" : "关注";

    public string FollowGlyph => IsFollowed ? "\uE8FB" : "\uE710";

    public string StatisticsText
    {
        get
        {
            if (Artist is null)
            {
                return string.Empty;
            }

            return $"{Artist.MusicSize} 首歌曲  /  {Artist.AlbumSize} 张专辑";
        }
    }

    public string IdentityText
    {
        get
        {
            if (Artist is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (Artist.Alias.Count > 0)
            {
                parts.Add(string.Join(" / ", Artist.Alias.Where(static a => !string.IsNullOrWhiteSpace(a))));
            }

            if (Artist.Identities.Count > 0)
            {
                parts.Add(string.Join(" / ", Artist.Identities.Where(static a => !string.IsNullOrWhiteSpace(a))));
            }

            return string.Join("  /  ", parts.Where(static p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    public string LatestReleaseDateText => LatestRelease?.PublishTime is > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(LatestRelease.PublishTime).ToString("yyyy-MM-dd")
        : string.Empty;

    public string LatestReleaseMetaText
    {
        get
        {
            if (LatestRelease is null)
            {
                return string.Empty;
            }

            var pieces = new List<string>();
            if (!string.IsNullOrWhiteSpace(LatestRelease.Type))
            {
                pieces.Add(LatestRelease.Type!);
            }

            if (LatestRelease.Size > 0)
            {
                pieces.Add($"{LatestRelease.Size} 首歌曲");
            }

            return string.Join(" / ", pieces);
        }
    }

    public async Task LoadAsync(long artistId)
    {
        if (IsLoading)
        {
            return;
        }

        _artistId = artistId;
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var detailTask = App.ApiClient.GetArtistDetailAsync(artistId);
            var topSongsTask = App.ApiClient.GetArtistTopSongsAsync(artistId);
            var albumsTask = App.ApiClient.GetArtistAlbumsAsync(artistId, limit: 200);
            var descTask = App.ApiClient.GetArtistDescAsync(artistId);

            await Task.WhenAll(detailTask, topSongsTask, albumsTask, descTask);

            var artist = await detailTask;
            var topSongs = await topSongsTask;
            var albums = (await albumsTask)
                .OrderByDescending(static a => a.PublishTime)
                .ToList();
            var fullDesc = await descTask;

            if (artist is null)
            {
                ErrorMessage = "歌手未找到";
                return;
            }

            Artist = artist;
            IsFollowed = artist.Followed;
            FullDescription = string.IsNullOrWhiteSpace(fullDesc) ? artist.BriefDesc ?? string.Empty : fullDesc;
            LatestRelease = albums.FirstOrDefault();

            ReplaceItems(PopularTracks, topSongs.Songs);

            _albumDetails = albums.Where(static a => !IsEpOrSingle(a)).ToList();
            _epDetails = albums.Where(IsEpOrSingle).ToList();
            var normalAlbums = _albumDetails.Select(MapAlbumCardItem).ToList();
            var eps = _epDetails.Select(MapAlbumCardItem).ToList();
            ReplaceItems(Albums, normalAlbums);
            ReplaceItems(EpsAndSingles, eps);

            OnPropertyChanged(nameof(HasArtist));
            OnPropertyChanged(nameof(ArtistName));
            OnPropertyChanged(nameof(ArtistCoverUrl));
            OnPropertyChanged(nameof(HasLatestRelease));
            OnPropertyChanged(nameof(LatestReleaseName));
            OnPropertyChanged(nameof(LatestReleaseCoverUrl));
            OnPropertyChanged(nameof(HasPopularTracks));
            OnPropertyChanged(nameof(HasAlbums));
            OnPropertyChanged(nameof(HasEpsAndSingles));
            OnPropertyChanged(nameof(HasDescription));
            OnPropertyChanged(nameof(HasLongDescription));
            OnPropertyChanged(nameof(StatisticsText));
            OnPropertyChanged(nameof(IdentityText));
            OnPropertyChanged(nameof(FollowText));
            OnPropertyChanged(nameof(FollowGlyph));
            OnPropertyChanged(nameof(LatestReleaseDateText));
            OnPropertyChanged(nameof(LatestReleaseMetaText));
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

    public async Task ToggleFollowAsync()
    {
        try
        {
            var newState = !IsFollowed;
            await App.ApiClient.SubscribeArtistAsync(_artistId, newState);
            IsFollowed = newState;
            if (Artist is not null)
            {
                Artist.Followed = newState;
            }

            OnPropertyChanged(nameof(FollowText));
            OnPropertyChanged(nameof(FollowGlyph));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task PlayPopularSongsAsync()
    {
        if (PopularTracks.Count == 0 || App.AudioPlayer is not { } player)
        {
            return;
        }

        player.SetQueue(PopularTracks.ToList(), 0);
        await player.PlayAsync(0);
    }

    public async Task PlayTrackAsync(TrackInfo track)
    {
        if (App.AudioPlayer is not { } player)
        {
            return;
        }

        var tracks = PopularTracks.ToList();
        var index = tracks.FindIndex(item => item.Id == track.Id);
        if (index < 0)
        {
            tracks = PopularTracks
                .Concat(_albumDetails.SelectMany(static a => a.Tracks))
                .Concat(_epDetails.SelectMany(static a => a.Tracks))
                .DistinctBy(static t => t.Id)
                .ToList();
            index = tracks.FindIndex(item => item.Id == track.Id);
        }

        if (index < 0)
        {
            return;
        }

        player.SetQueue(tracks, index);
        await player.PlayAsync(index);
    }

    private static bool IsEpOrSingle(AlbumDetail album)
    {
        var type = $"{album.Type} {album.SubType}".ToLowerInvariant();
        return type.Contains("ep") || type.Contains("single");
    }

    private static ArtistAlbumCardItem MapAlbumCardItem(AlbumDetail album)
    {
        var meta = DateTimeOffset.FromUnixTimeMilliseconds(album.PublishTime).ToString("yyyy");
        if (!string.IsNullOrWhiteSpace(album.Type))
        {
            meta = $"{meta} / {album.Type}";
        }

        return new ArtistAlbumCardItem
        {
            Id = album.Id,
            Name = album.Name,
            CoverUrl = album.CoverUrl,
            MetaText = meta,
        };
    }

    private static void ReplaceItems<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}

public sealed class ArtistAlbumCardItem
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public string MetaText { get; set; } = string.Empty;
}
