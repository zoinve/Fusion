namespace YPM.Core.Models;

public sealed class CaptchaSentResult
{
    public int Code { get; set; }

    public bool Data { get; set; }

    public string? Message { get; set; }
}

public sealed class CaptchaVerifyResult
{
    public int Code { get; set; }

    public bool Data { get; set; }

    public string? Message { get; set; }
}
