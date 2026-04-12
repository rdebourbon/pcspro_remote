using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public class PcsProCircuitHandlerTests
{
    private Mock<IConnectionTracker> _trackerMock = null!;

    [TestInitialize]
    public void SetUp()
    {
        _trackerMock = new Mock<IConnectionTracker>();
    }

    [TestMethod]
    public async Task OnCircuitOpenedAsync_IncrementsConnectionCountExactlyOnce()
    {
        var handler = new PcsProCircuitHandler(_trackerMock.Object);

        // Circuit is sealed with an internal constructor and cannot be instantiated
        // from an external assembly. The handler does not access the circuit parameter,
        // so null! is safe here.
        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);

        _trackerMock.Verify(t => t.Increment(), Times.Once);
    }

    [TestMethod]
    public async Task OnCircuitOpenedAsync_DoesNotDecrement()
    {
        var handler = new PcsProCircuitHandler(_trackerMock.Object);

        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);

        _trackerMock.Verify(t => t.Decrement(), Times.Never);
    }

    [TestMethod]
    public async Task OnCircuitClosedAsync_DecrementsConnectionCountExactlyOnce()
    {
        var handler = new PcsProCircuitHandler(_trackerMock.Object);

        await handler.OnCircuitClosedAsync(null!, CancellationToken.None);

        _trackerMock.Verify(t => t.Decrement(), Times.Once);
    }

    [TestMethod]
    public async Task OnCircuitClosedAsync_DoesNotIncrement()
    {
        var handler = new PcsProCircuitHandler(_trackerMock.Object);

        await handler.OnCircuitClosedAsync(null!, CancellationToken.None);

        _trackerMock.Verify(t => t.Increment(), Times.Never);
    }
}
