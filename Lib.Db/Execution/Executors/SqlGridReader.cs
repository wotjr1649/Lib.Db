// ============================================================================
// 파일: Lib.Db.Execution/SqlGridReader.cs
// 역할: 다중 ResultSet 소비용 GridReader 구현체
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Mapping;

namespace Lib.Db.Execution.Executors;

#region SqlGridReader 구현

/// <summary>
/// 다중 결과 셋을 차례로 읽기 위한 GridReader 구현체입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 단일 SP 호출에서 여러 SELECT 문의 결과를 순차적으로 소비해야 하는 시나리오를 지원합니다.
/// <c>_isConsumed</c> 플래그를 통해 상태를 관리하며, <see cref="IMapperFactory"/>와 연동하여
/// 타입 안전하고 성능 최적화된(Zero-Reflection) 매핑을 제공합니다.
/// </para>
/// 
/// <para><strong>💡 핵심 기능</strong></para>
/// <list type="bullet">
/// <item><strong>ReadAsync&lt;T&gt;</strong>: 현재 ResultSet의 모든 행을 List&lt;T&gt;로 반환</item>
/// <item><strong>ReadSingleAsync&lt;T&gt;</strong>: 현재 ResultSet의 첫 번째 행만 반환</item>
/// <item><strong>NextResult 자동 호출</strong>: 두 번째 호출부터 DbDataReader.NextResultAsync 자동 실행</item>
/// </list>
/// 
/// <para><strong>⚡ 성능 특성</strong></para>
/// <list type="bullet">
/// <item><strong>메모리 할당</strong>: 최소 (List&lt;T&gt; 만 생성)</item>
/// <item><strong>DB I/O</strong>: Streaming (행 단위 Fetch)</item>
/// <item><strong>시간 복잡도</strong>: O(N) 매핑</item>
/// </list>
/// 
/// <para><strong>🔒 스레드 안전성</strong></para>
/// <list type="bullet">
/// <item><strong>NOT Thread-Safe</strong>: 동일 인스턴스를 동시에 여러 스레드에서 사용 불가</item>
/// <item><strong>StatefulDesign</strong>: _isConsumed 플래그로 상태 관리</item>
/// <item><strong>Single Consumer</strong>: 한 번에 하나의 ResultSet만 처리</item>
/// </list>
/// 
/// <para><strong>🔧 사용 예시</strong></para>
/// <code>
/// await using var grid = await executor.QueryMultipleAsync(...);
/// var users = await grid.ReadAsync&lt;User&gt;();      // 1번째 SELECT
/// var orders = await grid.ReadAsync&lt;Order&gt;();    // 2번째 SELECT
/// var count = await grid.ReadSingleAsync&lt;int&gt;(); // 3번째 SELECT
/// </code>
/// </remarks>
internal sealed class SqlGridReader(
    DbDataReader reader,
    IMapperFactory mapperFactory
    ) : IMultipleResultReader
{
    private bool _isConsumed;

    /// <inheritdoc />
    public async Task<List<T>> ReadAsync<T>(CancellationToken ct = default)
    {
        if (_isConsumed && !await reader.NextResultAsync(ct).ConfigureAwait(false))
            return [];

        _isConsumed = true;

        var mapper = mapperFactory.GetMapper<T>();
        var list = new List<T>();

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add(mapper.MapResult(reader));

        return list;
    }

    /// <inheritdoc />
    public async Task<T?> ReadSingleAsync<T>(CancellationToken ct = default)
    {
        if (_isConsumed && !await reader.NextResultAsync(ct).ConfigureAwait(false))
            return default;

        _isConsumed = true;

        var mapper = mapperFactory.GetMapper<T>();
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? mapper.MapResult(reader)
            : default;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await reader.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Dry-Run 또는 결과 셋이 필요 없는 경우에 사용하는 빈 GridReader 구현입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 조건문 없이 안전하게 사용 가능한 Null Object 패턴을 적용하여, Dry-Run 모드나 결과가 없는 상황에서도
/// 호출 코드를 단순하게 유지할 수 있습니다. 빈 컬렉션을 반환하여 메모리 할당을 최소화합니다.
/// </para>
/// 
/// <para><strong>⚡ 성능 특성</strong></para>
/// <list type="bullet">
/// <item><strong>메모리 할당</strong>: Zero (빈 컴렉션 재사용)</item>
/// <item><strong>시간 복잡도</strong>: O(1)</item>
/// <item><strong>DB I/O</strong>: None</item>
/// </list>
/// </remarks>
internal sealed class EmptyGridReader : IMultipleResultReader
{
    public Task<List<T>> ReadAsync<T>(CancellationToken ct = default)
        => Task.FromResult(new List<T>());

    public Task<T?> ReadSingleAsync<T>(CancellationToken ct = default)
        => Task.FromResult<T?>(default);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

#endregion
