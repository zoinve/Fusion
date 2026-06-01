namespace YPM.Core.Models;

public sealed class LoginResult
{
    public int Code { get; set; }

    public string? Cookie { get; set; }

    public string? Token { get; set; }

    public string? Message { get; set; }

    public UserProfile? Profile { get; set; }

    public bool IsSuccess => Code == 200;
}

public sealed class LoginStatusResult
{
    public int Code { get; set; }

    public UserProfile? Profile { get; set; }

    public UserAccountResult? Account { get; set; }

    public bool IsLoggedIn => Code == 200;
}

public sealed class CellphoneExistenceResult
{
    public int Code { get; set; }

    public bool Exists { get; set; }

    public string? Message { get; set; }
}
