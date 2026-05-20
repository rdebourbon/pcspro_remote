using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.PlayCricket;

/// <summary>
/// Real HTTP implementation of <see cref="IPlayCricketApiClient"/> using the Play-Cricket API v2.
/// </summary>
public sealed class PlayCricketApiClient : IPlayCricketApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PlayCricketOptions _options;
    private readonly ILogger<PlayCricketApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    public PlayCricketApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<PlayCricketOptions> options,
        ILogger<PlayCricketApiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlayCricketFixture>> GetFixturesAsync(
        int siteId,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("PlayCricket");
        var season = DateTime.Now.Year;
        var requestUri = $"matches.json?site_id={siteId}&season={season}&api_token={_options.ApiKey}";

        try
        {
            var response = await client.GetAsync(requestUri, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Play-Cricket API returned {StatusCode} for site {SiteId}",
                    (int)response.StatusCode,
                    siteId);
                return Array.Empty<PlayCricketFixture>();
            }

            var json = await response.Content.ReadAsStringAsync(ct);

            PlayCricketResponse? dto;
            try
            {
                dto = JsonSerializer.Deserialize<PlayCricketResponse>(json, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Play-Cricket API response deserialisation failed for site {SiteId}",
                    siteId);
                return Array.Empty<PlayCricketFixture>();
            }

            if (dto?.SiteMatches is null)
                return Array.Empty<PlayCricketFixture>();

            return MapFixtures(dto.SiteMatches, siteId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Play-Cricket API network error for site {SiteId}",
                siteId);
            return Array.Empty<PlayCricketFixture>();
        }
    }

    private IReadOnlyList<PlayCricketFixture> MapFixtures(
        IReadOnlyList<PlayCricketMatchDto> dtos,
        int siteId)
    {
        var fixtures = new List<PlayCricketFixture>(dtos.Count);

        foreach (var dto in dtos)
        {
            if (!int.TryParse(dto.Id, out var fixtureId))
            {
                _logger.LogWarning(
                    "Play-Cricket fixture has non-integer id {RawId} for site {SiteId}",
                    dto.Id,
                    siteId);
                continue;
            }

            DateOnly matchDate;
            if (!DateOnly.TryParseExact(
                    dto.MatchDate,
                    "dd/MM/yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out matchDate))
            {
                _logger.LogWarning(
                    "Play-Cricket fixture {FixtureId} has unparseable date {RawDate}",
                    fixtureId,
                    dto.MatchDate);
                matchDate = DateOnly.MinValue;
            }

            fixtures.Add(new PlayCricketFixture(
                fixtureId,
                dto.HomeTeamName ?? "",
                dto.AwayTeamName ?? "",
                dto.MatchStatus ?? "",
                matchDate));
        }

        return fixtures.AsReadOnly();
    }

    private sealed record PlayCricketResponse(
        [property: JsonPropertyName("site_matches")] IReadOnlyList<PlayCricketMatchDto>? SiteMatches);

    private sealed record PlayCricketMatchDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("home_team_name")] string? HomeTeamName,
        [property: JsonPropertyName("away_team_name")] string? AwayTeamName,
        [property: JsonPropertyName("match_status")] string? MatchStatus,
        [property: JsonPropertyName("match_date")] string? MatchDate);
}
