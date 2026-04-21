using FluentAssertions;

namespace PcsRemote.Core.Tests;

[TestClass]
public class TeamNameFormatterTests
{
    // TC-1: One team matches, already first — stripped + first; other unchanged
    [TestMethod]
    public void FormatForTitle_Team1MatchesClub_StrippedAndFirst()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "HHCC - 1st XI", "Club B - 2nd XI", "HHCC");

        first.Should().Be("1st XI");
        second.Should().Be("Club B - 2nd XI");
    }

    // TC-2: One team matches, second position — reordered to first + stripped
    [TestMethod]
    public void FormatForTitle_Team2MatchesClub_ReorderedAndStripped()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "Club B - 2nd XI", "HHCC - 1st XI", "HHCC");

        first.Should().Be("1st XI");
        second.Should().Be("Club B - 2nd XI");
    }

    // TC-3: Neither team matches — both unchanged, original order
    [TestMethod]
    public void FormatForTitle_NeitherMatches_UnchangedOriginalOrder()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "Club C - 1st XI", "Club D - 1st XI", "HHCC");

        first.Should().Be("Club C - 1st XI");
        second.Should().Be("Club D - 1st XI");
    }

    // TC-4: Both teams match — both stripped, original input order preserved
    [TestMethod]
    public void FormatForTitle_BothMatch_BothStrippedOriginalOrder()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "HHCC - 1st XI", "HHCC - 2nd XI", "HHCC");

        first.Should().Be("1st XI");
        second.Should().Be("2nd XI");
    }

    // TC-5: Club name null — both unchanged, original order
    [TestMethod]
    public void FormatForTitle_ClubNameNull_UnchangedOriginalOrder()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "HHCC - 1st XI", "Club B - 2nd XI", null);

        first.Should().Be("HHCC - 1st XI");
        second.Should().Be("Club B - 2nd XI");
    }

    // TC-6: Club name empty — both unchanged, original order
    [TestMethod]
    public void FormatForTitle_ClubNameEmpty_UnchangedOriginalOrder()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "HHCC - 1st XI", "Club B - 2nd XI", "");

        first.Should().Be("HHCC - 1st XI");
        second.Should().Be("Club B - 2nd XI");
    }

    // TC-7: Team name starts with club name + separator " - " — prefix + separator stripped
    [TestMethod]
    public void FormatForTitle_ClubWithDashSeparator_PrefixAndSeparatorStripped()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "High Halstow CC - 1st XI", "Otford CC - 1st XI", "High Halstow CC");

        first.Should().Be("1st XI");
        second.Should().Be("Otford CC - 1st XI");
    }

    // TC-8: Team name equals club name exactly — returns unchanged (empty fallback)
    [TestMethod]
    public void FormatForTitle_TeamNameEqualsClubExactly_ReturnsUnchanged()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "HHCC", "Club B - 2nd XI", "HHCC");

        first.Should().Be("HHCC");
        second.Should().Be("Club B - 2nd XI");
    }

    // TC-9: Prefix match is case-insensitive
    [TestMethod]
    public void FormatForTitle_CaseInsensitiveMatch_StrippingApplied()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "hhcc - 1st XI", "Club B - 2nd XI", "HHCC");

        first.Should().Be("1st XI");
        second.Should().Be("Club B - 2nd XI");
    }

    // TC-10: Partial word match — club "High" must not match "Highlands CC"
    [TestMethod]
    public void FormatForTitle_PartialWordMatch_NotStripped()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "Highlands CC - 1st XI", "Club B - 2nd XI", "High");

        first.Should().Be("Highlands CC - 1st XI");
        second.Should().Be("Club B - 2nd XI");
    }

    // TC-11: Already-stripped name re-processed — idempotent
    [TestMethod]
    public void FormatForTitle_AlreadyStripped_Idempotent()
    {
        // First pass
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "HHCC - 1st XI", "Club B - 2nd XI", "HHCC");

        // Second pass with already-stripped result
        var (first2, second2) = TeamNameFormatter.FormatForTitle(
            first, second, "HHCC");

        first2.Should().Be("1st XI");
        second2.Should().Be("Club B - 2nd XI");
    }

    // TC-12: Team1 is null, Team2 matches — Team2 stripped; null treated as non-matching
    [TestMethod]
    public void FormatForTitle_Team1Null_Team2Matches_Team2StrippedFirst()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            null, "HHCC - 1st XI", "HHCC");

        first.Should().Be("1st XI");
        second.Should().BeEmpty();
    }

    // TC-13: Team1 is empty, Team2 matches — Team2 stripped; empty treated as non-matching
    [TestMethod]
    public void FormatForTitle_Team1Empty_Team2Matches_Team2StrippedFirst()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "", "HHCC - 1st XI", "HHCC");

        first.Should().Be("1st XI");
        second.Should().BeEmpty();
    }

    // TC-14: Team1 matches, Team2 is null — Team1 stripped; null treated as non-matching
    [TestMethod]
    public void FormatForTitle_Team1Matches_Team2Null_Team1StrippedFirst()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "HHCC - 1st XI", null, "HHCC");

        first.Should().Be("1st XI");
        second.Should().BeEmpty();
    }

    // TC-15: Team1 matches, Team2 is empty — Team1 stripped; empty treated as non-matching
    [TestMethod]
    public void FormatForTitle_Team1Matches_Team2Empty_Team1StrippedFirst()
    {
        var (first, second) = TeamNameFormatter.FormatForTitle(
            "HHCC - 1st XI", "", "HHCC");

        first.Should().Be("1st XI");
        second.Should().BeEmpty();
    }
}
