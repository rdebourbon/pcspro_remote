namespace PcsRemote.Core;

/// <summary>
/// Pure formatting function that strips a configured club name prefix from team
/// display names and reorders them so the club team appears first in the title.
/// </summary>
public static class TeamNameFormatter
{
    /// <summary>
    /// Formats two team display names for use in a broadcast title.
    /// Strips the configured club name prefix from whichever team(s) match,
    /// and reorders so the club-matching team appears first.
    /// </summary>
    /// <param name="team1">First team name (dialog order).</param>
    /// <param name="team2">Second team name (dialog order).</param>
    /// <param name="clubName">Configured club name prefix to strip. Null/empty disables formatting.</param>
    /// <returns>An ordered tuple (First, Second) representing title-ready team names.</returns>
    public static (string First, string Second) FormatForTitle(
        string? team1, string? team2, string? clubName)
    {
        if (string.IsNullOrEmpty(clubName))
        {
            return (team1 ?? "", team2 ?? "");
        }

        bool team1Matches = MatchesClubPrefix(team1, clubName);
        bool team2Matches = MatchesClubPrefix(team2, clubName);

        if (team1Matches && team2Matches)
        {
            // Both match — strip both, preserve original input order.
            return (StripPrefix(team1!, clubName), StripPrefix(team2!, clubName));
        }

        if (team1Matches)
        {
            // Team1 matches — strip it, keep it first.
            return (StripPrefix(team1!, clubName), team2 ?? "");
        }

        if (team2Matches)
        {
            // Team2 matches — strip it, reorder to first.
            return (StripPrefix(team2!, clubName), team1 ?? "");
        }

        // Neither matches — unchanged, original order.
        return (team1 ?? "", team2 ?? "");
    }

    /// <summary>
    /// Reorders two club names to match <see cref="FormatForTitle"/>'s swap logic.
    /// If only the away (second) club matches the configured club name, clubs are swapped;
    /// otherwise original order is preserved.
    /// </summary>
    public static (string First, string Second) OrderClubNamesForTitle(
        string? homeClub, string? awayClub, string? clubName)
    {
        if (string.IsNullOrEmpty(clubName))
        {
            return (homeClub ?? "", awayClub ?? "");
        }

        bool homeMatches = MatchesClubPrefix(homeClub, clubName);
        bool awayMatches = MatchesClubPrefix(awayClub, clubName);

        if (!homeMatches && awayMatches)
        {
            return (awayClub ?? "", homeClub ?? "");
        }

        return (homeClub ?? "", awayClub ?? "");
    }

    internal static bool MatchesClubPrefix(string? teamName, string clubName)
    {
        if (string.IsNullOrEmpty(teamName))
        {
            return false;
        }

        if (!teamName.StartsWith(clubName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Ensure the prefix is a complete match — not a partial word.
        // Either the team name equals the club name exactly, or the character
        // immediately after the prefix is a non-alphanumeric separator.
        if (teamName.Length == clubName.Length)
        {
            return true;
        }

        char nextChar = teamName[clubName.Length];
        return !char.IsLetterOrDigit(nextChar);
    }

    private static string StripPrefix(string teamName, string clubName)
    {
        string remainder = teamName[clubName.Length..];

        // Greedily trim leading whitespace and dash/hyphen characters.
        string trimmed = remainder.TrimStart(' ', '-', '\t');

        // If stripping + trimming produces empty, return original unchanged.
        return string.IsNullOrEmpty(trimmed) ? teamName : trimmed;
    }
}
