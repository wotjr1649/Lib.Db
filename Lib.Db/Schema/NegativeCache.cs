// ============================================================================
// 파일: Lib.Db/Schema/NegativeCache.cs
// 설명: Negative Cache - 존재하지 않는 SP/TVP 목록 캐싱
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Lib.Db.Schema;

/// <summary>
/// 데이터베이스에 존재하지 않는 객체(SP, TVP 등)의 조회 결과를 캐싱하여 불필요한 재조회를 방지하는 '부정 캐시(Negative Cache)'입니다.
/// <para>
/// <b>[설계 목적]</b><br/>
/// ORM이나 데이터 매퍼가 존재하지 않는 스키마를 반복적으로 조회하려고 할 때 발생하는 성능 저하를 막습니다.<br/>
/// 한 번 '없음'으로 확인된 객체는 메모리에 기록되어, 다음 요청부터는 DB Round-trip 없이 즉시 예외를 발생시킵니다.
/// </para>
/// <para>
/// <b>[주요 특징]</b>
/// <list type="bullet">
/// <item><b>Fail-Fast:</b> 존재하지 않는 객체 접근 시 즉각적인 오류 반환</item>
/// <item><b>Flyweight 패턴:</b> 예외 객체(Exception)를 미리 생성해두고 재사용하여 GC 부하 최소화</item>
/// <item><b>Thread-Safe:</b> 동시성 컬렉션 사용으로 멀티스레드 환경 안전 보장</item>
/// </list>
/// </para>
/// </summary>
internal static class NegativeCache
{
    // [스레드 안전] 존재하지 않는 객체 목록 저장소
    // Key: "DB해시:타입:객체명" 복합 키
    // Value: 재사용 가능한 사전 생성된 예외 객체
    private static readonly ConcurrentDictionary<string, InvalidOperationException> s_missingObjects = new();
    
    // [설정 가능] 캐시가 보유할 수 있는 최대 항목 수 (기본값: 1000)
    // 메모리 누수를 방지하기 위한 안전장치입니다.
    private static int s_maxSize = 1000;
    
    /// <summary>
    /// Negative Cache가 저장할 수 있는 최대 항목 수를 설정합니다.
    /// <para>
    /// [호출 시점] 애플리케이션 시작 시(LibDbOptions 초기화 등)에 한 번 설정하는 것이 좋습니다.
    /// </para>
    /// </summary>
    /// <param name="maxSize">최대 캐시 크기 (0보다 커야 함)</param>
    public static void Configure(int maxSize) => s_maxSize = maxSize > 0 ? maxSize : 1000;
    
    /// <summary>
    /// 저장된 모든 Negative Cache 항목을 제거합니다.
    /// <para>
    /// [사용 시나리오]
    /// <list type="bullet">
    /// <item>단위 테스트 간의 상태 격리가 필요할 때</item>
    /// <item>런타임에 스키마가 변경되어(객체 생성 등) 캐시를 무효화해야 할 때</item>
    /// </list>
    /// </para>
    /// </summary>
    public static void Clear() => s_missingObjects.Clear();
    
    /// <summary>
    /// 특정 객체가 DB에 존재하지 않음을 캐시에 기록합니다.
    /// <para>
    /// <b>[메모리 관리 정책]</b><br/>
    /// 캐시 크기가 <see cref="s_maxSize"/>를 초과하면, LRU와 같은 복잡한 알고리즘 대신
    /// <b>전체 캐시를 비우는(Clear)</b> 단순하고 빠른 전략을 사용합니다.<br/>
    /// 이는 Negative Cache의 특성상(일시적인 오류 상황) 빈번한 오버플로우가 발생하지 않는다는 가정에 기반합니다.
    /// </para>
    /// </summary>
    /// <param name="dbHash">대상 데이터베이스 인스턴스의 해시값</param>
    /// <param name="objectName">객체 이름 (예: "dbo.usp_GetUser")</param>
    /// <param name="objectType">객체 타입 (예: "StoredProcedure", "TvpType")</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordMissing(string dbHash, string objectName, string objectType)
    {
        // [메모리 보호] 최대 크기 도달 시 초기화 (복잡한 퇴출 정책 대신 단순함 선택)
        if (s_missingObjects.Count >= s_maxSize)
        {
            s_missingObjects.Clear();
        }
        
        string key = BuildKey(dbHash, objectName, objectType);
        
        // [최적화] 예외 객체를 미리 생성하여 값으로 저장 (Flyweight)
        // 나중에 조회 시 새로 생성하지 않고 이 인스턴스를 즉시 던집니다.
        var ex = new InvalidOperationException(
            $"[Negative Cache] {objectType} '{objectName}'이(가) DB '{dbHash}'에 존재하지 않습니다. (이전에 확인됨)");
        
        s_missingObjects[key] = ex;
    }
    
    /// <summary>
    /// 지정된 객체가 '존재하지 않음'으로 캐시되어 있는지 확인하고, 그렇다면 즉시 예외를 던집니다.
    /// <para>
    /// <b>[성능 보장]</b><br/>
    /// 이 메서드는 메모리 조회만 수행하므로 매우 빠릅니다(수 마이크로초).<br/>
    /// 불필요한 DB 연결이나 쿼리 실행을 원천 차단하여 시스템 전체의 응답성을 보호합니다.
    /// </para>
    /// </summary>
    /// <param name="dbHash">대상 데이터베이스 인스턴스 해시</param>
    /// <param name="objectName">객체 이름</param>
    /// <param name="objectType">객체 타입</param>
    /// <returns>
    /// 캐시되지 않음(존재 가능성 있음): <c>false</c> 반환<br/>
    /// 캐시됨(존재하지 않음): <b>반환되지 않음</b> (즉시 예외 throw)
    /// </returns>
    /// <exception cref="InvalidOperationException">해당 객체가 존재하지 않는 것으로 캐시되어 있을 때 발생</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ThrowIfCached(string dbHash, string objectName, string objectType)
    {
        string key = BuildKey(dbHash, objectName, objectType);
        
        if (s_missingObjects.TryGetValue(key, out var cachedException))
        {
            // [Fail-Fast] 캐시된 예외를 즉시 던짐
            // 스택 트레이스는 이 지점에서 생성된 것이 아니라 RecordMissing 시점의 문맥을 가질 수 있으나,
            // '존재하지 않음'이라는 사실 자체는 변하지 않으므로 문제없음.
            throw cachedException;
        }
        
        return false; // 캐시에 없으므로 DB 조회 시도 허용
    }
    
    /// <summary>
    /// [내부 헬퍼] 검색을 위한 복합 키 문자열을 생성합니다.
    /// <para>형식: <c>{dbHash}:{objectType}:{objectName}</c></para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string BuildKey(string dbHash, string objectName, string objectType)
        => $"{dbHash}:{objectType}:{objectName}";
}
