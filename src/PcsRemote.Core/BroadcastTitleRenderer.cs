using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PcsRemote.Core;

/// <summary>
/// Renders a YouTube broadcast title from a template string and match data.
/// Implements the token grammar defined in HLPS-008 §4.5.
/// </summary>
public sealed class BroadcastTitleRenderer
{
    private static readonly Regex TokenPattern = new(
        @"\{(\w+)(?::([^}]+))?\}",
        RegexOptions.Compiled);

    private const string DefaultTemplate = "{HomeTeam} vs {AwayTeam}";
    private const int MaxTitleLength = 100;

    private readonly ILogger<BroadcastTitleRenderer> _logger;

    public BroadcastTitleRenderer(ILogger<BroadcastTitleRenderer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Substitutes tokens in <paramref name="template"/> using values from <paramref name="match"/>.
    /// </summary>
    /// <param name="template">
    /// Title template with <c>{Token}</c> placeholders.
    /// Null, empty, or whitespace falls back to the default template.
    /// </param>
    /// <param name="match">Match data for token substitution.</param>
    /// <returns>The rendered title, truncated to 100 characters if necessary.</returns>
    public string Render(string? template, MatchInfo match)
    {
        var effectiveTemplate = string.IsNullOrWhiteSpace(template)
            ? DefaultTemplate
            : template;

        var result = TokenPattern.Replace(effectiveTemplate, m => ResolveToken(m, match));

        if (result.Length > MaxTitleLength)
        {
            result = string.Concat(result.AsSpan(0, MaxTitleLength - 3), "...");
        }

        return result;
    }

    private string ResolveToken(Match regexMatch, MatchInfo match)
    {
        var tokenName = regexMatch.Groups[1].Value;
        var format = regexMatch.Groups[2].Success ? regexMatch.Groups[2].Value : null;

        return tokenName switch
        {
            "HomeTeam" => match.HomeTeam ?? "",
            "AwayTeam" => match.AwayTeam ?? "",
            "MatchType" => match.MatchType ?? "",
            "Date" => FormatDate(match.MatchDate, format),
            _ => regexMatch.Value
        };
    }

    private string FormatDate(DateOnly date, string? format)
    {
        if (format is null)
        {
            return date.ToString("d");
        }

        try
        {
            return date.ToString(format);
        }
        catch (FormatException)
        {
            _logger.LogWarning(
                "Invalid date format {Format} in title template; falling back to short date",
                format);
            return date.ToString("d");
        }
    }
}
