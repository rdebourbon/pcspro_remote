namespace PcsRemote.Core;

/// <summary>
/// Holds structured team name information for both competing teams as read from
/// the loaded match in PCS Pro.
/// </summary>
/// <param name="Home">The home team's club and team name information.</param>
/// <param name="Away">The away team's club and team name information.</param>
public record MatchTeams(TeamNameInfo Home, TeamNameInfo Away);
