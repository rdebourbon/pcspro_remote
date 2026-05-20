using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PcsRemote.Core;
using PcsRemote.PlayCricket;

namespace PcsRemote.PlayCricket.Tests;

[TestClass]
public class PlayCricketApiClientTests
{
    private Mock<ILogger<PlayCricketApiClient>> _loggerMock = null!;
    private PlayCricketOptions _options = null!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<PlayCricketApiClient>>();
        _options = new PlayCricketOptions { ApiKey = "test-key" };
    }

    private PlayCricketApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://play-cricket.com/api/v2/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("PlayCricket")).Returns(httpClient);
        return new PlayCricketApiClient(factory.Object, Options.Create(_options), _loggerMock.Object);
    }

    // TC-1: Valid response maps to correct PlayCricketFixture records
    [TestMethod]
    public async Task GetFixturesAsync_ValidResponse_ReturnsMappedFixtures()
    {
        const string json = """
            {
              "site_matches": [
                { "id": "101", "home_team_name": "HHCC 1st XI", "away_team_name": "Visitors CC", "match_status": "Result", "match_date": "15/06/2025" },
                { "id": "102", "home_team_name": "HHCC 2nd XI", "away_team_name": "Away CC",    "match_status": "Playing", "match_date": "22/06/2025" }
              ]
            }
            """;

        var client = CreateClient(new FakeHttpHandler(HttpStatusCode.OK, json));

        var result = await client.GetFixturesAsync(42);

        result.Should().HaveCount(2);
        result[0].FixtureId.Should().Be(101);
        result[0].HomeTeam.Should().Be("HHCC 1st XI");
        result[0].AwayTeam.Should().Be("Visitors CC");
        result[0].Status.Should().Be("Result");
        result[0].MatchDate.Should().Be(new DateOnly(2025, 6, 15));
        result[1].FixtureId.Should().Be(102);
        result[1].MatchDate.Should().Be(new DateOnly(2025, 6, 22));
    }

    // TC-2: Non-success HTTP status returns empty list and logs warning
    [TestMethod]
    public async Task GetFixturesAsync_NonSuccessStatus_ReturnsEmptyListAndLogsWarning()
    {
        var client = CreateClient(new FakeHttpHandler(HttpStatusCode.InternalServerError, ""));

        var result = await client.GetFixturesAsync(42);

        result.Should().BeEmpty();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("500")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // TC-3: Malformed JSON returns empty list and logs warning
    [TestMethod]
    public async Task GetFixturesAsync_MalformedJson_ReturnsEmptyListAndLogsWarning()
    {
        var client = CreateClient(new FakeHttpHandler(HttpStatusCode.OK, "not-valid-json{{{"));

        var result = await client.GetFixturesAsync(42);

        result.Should().BeEmpty();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // TC-4: Network exception returns empty list and logs warning
    [TestMethod]
    public async Task GetFixturesAsync_NetworkException_ReturnsEmptyListAndLogsWarning()
    {
        var client = CreateClient(new ThrowingHttpHandler(new HttpRequestException("connection refused")));

        var result = await client.GetFixturesAsync(42);

        result.Should().BeEmpty();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<HttpRequestException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // TC-5: Cancellation propagates without swallowing
    [TestMethod]
    public async Task GetFixturesAsync_CancellationRequested_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var client = CreateClient(new FakeHttpHandler(HttpStatusCode.OK, "{}"));

        var act = async () => await client.GetFixturesAsync(42, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // TC-6: One record with bad date returns DateOnly.MinValue; warning logged
    [TestMethod]
    public async Task GetFixturesAsync_BadDateOnOneRecord_ReturnsMixedListAndLogsWarning()
    {
        const string json = """
            {
              "site_matches": [
                { "id": "101", "home_team_name": "A", "away_team_name": "B", "match_status": "Result", "match_date": "15/06/2025" },
                { "id": "102", "home_team_name": "C", "away_team_name": "D", "match_status": "Playing", "match_date": "not-a-date" }
              ]
            }
            """;

        var client = CreateClient(new FakeHttpHandler(HttpStatusCode.OK, json));

        var result = await client.GetFixturesAsync(42);

        result.Should().HaveCount(2);
        result[0].MatchDate.Should().Be(new DateOnly(2025, 6, 15));
        result[1].MatchDate.Should().Be(DateOnly.MinValue);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("not-a-date")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeHttpHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHttpHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw _exception;
    }
}
