namespace PcsRemote.Core;

/// <summary>
/// Identifies a match fixture for use throughout the automation and web layers.
/// Pre-load display fields (HomeTeam, AwayTeam, MatchType, MatchDate) are populated
/// by GetTodaysMatchesAsync; their defaults are sentinels — not valid fixture data.
/// </summary>
/// <param name="MatchId">The PlayCricket fixture identifier.</param>
/// <param name="HomeTeam">Display name of the home team. Default "Home XI" (sentinel).</param>
/// <param name="AwayTeam">Display name of the away team. Default "Away XI" (sentinel).</param>
/// <param name="MatchType">Short match-type label, e.g. "Club T20". Default "Friendly" (sentinel).</param>
/// <param name="MatchDate">Fixture date. Default DateOnly.MinValue (sentinel — "not set").</param>
public record MatchInfo(
    string MatchId,
    string HomeTeam = "Home XI",
    string AwayTeam = "Away XI",
    string MatchType = "Friendly",
    DateOnly MatchDate = default);
