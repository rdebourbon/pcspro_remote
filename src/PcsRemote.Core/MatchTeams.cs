namespace PcsRemote.Core;

/// <summary>
/// Holds the names of the two competing teams as read from the loaded match in PCS Pro.
/// </summary>
/// <param name="HomeTeam">The name of the home team.</param>
/// <param name="AwayTeam">The name of the away team.</param>
public record MatchTeams(string HomeTeam, string AwayTeam);
