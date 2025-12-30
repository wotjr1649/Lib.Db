using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lib.Db.Caching;
using Lib.Db.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lib.Db.TestSuite.Integration;

public class CacheLeaderElectionTests : IDisposable
{
    private readonly string _tempBasePath;

    public CacheLeaderElectionTests()
    {
        _tempBasePath = Path.Combine(Path.GetTempPath(), "LibDbTest_Leader_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempBasePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempBasePath))
                Directory.Delete(_tempBasePath, true);
        }
        catch { }
    }

    [Fact]
    public void TryAcquireLeadership_ShouldReturnTrue_WhenNoContention()
    {
        // Arrange
        var options = CreateOptions("TestIsoKey");
        using var election = new CacheLeaderElection(options, NullLogger<CacheLeaderElection>.Instance);

        // Act
        bool result = election.TryAcquireLeadership();

        // Assert
        Assert.True(result);
        Assert.True(election.IsLeader);
    }

    [Fact]
    public void Heartbeat_ShouldCreateLeaseFile_WhenLeader()
    {
        // Arrange
        var options = CreateOptions("TestIsoKey_Heartbeat");
        using var election = new CacheLeaderElection(options, NullLogger<CacheLeaderElection>.Instance);

        // Act
        election.TryAcquireLeadership();

        // Assert
        string leasePath = Path.Combine(_tempBasePath, "leader.lease");
        Assert.True(File.Exists(leasePath));

        string content = File.ReadAllText(leasePath);
        Assert.Contains("PID=", content);
        Assert.Contains("Time=", content);
    }

    [Fact]
    public void TryAcquireLeadership_ShouldReturnFalse_WhenLocked()
    {
        // Arrange
        var options = CreateOptions("TestIsoKey_Contention");
        
        // Leader 1
        using var leader1 = new CacheLeaderElection(options, NullLogger<CacheLeaderElection>.Instance);
        bool first = leader1.TryAcquireLeadership();
        Assert.True(first);

        // Leader 2 (Same Isolation Key -> Same Mutex Name)
        using var leader2 = new CacheLeaderElection(options, NullLogger<CacheLeaderElection>.Instance);
        
        // Act
        // Mutex is thread-affine. Use explicit Thread to guarantee OS thread difference.
        bool second = false;
        var t = new Thread(() => 
        {
            second = leader2.TryAcquireLeadership();
        });
        t.Start();
        t.Join();

        // Assert
        Assert.False(second);
        Assert.False(leader2.IsLeader);
        Assert.False(leader2.IsLeader);
    }

    private IOptions<SharedMemoryCacheOptions> CreateOptions(string key)
    {
        return Options.Create(new SharedMemoryCacheOptions
        {
            BasePath = _tempBasePath,
            IsolationKey = key
        });
    }
}
