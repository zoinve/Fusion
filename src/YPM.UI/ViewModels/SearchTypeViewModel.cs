using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using YPM.Core.Models;
using YPM.Core.Mvvm;

namespace YPM.UI.ViewModels;

public sealed class SearchTypeViewModel : ObservableObject
{
    private string _keyword = string.Empty;
    private string _typeKey = string.Empty;
    private string _statusText = string.Empty;
    private string _typeDisplayName = string.Empty;
    private bool _isLoading;
    private bool _hasMore = true;
    private int _offset;

    public ObservableCollection<SearchItemInfo> Results { get; } = [];

    public AsyncRelayCommand LoadMoreCommand { get; }

    public SearchTypeViewModel()
    {
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync);
    }

    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    public string TypeKey
    {
        get => _typeKey;
        set
        {
            if (SetProperty(ref _typeKey, value))
            {
                OnPropertyChanged(nameof(IsTrackType));
                OnPropertyChanged(nameof(IsNotTrackType));
            }
        }
    }

    public string TypeDisplayName
    {
        get => _typeDisplayName;
        set => SetProperty(ref _typeDisplayName, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool HasMore
    {
        get => _hasMore;
        set => SetProperty(ref _hasMore, value);
    }

    public bool HasResults => Results.Count > 0;

    public bool IsTrackType => _typeKey == "tracks";
    public bool IsNotTrackType => _typeKey != "tracks";

    public async Task LoadAsync(string keyword, string typeKey)
    {
        Keyword = keyword;
        TypeKey = typeKey;
        TypeDisplayName = typeKey switch
        {
            "tracks" => "歌曲",
            "albums" => "专辑",
            "artists" => "歌手",
            "playlists" => "歌单",
            _ => typeKey,
        };

        Results.Clear();
        _offset = 0;
        HasMore = true;
        StatusText = string.Empty;

        await LoadMoreAsync();
    }

    private async Task LoadMoreAsync()
    {
        if (IsLoading || !HasMore) return;

        IsLoading = true;

        try
        {
            var typeCode = TypeKey switch
            {
                "tracks" => 1,
                "albums" => 10,
                "artists" => 100,
                "playlists" => 1000,
                _ => 1,
            };

            var result = await App.ApiClient.CloudSearchAsync(Keyword, typeCode, limit: 30, offset: _offset);

            var searchItemType = TypeKey switch
            {
                "tracks" => SearchItemType.Song,
                "albums" => SearchItemType.Album,
                "artists" => SearchItemType.Artist,
                "playlists" => SearchItemType.Playlist,
                _ => SearchItemType.Song,
            };

            var items = GetItemsFromResult(result, TypeKey);
            if (items is not null)
            {
                foreach (var item in items)
                {
                    if (item is JsonObject json)
                    {
                        var info = ParseSearchTypeItem(json, searchItemType);
                        if (info is not null)
                            Results.Add(info);
                    }
                }
            }

            var total = GetTotalFromResult(result, TypeKey);
            _offset = Results.Count;
            HasMore = total > Results.Count;
            StatusText = total > 0 ? $"共 {total} 个结果" : "未找到结果";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasResults));
        }
    }

    private static List<object>? GetItemsFromResult(SearchResult result, string typeKey)
    {
        return typeKey switch
        {
            "tracks" => result.Songs?.Items,
            "albums" => result.Albums?.Items,
            "artists" => result.Artists?.Items,
            "playlists" => result.Playlists?.Items,
            _ => null,
        };
    }

    private static int GetTotalFromResult(SearchResult result, string typeKey)
    {
        return typeKey switch
        {
            "tracks" => result.Songs?.Total ?? 0,
            "albums" => result.Albums?.Total ?? 0,
            "artists" => result.Artists?.Total ?? 0,
            "playlists" => result.Playlists?.Total ?? 0,
            _ => 0,
        };
    }

    private static SearchItemInfo? ParseSearchTypeItem(JsonObject obj, SearchItemType type)
    {
        try
        {
            return type switch
            {
                SearchItemType.Song => new SearchItemInfo
                {
                    Id = SafeGetLong(obj, "id"),
                    Name = SafeGetString(obj, "name"),
                    AliasText = TryGetSongAlias(obj),
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

    private static string TryGetAlias(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("alias", out var node) && node is JsonArray arr && arr.Count > 0)
        {
            return string.Join(" / ", arr.Select(a => a?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0));
        }
        return string.Empty;
    }

    private static string TryGetSongAlias(JsonObject obj)
    {
        static IEnumerable<string> Read(JsonNode? node)
        {
            return node is JsonArray arr
                ? arr.Select(a => a?.GetValue<string>() ?? string.Empty).Where(static s => s.Length > 0)
                : [];
        }

        foreach (var key in new[] { "alia", "alias", "tns" })
        {
            if (obj.TryGetPropertyValue(key, out var node))
            {
                var aliases = Read(node).ToList();
                if (aliases.Count > 0)
                {
                    return string.Join(" / ", aliases);
                }
            }
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
