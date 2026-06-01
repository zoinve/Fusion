namespace YPM.Core.Models;

public sealed class QrLoginSession
{
    public string Key { get; set; } = string.Empty;

    public string QrImageBase64 { get; set; } = string.Empty;

    public string QrUrl { get; set; } = string.Empty;
}
