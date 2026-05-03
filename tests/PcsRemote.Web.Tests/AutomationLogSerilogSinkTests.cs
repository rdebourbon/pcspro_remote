using FluentAssertions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;
using Serilog.Events;
using Serilog.Parsing;

namespace PcsRemote.Web.Tests;

[TestClass]
public sealed class AutomationLogSerilogSinkTests
{
    private Mock<IAutomationLogService> _logServiceMock = null!;
    private AutomationLogSerilogSink _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _logServiceMock = new Mock<IAutomationLogService>();
        _sut = new AutomationLogSerilogSink(_logServiceMock.Object);
    }

    private static LogEvent CreateLogEvent(
        LogEventLevel level,
        string messageTemplate = "Test message",
        Exception? exception = null)
    {
        var parser = new MessageTemplateParser();
        var template = parser.Parse(messageTemplate);
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            exception,
            template,
            Array.Empty<LogEventProperty>());
    }

    private static LogEvent CreateStructuredLogEvent(
        LogEventLevel level,
        string messageTemplate,
        params LogEventProperty[] properties)
    {
        var parser = new MessageTemplateParser();
        var template = parser.Parse(messageTemplate);
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            null,
            template,
            properties);
    }

    // TC-1: Emit Warning → AddEntry called with Warning
    [TestMethod]
    public void Emit_WarningLevel_CallsAddEntryWithWarning()
    {
        var logEvent = CreateLogEvent(LogEventLevel.Warning, "Something suspicious");

        _sut.Emit(logEvent);

        _logServiceMock.Verify(
            s => s.AddEntry(
                It.Is<string>(msg => msg.Contains("Something suspicious")),
                AutomationLogOutcome.Warning),
            Times.Once);
    }

    // TC-2: Emit Error → AddEntry called with Failure
    [TestMethod]
    public void Emit_ErrorLevel_CallsAddEntryWithFailure()
    {
        var logEvent = CreateLogEvent(LogEventLevel.Error, "Operation failed");

        _sut.Emit(logEvent);

        _logServiceMock.Verify(
            s => s.AddEntry(
                It.Is<string>(msg => msg.Contains("Operation failed")),
                AutomationLogOutcome.Failure),
            Times.Once);
    }

    // TC-3: Emit Fatal → AddEntry called with Failure
    [TestMethod]
    public void Emit_FatalLevel_CallsAddEntryWithFailure()
    {
        var logEvent = CreateLogEvent(LogEventLevel.Fatal, "Critical crash");

        _sut.Emit(logEvent);

        _logServiceMock.Verify(
            s => s.AddEntry(
                It.Is<string>(msg => msg.Contains("Critical crash")),
                AutomationLogOutcome.Failure),
            Times.Once);
    }

    // TC-4: Emit Information → AddEntry NOT called
    [TestMethod]
    public void Emit_InformationLevel_DoesNotCallAddEntry()
    {
        var logEvent = CreateLogEvent(LogEventLevel.Information, "Just info");

        _sut.Emit(logEvent);

        _logServiceMock.Verify(
            s => s.AddEntry(It.IsAny<string>(), It.IsAny<AutomationLogOutcome>()),
            Times.Never);
    }

    // TC-4 extension: Emit Debug → AddEntry NOT called
    [TestMethod]
    public void Emit_DebugLevel_DoesNotCallAddEntry()
    {
        var logEvent = CreateLogEvent(LogEventLevel.Debug, "Debug detail");

        _sut.Emit(logEvent);

        _logServiceMock.Verify(
            s => s.AddEntry(It.IsAny<string>(), It.IsAny<AutomationLogOutcome>()),
            Times.Never);
    }

    // TC-5: AddEntry throws → sink does not propagate
    [TestMethod]
    public void Emit_AddEntryThrows_DoesNotPropagate()
    {
        _logServiceMock
            .Setup(s => s.AddEntry(It.IsAny<string>(), It.IsAny<AutomationLogOutcome>()))
            .Throws(new InvalidOperationException("Boom"));

        var logEvent = CreateLogEvent(LogEventLevel.Warning, "Test");

        var act = () => _sut.Emit(logEvent);

        act.Should().NotThrow();
    }

    // TC-6: Structured message template renders correctly
    [TestMethod]
    public void Emit_StructuredMessage_RendersPropertyValues()
    {
        var logEvent = CreateStructuredLogEvent(
            LogEventLevel.Warning,
            "User {UserId} logged in from {IpAddress}",
            new LogEventProperty("UserId", new ScalarValue("user-42")),
            new LogEventProperty("IpAddress", new ScalarValue("192.168.1.1")));

        _sut.Emit(logEvent);

        _logServiceMock.Verify(
            s => s.AddEntry(
                It.Is<string>(msg =>
                    msg.Contains("user-42") && msg.Contains("192.168.1.1")),
                AutomationLogOutcome.Warning),
            Times.Once);
    }

    // TC-8: Error event with exception → message includes exception suffix
    [TestMethod]
    public void Emit_ErrorWithException_IncludesExceptionDetail()
    {
        var exception = new ArgumentNullException("paramName", "Value cannot be null");
        var logEvent = CreateLogEvent(LogEventLevel.Error, "Connection failed", exception);

        _sut.Emit(logEvent);

        _logServiceMock.Verify(
            s => s.AddEntry(
                It.Is<string>(msg =>
                    msg.Contains("Connection failed") &&
                    msg.Contains("ArgumentNullException") &&
                    msg.Contains("Value cannot be null")),
                AutomationLogOutcome.Failure),
            Times.Once);
    }
}
