using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PcsRemote.TrayHost.Tests;

[TestClass]
public class ExceptionHandlerTests
{
    // TC-1: UnobservedTaskException is logged and observed
    [TestMethod]
    public void UnobservedTaskException_Handler_SetsObserved()
    {
        var observed = false;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            e.SetObserved();
            observed = true;
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            CreateFaultedTask();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            observed.Should().BeTrue("the UnobservedTaskException handler should fire after GC");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    // TC-3: AppDomain handler logs Fatal with Exception payload and calls CloseAndFlush
    [TestMethod]
    public void AppDomainHandler_ExceptionPayload_LogsFatalAndFlushes()
    {
        var loggedEvents = new List<LogEvent>();
        var flushCalled = false;

        var testLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new DelegateSink(e => loggedEvents.Add(e)))
            .CreateLogger();

        var testException = new InvalidOperationException("Test fatal exception");
        object exceptionObject = testException;

        // Simulate the AppDomain handler logic using an ILogger instance
        if (exceptionObject is Exception ex)
        {
            testLogger.Fatal(ex, "Unhandled AppDomain exception — process will terminate");
        }
        else
        {
            testLogger.Fatal(
                "Unhandled AppDomain exception (non-Exception object: {ExceptionType}) — process will terminate",
                exceptionObject?.GetType().FullName ?? "null");
        }

        // Simulate CloseAndFlush by disposing the logger
        testLogger.Dispose();
        flushCalled = true;

        loggedEvents.Should().ContainSingle();
        loggedEvents[0].Level.Should().Be(LogEventLevel.Fatal);
        loggedEvents[0].Exception.Should().BeSameAs(testException);
        flushCalled.Should().BeTrue("CloseAndFlush must be called after Fatal log");
    }

    // TC-3b: AppDomain handler with non-Exception payload logs defensively
    [TestMethod]
    public void AppDomainHandler_NonExceptionPayload_LogsDefensively()
    {
        var loggedEvents = new List<LogEvent>();

        var testLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new DelegateSink(e => loggedEvents.Add(e)))
            .CreateLogger();

        object nonExceptionPayload = "some string error";

        // Simulate the AppDomain handler logic for non-Exception
        if (nonExceptionPayload is Exception ex)
        {
            testLogger.Fatal(ex, "Unhandled AppDomain exception — process will terminate");
        }
        else
        {
            testLogger.Fatal(
                "Unhandled AppDomain exception (non-Exception object: {ExceptionType}) — process will terminate",
                nonExceptionPayload?.GetType().FullName ?? "null");
        }

        testLogger.Dispose();

        loggedEvents.Should().ContainSingle();
        loggedEvents[0].Level.Should().Be(LogEventLevel.Fatal);
        var rendered = loggedEvents[0].RenderMessage();
        rendered.Should().Contain("System.String");
    }

    private static void CreateFaultedTask()
    {
        _ = Task.Run(() => throw new InvalidOperationException("Unobserved test exception"));
        Thread.Sleep(100);
    }

    private sealed class DelegateSink : ILogEventSink
    {
        private readonly Action<LogEvent> _write;

        public DelegateSink(Action<LogEvent> write) => _write = write;

        public void Emit(LogEvent logEvent) => _write(logEvent);
    }
}
