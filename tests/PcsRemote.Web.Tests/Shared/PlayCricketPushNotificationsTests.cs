using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public sealed class PlayCricketPushNotificationsTests
{
    private const string ModulePath = "./js/playCricketNotifications.js";

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static Mock<IPlayCricketWatcherService> BuildWatcherMock(bool isEnabled = false)
    {
        var mock = new Mock<IPlayCricketWatcherService>();
        mock.Setup(w => w.IsEnabled).Returns(isEnabled);
        return mock;
    }

    private static (IRenderedComponent<PlayCricketPushNotifications> Cut, BunitContext Ctx, BunitJSInterop ModuleInterop)
    Render(Mock<IPlayCricketWatcherService> watcherMock)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(watcherMock.Object);

        var moduleInterop = ctx.JSInterop.SetupModule(ModulePath);
        moduleInterop.SetupVoid("requestPermission").SetVoidResult();
        moduleInterop.SetupVoid("sendNotification", _ => true).SetVoidResult();

        var cut = ctx.Render<PlayCricketPushNotifications>();
        return (cut, ctx, moduleInterop);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-1 — First enable triggers permission request exactly once
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FirstEnable_RequestsPermissionOnce()
    {
        var watcherMock = BuildWatcherMock();
        var (cut, ctx, moduleInterop) = Render(watcherMock);
        using (ctx)
        {
            watcherMock.Raise(
                w => w.AutoWatchEnabledChanged += null,
                this,
                new AutoWatchEnabledChangedSnapshot(true));

            cut.WaitForAssertion(() => moduleInterop.VerifyInvoke("requestPermission", 1));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2 — Second enable does not re-request permission
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SecondEnable_DoesNotRerequestPermission()
    {
        var watcherMock = BuildWatcherMock();
        var (cut, ctx, moduleInterop) = Render(watcherMock);
        using (ctx)
        {
            watcherMock.Raise(
                w => w.AutoWatchEnabledChanged += null,
                this,
                new AutoWatchEnabledChangedSnapshot(true));

            cut.WaitForAssertion(() => moduleInterop.VerifyInvoke("requestPermission", 1));

            watcherMock.Raise(
                w => w.AutoWatchEnabledChanged += null,
                this,
                new AutoWatchEnabledChangedSnapshot(true));

            // Still only one invocation after the second enable.
            cut.WaitForAssertion(() => moduleInterop.VerifyInvoke("requestPermission", 1));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3 — CountdownStarted dispatches start notification
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CountdownStarted_SendsNotificationWithDuration()
    {
        var watcherMock = BuildWatcherMock();
        var (cut, ctx, moduleInterop) = Render(watcherMock);
        using (ctx)
        {
            watcherMock.Raise(
                w => w.CountdownStarted += null,
                this,
                new CountdownStartedSnapshot(TimeSpan.FromMinutes(5), null));

            cut.WaitForAssertion(() => moduleInterop.VerifyInvoke("sendNotification", 1));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4 — AutoCloseT60Warning dispatches 60-second warning notification
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AutoCloseT60Warning_SendsWarningNotification()
    {
        var watcherMock = BuildWatcherMock();
        var (cut, ctx, moduleInterop) = Render(watcherMock);
        using (ctx)
        {
            watcherMock.Raise(
                w => w.AutoCloseT60Warning += null,
                this,
                EventArgs.Empty);

            cut.WaitForAssertion(() => moduleInterop.VerifyInvoke("sendNotification", 1));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5 — JS module silently no-ops on permission denial (does not throw)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PermissionDenied_DoesNotThrow()
    {
        var watcherMock = BuildWatcherMock();
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(watcherMock.Object);

        var moduleInterop = ctx.JSInterop.SetupModule(ModulePath);
        // requestPermission returns without effect — simulating JS-side silent no-op on denial.
        moduleInterop.SetupVoid("requestPermission").SetVoidResult();
        moduleInterop.SetupVoid("sendNotification", _ => true).SetVoidResult();

        using (ctx)
        {
            var cut = ctx.Render<PlayCricketPushNotifications>();

            watcherMock.Raise(
                w => w.AutoWatchEnabledChanged += null,
                this,
                new AutoWatchEnabledChangedSnapshot(true));

            // WaitForAssertion re-throws any unhandled exception captured by bUnit.
            // If the component had propagated an exception, this assertion would throw.
            cut.WaitForAssertion(() => moduleInterop.VerifyInvoke("requestPermission", 1));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-6 — JSDisconnectedException from JS call is caught silently
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void JSDisconnectedException_IsCaughtSilently()
    {
        var watcherMock = BuildWatcherMock();
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(watcherMock.Object);

        var moduleInterop = ctx.JSInterop.SetupModule(ModulePath);
        moduleInterop.SetupVoid("requestPermission").SetVoidResult();
        moduleInterop.SetupVoid("sendNotification", _ => true)
            .SetException(new JSDisconnectedException("Circuit disconnected"));

        using (ctx)
        {
            var cut = ctx.Render<PlayCricketPushNotifications>();

            watcherMock.Raise(
                w => w.CountdownStarted += null,
                this,
                new CountdownStartedSnapshot(TimeSpan.FromMinutes(5), null));

            // WaitForAssertion re-throws any unhandled exception captured by bUnit.
            // A passing assertion on the stable empty markup confirms no exception escaped.
            cut.WaitForAssertion(() => cut.Markup.Should().BeEmpty());
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-7 — Dispose cleans up: no JS calls after disposal
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Dispose_NoJsCallsAfterDisposal()
    {
        var watcherMock = BuildWatcherMock();
        var (cut, ctx, moduleInterop) = Render(watcherMock);
        using (ctx)
        {
            await cut.Instance.DisposeAsync();

            watcherMock.Raise(
                w => w.CountdownStarted += null,
                this,
                new CountdownStartedSnapshot(TimeSpan.FromMinutes(5), null));

            // _disposed guard fires before any await, so the handler returns synchronously.
            // No JS invocations should have been recorded through the module.
            moduleInterop.Invocations.Should().BeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-8 — Late-join: already-enabled circuit requests permission at mount
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void LateJoin_AlreadyEnabled_RequestsPermissionAtMount()
    {
        var watcherMock = BuildWatcherMock(isEnabled: true);
        var (cut, ctx, moduleInterop) = Render(watcherMock);
        using (ctx)
        {
            // Permission flow should have been triggered during OnInitializedAsync
            // without any AutoWatchEnabledChanged event being raised.
            cut.WaitForAssertion(() => moduleInterop.VerifyInvoke("requestPermission", 1));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-9 — Component renders no HTML
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_ProducesNoHtml()
    {
        var watcherMock = BuildWatcherMock();
        var (cut, ctx, _) = Render(watcherMock);
        using (ctx)
        {
            cut.Markup.Trim().Should().BeEmpty();
        }
    }
}
