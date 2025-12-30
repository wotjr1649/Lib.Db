using Lib.Db.Contracts.Execution;

namespace Lib.Db.Verification.Tests.Infrastructure;

public class MockMemoryPressureMonitor : IMemoryPressureMonitor
{
    private bool _isCritical;

    public bool IsCritical
    {
        get => _isCritical;
        set => _isCritical = value;
    }

    // IsCritical이 true면 0.9(90%), false면 0.1(10%) 부하로 시뮬레이션
    public double LoadFactor => _isCritical ? 0.9 : 0.1;
}
