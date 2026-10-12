namespace Channeled.Domain.Identity;

public static class PasswordRules
{
    public const int MinLength = 12;

    public static bool IsValid(string password) =>
        !string.IsNullOrEmpty(password) && password.Length >= MinLength;
}
