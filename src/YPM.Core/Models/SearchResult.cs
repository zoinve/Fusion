namespace YPM.Core.Models;

public sealed class SearchResult
{
    public SearchSection? Songs { get; set; }

    public SearchSection? Albums { get; set; }

    public SearchSection? Artists { get; set; }

    public SearchSection? Playlists { get; set; }

    public SearchSection? Mvs { get; set; }

    public SearchSection? Videos { get; set; }

    public SearchSection? DjRadios { get; set; }

    public SearchSection? Users { get; set; }

    public int TotalCount { get; set; }
}

public sealed class SearchSection
{
    public bool More { get; set; }

    public int Total { get; set; }

    public List<object> Items { get; set; } = [];
}

public sealed class SearchDefaultResult
{
    public string? RealKeyword { get; set; }

    public int SearchType { get; set; }
}

public sealed class SearchHotItem
{
    public string? SearchWord { get; set; }

    public int Score { get; set; }

    public string? Content { get; set; }

    public int IconType { get; set; }
}

public sealed class SearchSuggestResult
{
    public SearchSuggestSection? Albums { get; set; }

    public SearchSuggestSection? Artists { get; set; }

    public SearchSuggestSection? Songs { get; set; }

    public SearchSuggestSection? Playlists { get; set; }

    public List<string>? Order { get; set; }
}

public sealed class SearchSuggestSection
{
    public List<SearchSuggestItem> Items { get; set; } = [];
}

public sealed class SearchSuggestItem
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ArtistSummary? Artist { get; set; }

    public List<ArtistSummary>? Artists { get; set; }

    public AlbumSummary? Album { get; set; }
}
