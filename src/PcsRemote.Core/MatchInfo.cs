namespace PcsRemote.Core;

/// <summary>
/// Identifies a specific match fixture for use by the automation service's match selection operation.
/// </summary>
/// <param name="MatchId">The PlayCricket fixture identifier used to locate and select the match.</param>
public record MatchInfo(string MatchId);
