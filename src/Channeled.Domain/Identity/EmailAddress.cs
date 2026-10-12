using System.Text.RegularExpressions;

namespace Channeled.Domain.Identity;

public sealed partial class EmailAddress : IEquatable<EmailAddress>
{
    private static readonly Regex Pattern = EmailRegex();

    public string Value { get; }

    private EmailAddress(string normalized)
    {
        Value = normalized;
    }

    public static EmailAddress Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Email is required.", nameof(input));
        }

        var trimmed = input.Trim();
        if (!Pattern.IsMatch(trimmed) || trimmed.Length > 320)
        {
            throw new ArgumentException("Email format is invalid.", nameof(input));
        }

        return new EmailAddress(trimmed.ToLowerInvariant());
    }

    public bool Equals(EmailAddress? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is EmailAddress other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
