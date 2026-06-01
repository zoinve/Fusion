namespace YPM.Core.Models;

public sealed class HomePageBlockResult
{
    public List<HomePageBlock> Blocks { get; set; } = [];

    public bool HasMore { get; set; }

    public string? Cursor { get; set; }
}

public sealed class HomePageBlock
{
    public string? BlockCode { get; set; }

    public string? ShowType { get; set; }

    public string? UiElement { get; set; }

    public object? ExtInfo { get; set; }

    public object? Creatives { get; set; }
}

public sealed class HomePageDragonBall
{
    public List<DragonBallItem> Data { get; set; } = [];
}

public sealed class DragonBallItem
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? IconUrl { get; set; }
}
