using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PcsRemote.YouTube;

namespace PcsRemote.YouTube.Tests;

[TestClass]
public class DpapiFileDataStoreTests
{
    private string _testDir = null!; // Assigned in TestInitialize
    private Mock<ILogger<DpapiFileDataStore>> _loggerMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"DpapiTests_{Guid.NewGuid():N}");
        _loggerMock = new Mock<ILogger<DpapiFileDataStore>>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private DpapiFileDataStore CreateStore() => new(_testDir, _loggerMock.Object);

    // AC-1: StoreAsync encrypts with DPAPI and writes to configured path;
    //        round-trip store → get returns identical value
    [TestMethod]
    public async Task StoreAsync_RoundTrip_ReturnsIdenticalValue()
    {
        var store = CreateStore();
        var data = new TestToken { AccessToken = "test-token-123", ExpiresInSeconds = 3600 };

        await store.StoreAsync("test-key", data);
        var result = await store.GetAsync<TestToken>("test-key");

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("test-token-123");
        result.ExpiresInSeconds.Should().Be(3600);
    }

    [TestMethod]
    public async Task StoreAsync_CreatesDirectory_WhenNotExists()
    {
        var store = CreateStore();
        Directory.Exists(_testDir).Should().BeFalse();

        await store.StoreAsync("key", new TestToken { AccessToken = "a" });

        Directory.Exists(_testDir).Should().BeTrue();
    }

    [TestMethod]
    public async Task StoreAsync_WritesEncryptedBytes_NotPlaintext()
    {
        var store = CreateStore();
        var data = new TestToken { AccessToken = "plaintext-sentinel" };

        await store.StoreAsync("key", data);

        var filePath = Path.Combine(_testDir, "key");
        var rawBytes = File.ReadAllBytes(filePath);
        var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);

        // The file should not contain the plaintext token value
        rawText.Should().NotContain("plaintext-sentinel");
    }

    // AC-2: GetAsync returns default with Warning log when decryption fails
    [TestMethod]
    public async Task GetAsync_CorruptFile_ReturnsNull_LogsWarning()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_testDir);
        var filePath = Path.Combine(_testDir, "corrupt-key");
        File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xFE, 0xFD, 0x01, 0x02, 0x03 });

        var result = await store.GetAsync<TestToken>("corrupt-key");

        result.Should().BeNull();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Failed to decrypt")), // null-forgiving: ToString() on structured log state is never null
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetAsync_CorruptFile_DeletesFile()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_testDir);
        var filePath = Path.Combine(_testDir, "corrupt-key2");
        File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xFE });

        await store.GetAsync<TestToken>("corrupt-key2");

        File.Exists(filePath).Should().BeFalse();
    }

    [TestMethod]
    public async Task GetAsync_FileNotFound_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetAsync<TestToken>("nonexistent");

        result.Should().BeNull();
    }

    // AC-3: DeleteAsync removes the file
    [TestMethod]
    public async Task DeleteAsync_RemovesFile()
    {
        var store = CreateStore();
        await store.StoreAsync("to-delete", new TestToken { AccessToken = "del" });
        File.Exists(Path.Combine(_testDir, "to-delete")).Should().BeTrue();

        await store.DeleteAsync<TestToken>("to-delete");

        File.Exists(Path.Combine(_testDir, "to-delete")).Should().BeFalse();
    }

    [TestMethod]
    public async Task DeleteAsync_ThenGet_ReturnsNull()
    {
        var store = CreateStore();
        await store.StoreAsync("key", new TestToken { AccessToken = "x" });

        await store.DeleteAsync<TestToken>("key");
        var result = await store.GetAsync<TestToken>("key");

        result.Should().BeNull();
    }

    // AC-4: ClearAsync removes all files in token directory
    [TestMethod]
    public async Task ClearAsync_RemovesAllFiles()
    {
        var store = CreateStore();
        await store.StoreAsync("key1", new TestToken { AccessToken = "a" });
        await store.StoreAsync("key2", new TestToken { AccessToken = "b" });
        Directory.GetFiles(_testDir).Should().HaveCount(2);

        await store.ClearAsync();

        Directory.GetFiles(_testDir).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ClearAsync_DirectoryNotExists_DoesNotThrow()
    {
        var store = CreateStore();

        var act = () => store.ClearAsync();

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public void FolderPath_ReturnsConfiguredPath()
    {
        var store = CreateStore();
        store.FolderPath.Should().Be(_testDir);
    }

    [TestMethod]
    public async Task StoreAsync_PathTraversalKey_ThrowsArgumentException()
    {
        var store = CreateStore();

        var act = () => store.StoreAsync(@"..\escaped-key", new TestToken { AccessToken = "x" });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*outside the token store folder*");
    }

    [TestMethod]
    public async Task GetAsync_PathTraversalKey_ThrowsArgumentException()
    {
        var store = CreateStore();

        var act = () => store.GetAsync<TestToken>(@"..\escaped-key");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*outside the token store folder*");
    }

    /// <summary>Simple POCO for round-trip testing.</summary>
    private sealed class TestToken
    {
        public string AccessToken { get; set; } = "";
        public int ExpiresInSeconds { get; set; }
    }
}
