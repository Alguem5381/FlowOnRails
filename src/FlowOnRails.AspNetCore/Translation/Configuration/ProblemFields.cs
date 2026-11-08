using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace FlowOnRails.AspNetCore.Translation;

internal sealed record ProblemFields(int Status, string? Type, string? Title)
{
    internal ProblemDetails Create() => new()
    {
        Status = Status,
        Type = Type ?? "about:blank",
        Title = Title ?? ReasonPhrases.GetReasonPhrase(Status)
    };

    internal static void ValidateStatus(int status)
    {
        if (status is < 400 or > 599)
            throw new ArgumentOutOfRangeException(nameof(status), status,
                "An error problem requires a status from 400 through 599.");
    }

    internal static void ValidateUri(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Uri.IsWellFormedUriString(value, UriKind.RelativeOrAbsolute))
            throw new ArgumentException("A URI reference is required.", nameof(value));
    }
}
