// ============================================================================
// 파일: Infrastructure/MockMemoryPressureMonitor.cs
// 설명: 테스트용 Mock 메모리 압력 모니터
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Contracts.Execution;

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// 메모리 압력을 시뮬레이션하는 Mock 모니터.
/// </summary>
public sealed class MockMemoryPressureMonitor : IMemoryPressureMonitor
{
    private bool _isCritical;

    public bool IsCritical
    {
        get => _isCritical;
        set => _isCritical = value;
    }

    public double LoadFactor => _isCritical ? 0.9 : 0.1;
}
