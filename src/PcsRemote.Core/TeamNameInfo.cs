namespace PcsRemote.Core;

/// <summary>
/// Holds the club name and team name as read from a single team's ComboBoxes
/// in the PCS Pro Match Details dialog.
/// </summary>
/// <param name="ClubName">
/// The club name (e.g., "Northampton Town CC"). Empty string when the club
/// ComboBox is blank or unreadable.
/// </param>
/// <param name="TeamName">The team name (e.g., "2nd XI").</param>
public record TeamNameInfo(string ClubName, string TeamName);
