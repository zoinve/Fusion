namespace YPM.Core.Models;

public sealed class QrLoginStatus
{
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Cookie { get; set; }

    public bool IsCompleted => Code == 803;
}
