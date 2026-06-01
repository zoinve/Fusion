namespace YPM.Core.Models;

public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:3000";

    public bool EnableRealIp { get; set; } = true;

    public string RealIp { get; set; } = "211.161.244.70";

    public string? Proxy { get; set; }
}
