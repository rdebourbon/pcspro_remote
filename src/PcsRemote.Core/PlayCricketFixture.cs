namespace PcsRemote.Core;

/// <summary>
/// Represents a single Play-Cricket fixture as returned by the API.
/// </summary>
/// <param name="FixtureId">Play-Cricket fixture identifier. Not related to <see cref="MatchInfo.MatchId"/>.</param>
/// <param name="HomeTeam">Home team display name. Defaults to empty string.</param>
/// <param name="AwayTeam">Away team display name. Defaults to empty string.</param>
/// <param name="Status">
/// Fixture status string as returned by the API. Exact values are resolved in
/// SPEC-IS-021-S-007 (OQ-1). Defaults to empty string.
/// </param>
/// <param name="MatchDate">
/// Fixture date. Defaults to <see cref="DateOnly.MinValue"/> (sentinel — "not set"),
/// consistent with <see cref="MatchInfo.MatchDate"/>.
/// </param>
public record PlayCricketFixture(
    int FixtureId,
    string HomeTeam = "",
    string AwayTeam = "",
    string Status = "",
    DateOnly MatchDate = default);
