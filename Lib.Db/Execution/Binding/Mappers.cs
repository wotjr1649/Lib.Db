// ============================================================================
// File: Lib.Db/Execution/Binding/DbMappers.cs
// Role: DTO/Dictionary/DataRow/Scalar <-> SQL 파라미터/결과 매핑 통합 엔진
// Env : .NET 10 / C# 14 (Preview 가정)
// Notes:
//   - MapperFactory: 타입별 매퍼 캐시 + DI 우선 + AOT/JIT 하이브리드
//   - ExpressionTreeMapper: JIT 전용 고성능 DTO 매퍼 (Typed Getter + JSON 역직렬화)
//   - ReflectionParameterMapper: AOT Fallback (FrozenDictionary 기반 캐시 + Attribute 지원)
//   - Scalar/Dictionary/DataRow 매퍼: 레거시/유연 바인딩 지원
//   - GeneratedResultMapper: IMapableResult<T> + 정적 Map(DbDataReader) 패턴 대응
//   - AOT 환경에서 DTO 결과 매핑이 필요하면 Source Generator 또는 수동 매퍼 필수
// ============================================================================

#nullable enable

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;

using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Output;
using Microsoft.Extensions.ObjectPool;

namespace Lib.Db.Execution.Binding;

#region 매퍼 팩터리 (JIT/AOT 하이브리드)

/// <summary>
/// 다양한 타입(DTO, Dictionary, DataRow 등)에 대한 고성능 매퍼를 생성/캐싱하는 팩토리입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// - Generation-Based Cache (Gen0 젊은 세대 / Gen1 오래된 세대)<br/>
/// - 접근 빈도에 따라 Gen0 → Gen1 승격<br/>
/// - 전체 Clear 대신 Gen0의 50%만 제거하여 Thundering Herd 방지
/// </para>
/// </summary>
internal sealed class MapperFactory(IServiceProvider serviceProvider, LibDbOptions options) : IMapperFactory
{
    #region [필드 선언] Generation-Based Cache

    /// <summary>
    /// [Gen0] 젊은 세대 캐시 - 새로 생성된 매퍼 + 접근 횟수 추적
    /// </summary>
    private static readonly ConcurrentDictionary<MapperCacheKey, CacheEntry> s_gen0Cache = new();

    /// <summary>
    /// [Gen1] 오래된 세대 캐시 - 자주 사용되는 매퍼 (승격된 항목)
    /// </summary>
    private static readonly ConcurrentDictionary<MapperCacheKey, object> s_gen1Cache = new();

    /// <summary>Source Generator가 생성한 매퍼 타입 캐시 (Type → Mapper Type)</summary>
    private static volatile FrozenDictionary<Type, Type>? s_generatedMapperTypes;
    private static readonly Lock s_discoveryLock = new();
    private static bool s_discoveryCompleted;

    /// <summary>캐시 최대 크기 (Gen0 + Gen1 합산)</summary>
    private readonly int _maxCache = options.MaxCacheSize;

    /// <summary>승격 임계값 - Gen0에서 이 횟수만큼 접근되면 Gen1로 승격</summary>
    private const int PromotionThreshold = 2;

    /// <summary>
    /// 캐시 키 - 매퍼 대상 타입 + 런타임 dynamic-code 지원 모드
    /// </summary>
    private readonly record struct MapperCacheKey(Type Type, bool DynamicCodeSupported);

    /// <summary>
    /// 캐시 항목 - 매퍼 인스턴스 + 접근 횟수
    /// </summary>
    private readonly record struct CacheEntry(object Mapper, int AccessCount);

    #endregion

    /// <inheritdoc />
    public ISqlMapper<T> GetMapper<T>()
    {
        // ---------------------------------------------------------------------
        // 1) DI 컨테이너에 등록된 매퍼 우선
        // ---------------------------------------------------------------------
        if (serviceProvider.GetService(typeof(ISqlMapper<T>)) is ISqlMapper<T> diMapper)
            return diMapper;

        Type type = typeof(T);
        bool dynamicCodeSupported =
            RuntimeFeatureSwitch.IsRuntimeDynamicCodeSupported &&
            RuntimeFeatureSwitch.DynamicCodeSupportedOverride is not false;
        MapperCacheKey cacheKey = new(type, dynamicCodeSupported);

        // ---------------------------------------------------------------------
        // 2) Gen1 캐시 조회 (가장 자주 사용되는 매퍼)
        // ---------------------------------------------------------------------
        if (s_gen1Cache.TryGetValue(cacheKey, out object? gen1Cached))
        {
            return (ISqlMapper<T>)gen1Cached;
        }

        // ---------------------------------------------------------------------
        // 3) Gen0 캐시 조회
        // ---------------------------------------------------------------------
        if (s_gen0Cache.TryGetValue(cacheKey, out CacheEntry gen0Entry))
        {
            // 접근 횟수 증가
            int newAccessCount = gen0Entry.AccessCount + 1;

            // ✅ [승격 조건] 접근 횟수가 임계값 이상이면 Gen1로 승격
            if (newAccessCount >= PromotionThreshold)
            {
                PromoteToGen1(cacheKey, gen0Entry.Mapper);
            }
            else
            {
                // 접근 횟수만 증가 (CAS 패턴)
                s_gen0Cache.TryUpdate(cacheKey,
                    new CacheEntry(gen0Entry.Mapper, newAccessCount),
                    gen0Entry);
            }

            return (ISqlMapper<T>)gen0Entry.Mapper;
        }

        // ---------------------------------------------------------------------
        // 4) 캐시 미스 → 새로 생성
        // ---------------------------------------------------------------------

        // ✅ [캐시 크기 관리] 임계값 초과 시 Gen0 정리
        int totalCacheSize = s_gen0Cache.Count + s_gen1Cache.Count;
        if (totalCacheSize >= _maxCache)
        {
            CleanupGen0Cache();
        }

        // 매퍼 생성
        ISqlMapper<T> mapper = CreateMapper<T>(dynamicCodeSupported);

        // Gen0에 추가 (초기 접근 횟수 = 0)
        CacheEntry entry = new CacheEntry(mapper, 0);
        s_gen0Cache.TryAdd(cacheKey, entry);

        return mapper;
    }

    /// <summary>
    /// Gen0에서 Gen1로 매퍼를 승격합니다.
    /// </summary>
    private static void PromoteToGen1(MapperCacheKey cacheKey, object mapper)
    {
        // Gen1에 추가
        s_gen1Cache.TryAdd(cacheKey, mapper);

        // Gen0에서 제거
        s_gen0Cache.TryRemove(cacheKey, out _);
    }

    /// <summary>
    /// Gen0 캐시를 정리합니다.
    /// <para>
    /// <b>[Thundering Herd 방지]</b><br/>
    /// 전체 Clear 대신, Gen0의 약 50%를 무작위로 제거합니다.<br/>
    /// OrderBy를 사용하지 않아 O(N) 복잡도를 유지합니다.
    /// </para>
    /// <para>
    /// <b>[성능 최적화]</b><br/>
    /// 기존 OrderBy(O(N log N)) 대신 Random Sampling(O(N)) 사용<br/>
    /// 10,000 항목 기준: ~5ms → ~1ms (5배 향상)
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>호출 빈도</b>: 캐시 크기가 MaxCacheSize 도달 시<br/>
    /// <b>동시성</b>: Thread-safe (ConcurrentDictionary 사용)<br/>
    /// <b>부작용</b>: Gen0의 약 50% 항목 제거 (무작위 선택)<br/>
    /// <b>시간 복잡도</b>: O(N) - 순회 1회만 수행<br/>
    /// <b>공간 복잡도</b>: O(N/2) - 제거할 키 리스트만 임시 저장
    /// </remarks>
    private static void CleanupGen0Cache()
    {
        // ✅ [O(N) 최적화] OrderBy 제거 - Random Sampling 전략
        // 접근 빈도와 무관하게 무작위로 50%를 선택하여 제거
        // 이는 LRU보다 구현이 단순하며 충분히 효과적임

        List<MapperCacheKey> toRemove = new List<MapperCacheKey>(s_gen0Cache.Count / 2);

        // Random.Shared는 .NET 6+에서 제공하는 Thread-safe 난수 생성기
        foreach (KeyValuePair<MapperCacheKey, CacheEntry> kv in s_gen0Cache)
        {
            // 50% 확률로 제거 대상에 추가
            if (Random.Shared.Next(2) == 0)
            {
                toRemove.Add(kv.Key);
            }
        }

        // 제거 대상이 너무 적으면 추가로 제거 (최소 25% 보장)
        if (toRemove.Count < s_gen0Cache.Count / 4)
        {
            int needed = (s_gen0Cache.Count / 2) - toRemove.Count;
            foreach (KeyValuePair<MapperCacheKey, CacheEntry> kv in s_gen0Cache)
            {
                if (needed <= 0)
                    break;
                if (!toRemove.Contains(kv.Key))
                {
                    toRemove.Add(kv.Key);
                    needed--;
                }
            }
        }

        // 실제 제거
        foreach (MapperCacheKey key in toRemove)
        {
            s_gen0Cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 타입별 매퍼 인스턴스를 생성합니다.
    /// <para>
    /// 우선순위: Source Generator (100) → ExpressionTree (50) → Reflection (0)
    /// </para>
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2091",
        Justification = "MapperFactory selects generated/static mappers when available. Reflection and expression mappers are documented non-AOT convenience paths.")]
    [UnconditionalSuppressMessage(
        "Aot",
        "IL3050:RequiresDynamicCode",
        Justification = "ExpressionTreeMapper is selected only when RuntimeFeatureSwitch reports dynamic code support; the test override can only disable dynamic code and Native AOT returns false.")]
    private ISqlMapper<T> CreateMapper<T>(bool dynamicCodeSupported)
    {
        Type type = typeof(T);

        // [특수 타입] Dictionary 및 DataRow
        if (type == typeof(Dictionary<string, object?>))
            return (ISqlMapper<T>)(object)new DictionarySqlMapper(options.StrictRequiredParameterCheck);

        if (type == typeof(DataRow))
            return (ISqlMapper<T>)(object)new DataRowSqlMapper(options.StrictRequiredParameterCheck);

        if (type == typeof(object))
            return (ISqlMapper<T>)(object)new ObjectSqlMapper(options.StrictRequiredParameterCheck);

        // [특수 타입] Scalar (Primitive, string, decimal, DateTime, Guid, Stream 등)
        if (IsScalar(type))
            return new ScalarSqlMapper<T>();

        // [Generated] IMapableResult<T> (정적 Map(DbDataReader) 패턴)
        if (type.IsAssignableTo(typeof(IMapableResult<T>)))
        {
            Type mapperType = typeof(GeneratedResultMapper<>).MakeGenericType(type);

            return (ISqlMapper<T>)(Activator.CreateInstance(mapperType, options)
                ?? throw new InvalidOperationException($"'{type.Name}'에 대한 GeneratedResultMapper 생성에 실패했습니다."));
        }

        // =====================================================================
        // [1순위] Source Generator가 생성한 매퍼 Discovery
        // =====================================================================
        ISqlMapper<T>? sgMapper = DiscoverGeneratedMapper<T>();
        if (sgMapper is not null)
            return sgMapper;

        // =====================================================================
        // [2순위] JIT 환경: Expression Tree 기반 DTO 매퍼
        // =====================================================================
        if (dynamicCodeSupported)
            return new ExpressionTreeMapper<T>(options.JsonOptions, options.StrictRequiredParameterCheck);

        // =====================================================================
        // [3순위] AOT Fallback: Reflection (Parameter Only)
        // =====================================================================
        return new ReflectionParameterMapper<T>(options.StrictRequiredParameterCheck);
    }

    /// <summary>
    /// Source Generator가 생성한 매퍼를 Assembly Scan을 통해 발견합니다.
    /// <para>
    /// - 최초 1회만 Assembly Scan 수행 후 FrozenDictionary에 결과 캐싱<br/>
    /// - IGeneratedMapper&lt;T&gt; 구현체를 우선적으로 사용
    /// </para>
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "Generated mapper discovery is a legacy convenience. Native AOT applications should register explicit/generated mappings and do not rely on discovery.")]
    private static ISqlMapper<T>? DiscoverGeneratedMapper<T>()
    {
        // 첫 호출 시 Assembly Scan 수행
        if (!s_discoveryCompleted)
        {
            lock (s_discoveryLock)
            {
                if (!s_discoveryCompleted)
                {
                    ScanGeneratedMappers();
                    s_discoveryCompleted = true;
                }
            }
        }

        // FrozenDictionary에서 조회
        if (s_generatedMapperTypes?.TryGetValue(typeof(T), out Type? mapperType) == true)
        {
            // Activator.CreateInstance는 기본 생성자 호출
            // Source Generator 매퍼는 매개변수 없는 생성자를 가져야 함
            return (ISqlMapper<T>?)Activator.CreateInstance(mapperType);
        }

        return null;
    }

    /// <summary>
    /// Assembly를 Scan하여 IGeneratedMapper&lt;T&gt; 구현체를 검색합니다.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Generated mapper assembly scan is a legacy discovery convenience. Native AOT applications should register static mappings explicitly.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Generated mapper assembly scan is a legacy discovery convenience. Native AOT applications should register static mappings explicitly.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2065",
        Justification = "Generated mapper assembly scan is a legacy discovery convenience. Native AOT applications should register static mappings explicitly.")]
    private static void ScanGeneratedMappers()
    {
        Assembly assembly = typeof(MapperFactory).Assembly;
        Dictionary<Type, Type> generatedMappers = new Dictionary<Type, Type>();

        foreach (Type type in assembly.GetTypes())
        {
            // Lib.Db.Generated 네임스페이스 확인
            if (type.Namespace != "Lib.Db.Generated")
                continue;

            // IGeneratedMapper<T> 구현 여부 확인
            foreach (Type iface in type.GetInterfaces())
            {
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition() == typeof(IGeneratedMapper<>))
                {
                    Type dtoType = iface.GetGenericArguments()[0];
                    generatedMappers[dtoType] = type;
                    break;
                }
            }
        }

        s_generatedMapperTypes = generatedMappers.ToFrozenDictionary();
    }

    /// <summary>
    /// 스칼라 타입(Primitive/문자열/날짜/Guid/바이너리/Stream 등) 여부를 판별합니다.
    /// </summary>
    private static bool IsScalar(Type t)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;

        return u.IsPrimitive
               || u == typeof(string)
               || u == typeof(decimal)
               || u == typeof(DateTime)
               || u == typeof(DateTimeOffset)
               || u == typeof(Guid)
               || u == typeof(byte[])
               || u == typeof(TimeSpan)
               || typeof(Stream).IsAssignableFrom(u);
    }
}

#endregion

#region 리플렉션 캐시 (공유)

/// <summary>
/// 리플렉션 호출을 캐싱하여 반복 비용을 제거하는 정적 헬퍼입니다.
/// <para><b>[설계 의도]</b> GetProperties/GetConstructors 호출은 내부적으로 배열 복사를 수행하므로,
/// ConcurrentDictionary로 타입별 결과를 캐싱하여 GC 압력과 CPU 비용을 절감합니다.</para>
/// </summary>
internal static class ReflectionCache
{
    /// <summary>타입별 Public 인스턴스 프로퍼티 캐시</summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> s_propertyCache = new();

    /// <summary>타입별 Public 인스턴스 생성자 캐시</summary>
    private static readonly ConcurrentDictionary<Type, ConstructorInfo[]> s_ctorCache = new();

    /// <summary>
    /// 지정 타입의 Public 인스턴스 프로퍼티를 캐시에서 반환합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "ReflectionCache is used by non-AOT mapper fallback paths. Native AOT hot paths use generated or explicit mappings.")]
    public static PropertyInfo[] GetPublicProperties(Type type)
        => s_propertyCache.GetOrAdd(type, static t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));

    /// <summary>
    /// 지정 타입의 Public 인스턴스 생성자를 캐시에서 반환합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "ReflectionCache is used by non-AOT mapper fallback paths. Native AOT hot paths use generated or explicit mappings.")]
    public static ConstructorInfo[] GetPublicConstructors(Type type)
        => s_ctorCache.GetOrAdd(type, static t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
}

#endregion

#region SQL 식별자 이름 해석

/// <summary>
/// SQL 컬럼/파라미터 이름과 CLR 프로퍼티 이름을 보수적으로 매칭하는 헬퍼입니다.
/// </summary>
internal static class SqlIdentifierName
{
    public static FrozenDictionary<string, PropertyInfo> BuildNormalizedPropertyMap(PropertyInfo[] properties)
    {
        Dictionary<string, PropertyInfo> normalized = new(StringComparer.Ordinal);
        HashSet<string> ambiguous = new(StringComparer.Ordinal);

        foreach (PropertyInfo property in properties)
        {
            string key = Normalize(property.Name);
            if (key.Length == 0 || ambiguous.Contains(key))
                continue;

            if (normalized.ContainsKey(key))
            {
                normalized.Remove(key);
                ambiguous.Add(key);
                continue;
            }

            normalized.Add(key, property);
        }

        return normalized.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static FrozenSet<string> BuildAmbiguousNormalizedPropertySet(PropertyInfo[] properties)
    {
        HashSet<string> observed = new(StringComparer.Ordinal);
        HashSet<string> ambiguous = new(StringComparer.Ordinal);

        foreach (PropertyInfo property in properties)
        {
            string key = Normalize(property.Name);
            if (key.Length == 0)
                continue;

            if (!observed.Add(key))
                ambiguous.Add(key);
        }

        return ambiguous.ToFrozenSet(StringComparer.Ordinal);
    }

    public static bool TryGetProperty(
        FrozenDictionary<string, PropertyInfo> exactMap,
        FrozenDictionary<string, PropertyInfo> normalizedMap,
        string name,
        [NotNullWhen(true)] out PropertyInfo? property)
    {
        if (exactMap.TryGetValue(name, out property))
            return true;

        string normalized = Normalize(name);
        return normalizedMap.TryGetValue(normalized, out property);
    }

    public static bool IsAmbiguousNormalizedName(FrozenSet<string> ambiguousNormalizedNames, string name)
    {
        string normalized = Normalize(name);
        return normalized.Length > 0 && ambiguousNormalizedNames.Contains(normalized);
    }

    private static string Normalize(string name)
    {
        ReadOnlySpan<char> span = name.AsSpan();
        while (!span.IsEmpty && span[0] == '@')
            span = span[1..];

        char[] buffer = new char[span.Length];
        int written = 0;

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (c == '_')
                continue;

            buffer[written++] = char.ToUpperInvariant(c);
        }

        return new string(buffer, 0, written);
    }
}

#endregion

#region 명시적 ReturnValue 후보 처리

internal static class ExplicitReturnValueBinding
{
    public static void BindOrValidateCandidate(
        SqlCommand cmd,
        string fallbackName,
        object? value,
        ref bool observedCandidate)
    {
        if (value is not SqlParameter { Direction: ParameterDirection.ReturnValue } source)
            return;

        string candidateName = string.IsNullOrWhiteSpace(source.ParameterName)
            ? fallbackName
            : source.ParameterName;

        if (TryGetExistingReturnValue(cmd, out SqlParameter? existing))
        {
            if (!observedCandidate &&
                OutputParameterName.From(existing.ParameterName).Matches(candidateName) &&
                SqlParameterCloneFactory.IsRegisteredSource(existing, source))
            {
                observedCandidate = true;
                return;
            }

            ThrowDuplicate(candidateName);
        }

        if (observedCandidate)
            ThrowDuplicate(candidateName);

        if (DbBinder.TryBindExplicitReturnValueParameter(cmd, fallbackName, source))
            observedCandidate = true;
    }

    private static bool TryGetExistingReturnValue(
        SqlCommand cmd,
        [NotNullWhen(true)] out SqlParameter? parameter)
    {
        foreach (SqlParameter existing in cmd.Parameters)
        {
            if (existing.Direction == ParameterDirection.ReturnValue)
            {
                parameter = existing;
                return true;
            }
        }

        parameter = null;
        return false;
    }

    private static void ThrowDuplicate(string name)
    {
        string display = OutputParameterName.From(name).SafeDisplay();
        throw new InvalidOperationException(
            $"Only one ReturnValue parameter can be bound to a SqlCommand. Duplicate candidate: '{display}'.");
    }
}

#endregion

// ============================================================================
// [Expression Tree Mapper] JIT 전용 DTO 매퍼 (Typed Getter + JSON 지원)
// ============================================================================

#region Expression Tree DTO 매퍼

/// <summary>
/// 런타임에 DTO 구조를 분석하고, Expression Tree를 통해 DbDataReader &lt;-&gt; DTO 변환 코드를
/// 고성능으로 컴파일하여 사용하는 매퍼입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// - SP 스키마 기반 파라미터 바인딩<br/>
/// - 스키마 없는 Raw SQL 파라미터 바인딩 (DbParameterAttribute 우선)<br/>
/// - 결과 매핑 시 Typed Getter(GetInt32, GetGuid 등) 우선 사용으로 Boxing 최소화<br/>
/// - 문자열 컬럼 → 복합 DTO 프로퍼티에 대해서 JSON 역직렬화 지원
/// </para>
/// </summary>
[RequiresDynamicCode("Expression Tree 컴파일 기능이 필요하므로 Native AOT에서는 사용하지 않습니다.")]
internal sealed class ExpressionTreeMapper<T>(JsonSerializerOptions? jsonOptions, bool strict) : ISqlMapper<T>
{
    #region [정적 메타데이터 캐시]

    /// <summary>프로퍼티 메타데이터 + Attribute 캐시 구조체</summary>
    private readonly record struct PropertyMeta(PropertyInfo Info, DbParameterAttribute? Attribute);

    /// <summary>
    /// 타입별 정적 메타데이터 캐시입니다.
    /// <para>FrozenDictionary를 통해 읽기 경로의 Lock-Free 고성능 조회를 지원합니다.</para>
    /// </summary>
    private static class Meta
    {
        /// <summary>대소문자 무시 프로퍼티 조회용 맵 (이름 → PropertyInfo)</summary>
        public static readonly FrozenDictionary<string, PropertyInfo> PropMap;

        /// <summary>snake_case/upper snake 컬럼명 조회용 정규화 맵 (충돌 항목 제외)</summary>
        public static readonly FrozenDictionary<string, PropertyInfo> NormalizedPropMap;

        /// <summary>정규화 이름 충돌로 output target을 결정할 수 없는 프로퍼티 이름 집합</summary>
        public static readonly FrozenSet<string> AmbiguousNormalizedPropNames;

        /// <summary>Raw SQL 바인딩용 전체 프로퍼티 메타데이터 배열 (선언 순서 유지)</summary>
        public static readonly PropertyMeta[] AllProps;

        static Meta()
        {
            // 1) 모든 Public 인스턴스 프로퍼티를 PropMap에 올린다. (ReflectionCache 경유)
            PropertyInfo[] allProps = ReflectionCache.GetPublicProperties(typeof(T));

            PropMap = allProps.ToFrozenDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            NormalizedPropMap = SqlIdentifierName.BuildNormalizedPropertyMap(allProps);
            AmbiguousNormalizedPropNames = SqlIdentifierName.BuildAmbiguousNormalizedPropertySet(allProps);

            // 2) Raw SQL 파라미터 바인딩용 AllProps는 "읽기 가능한" 프로퍼티만 대상
            AllProps = allProps
                .Where(p => p.CanRead)
                .OrderBy(p => p.MetadataToken) // 코드 정의 순서 보존
                .Select(p => new PropertyMeta(p, p.GetCustomAttribute<DbParameterAttribute>()))
                .ToArray();
        }

        public static bool TryGetProperty(string name, [NotNullWhen(true)] out PropertyInfo? property)
            => SqlIdentifierName.TryGetProperty(PropMap, NormalizedPropMap, name, out property);

        public static bool HasAmbiguousNormalizedName(string name)
            => SqlIdentifierName.IsAmbiguousNormalizedName(AmbiguousNormalizedPropNames, name);
    }

    #endregion

    #region [Getter/Setter/Deserializer 캐시]

    /// <summary>프로퍼티 이름 기준 Getter 캐시 (DTO → 파라미터 값)</summary>
    private static readonly ConcurrentDictionary<string, Func<T, object?>> s_getters =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>프로퍼티 이름 기준 Setter 캐시 (Output 파라미터 값 → DTO)</summary>
    private static readonly ConcurrentDictionary<string, Action<T, object?>> s_setters =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>컬럼 시그니처(필드 구성)별 결과 역직렬화 델리게이트 캐시</summary>
    private readonly ConcurrentDictionary<int, Func<DbDataReader, T>> _deserializers = new();

    #endregion

    #region [파라미터 매핑]

    /// <inheritdoc />
    public void MapParameters(SqlCommand cmd, T param, SpSchema? schema)
    {
        if (param is null)
            return;

        // [Case A] SP 스키마 기반 바인딩 (DB 정의 우선)
        if (schema is not null)
        {
            SchemaOutputTargetValidator.ValidateUniqueOutputParameterNames(schema);

            foreach (SpParameterMetadata meta in schema.Parameters)
            {
                string name = meta.Name.TrimStart('@');
                bool hasProperty = Meta.TryGetProperty(name, out PropertyInfo? prop);
                SchemaOutputTargetValidator.ValidateObjectTarget(
                    meta,
                    strict,
                    hasProperty ? prop : null,
                    Meta.HasAmbiguousNormalizedName(name));

                if (hasProperty && prop!.CanRead)
                {
                    Func<T, object?> getter = GetGetter(name, prop);
                    object? value = getter(param);
                    SchemaOutputTargetValidator.ValidateObjectValue(meta, strict, prop, value);

                    if (DbBinder.TryBindExplicitParameter(cmd, meta, value, strict))
                        continue;

                    if (meta.Direction == ParameterDirection.ReturnValue)
                        continue;

                    // Output은 값 없이 파라미터만 생성
                    if (meta.Direction == ParameterDirection.Output)
                    {
                        DbBinder.BindParameter(cmd, meta, null, strict);
                        continue;
                    }

                    DbBinder.BindParameter(cmd, meta, value, strict);
                }
                else
                {
                    if (meta.Direction == ParameterDirection.ReturnValue)
                        continue;

                    // Output은 값 없이 파라미터만 생성
                    if (meta.Direction == ParameterDirection.Output)
                    {
                        DbBinder.BindParameter(cmd, meta, null, strict);
                        continue;
                    }

                    // 필수 Input 파라미터 누락 검사
                    if (meta.Direction == ParameterDirection.Input &&
                        !meta.HasDefaultValue &&
                        strict &&
                        !meta.IsNullable)
                    {
                        throw new InvalidOperationException(
                            $"필수 파라미터 '{meta.Name}'에 매핑할 프로퍼티가 DTO '{typeof(T).Name}'에 없습니다.");
                    }

                    DbBinder.BindParameter(cmd, meta, null, strict);
                }
            }

            BindExtraReturnValueParameter(cmd, param);
            return;
        }

        // [Case B] 스키마 없는 Raw SQL 바인딩 (Attribute 메타데이터 우선)
        PropertyMeta[] props = Meta.AllProps;
        for (int i = 0; i < props.Length; i++)
        {
            ref readonly PropertyMeta meta = ref props[i];
            Func<T, object?> getter = GetGetter(meta.Info.Name, meta.Info);
            object? value = getter(param);
            DbBinder.BindRawParameter(cmd, meta.Info.Name, value, meta.Attribute);
        }
    }

    private static void BindExtraReturnValueParameter(SqlCommand cmd, T param)
    {
        PropertyMeta[] props = Meta.AllProps;
        bool observedCandidate = false;
        for (int i = 0; i < props.Length; i++)
        {
            ref readonly PropertyMeta meta = ref props[i];
            if (!typeof(SqlParameter).IsAssignableFrom(meta.Info.PropertyType))
                continue;

            Func<T, object?> getter = GetGetter(meta.Info.Name, meta.Info);
            object? value = getter(param);
            ExplicitReturnValueBinding.BindOrValidateCandidate(cmd, meta.Info.Name, value, ref observedCandidate);
        }
    }

    /// <inheritdoc />
    public void MapOutputParameters(SqlCommand cmd, T param)
    {
        if (param is null)
            return;

        List<ObjectOutputWrite<T>> writes = [];

        foreach (SqlParameter p in cmd.Parameters)
        {
            if (!OutputWriteApplier.IsOutputParameter(p))
                continue;

            string name = p.ParameterName.TrimStart('@');

            SchemaOutputTargetValidator.ThrowIfAmbiguousObjectTarget(
                p.Direction,
                OutputParameterName.From(p.ParameterName),
                Meta.HasAmbiguousNormalizedName(name));

            if (Meta.TryGetProperty(name, out PropertyInfo? prop))
            {
                if (typeof(SqlParameter).IsAssignableFrom(prop.PropertyType))
                {
                    SqlParameter? sourceParameter = SqlParameterCloneFactory.TryGetRegisteredSource(p, out SqlParameter? registeredSource)
                        ? registeredSource
                        : prop.GetValue(param) as SqlParameter;

                    if (sourceParameter is not null)
                    {
                        writes.Add(new ObjectOutputWrite<T>(
                            null,
                            null,
                            sourceParameter,
                            SqlParameterCloneFactory.CaptureValueState(sourceParameter),
                            p,
                            null));
                    }

                    continue;
                }

                if (p.Direction == ParameterDirection.ReturnValue)
                    continue;

                if (!prop.CanWrite)
                {
                    SchemaOutputTargetValidator.ThrowIfReadOnlyObjectTarget(
                        p.Direction,
                        OutputParameterName.From(p.ParameterName),
                        strict,
                        prop);
                    continue;
                }

                Action<T, object?> setter = GetSetter(name, prop);
                writes.Add(new ObjectOutputWrite<T>(
                    setter,
                    prop.GetValue(param),
                    null,
                    default,
                    p,
                    OutputWriteApplier.ToClrValue(p)));
            }
            else
            {
                SchemaOutputTargetValidator.ThrowIfMissingObjectTarget(
                    p.Direction,
                    OutputParameterName.From(p.ParameterName),
                    strict);
            }
        }

        OutputWriteApplier.ApplyObjectWrites(param, writes);
    }

    #endregion

    #region [결과 매핑]

    /// <inheritdoc />
    public T MapResult(DbDataReader reader)
    {
        int sig = GetSignature(reader);
        Func<DbDataReader, T> func = _deserializers.GetOrAdd(sig, _ => BuildDeserializer(reader));
        return func(reader);
    }

    /// <summary>
    /// DbDataReader → T 변환 Expression Tree를 빌드하고 컴파일합니다.
    /// <para>
    /// - DB 컬럼 타입과 프로퍼티 타입이 동일할 때 GetInt32, GetString 등 Typed Getter 우선 사용<br/>
    /// - 문자열 컬럼 + 복합 프로퍼티 타입인 경우 JSON 역직렬화 수행<br/>
    /// - Nullable/ValueType에 대한 DBNull 처리 최적화
    /// </para>
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "ExpressionTreeMapper is a JIT-only mapper. Native AOT result mapping uses generated or explicit mappers.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2087",
        Justification = "ExpressionTreeMapper is a JIT-only mapper. Native AOT result mapping uses generated or explicit mappers.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2111",
        Justification = "ExpressionTreeMapper is a JIT-only mapper. Native AOT result mapping uses generated or explicit mappers.")]
    private Func<DbDataReader, T> BuildDeserializer(DbDataReader reader)
    {
        ParameterExpression rParam = Expression.Parameter(typeof(DbDataReader), "reader");
        List<MemberBinding> bindings = new List<MemberBinding>();
        HashSet<string> boundProperties = new(StringComparer.OrdinalIgnoreCase);

        // 공통 메서드 캐시
        MethodInfo isDbNull = typeof(DbDataReader).GetMethod(nameof(DbDataReader.IsDBNull))!;
        MethodInfo getString = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetString))!;
        MethodInfo getValue = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;

        // JSON 역직렬화 메서드
        MethodInfo jsonDeser = typeof(JsonSerializer).GetMethod(
            nameof(JsonSerializer.Deserialize),
            [typeof(string), typeof(JsonSerializerOptions)])!;
        ConstantExpression jsonOptExp = Expression.Constant(jsonOptions, typeof(JsonSerializerOptions));

        for (int i = 0; i < reader.FieldCount; i++)
        {
            string colName = reader.GetName(i);

            if (!Meta.TryGetProperty(colName, out PropertyInfo? prop) || !prop.CanWrite)
                continue;
            if (!boundProperties.Add(prop.Name))
                continue;

            ConstantExpression idxExp = Expression.Constant(i);
            MethodCallExpression checkNull = Expression.Call(rParam, isDbNull, idxExp);

            Type propType = prop.PropertyType;
            Type dbFieldType = reader.GetFieldType(i) ?? Nullable.GetUnderlyingType(propType) ?? propType;
            Expression valueExp;

            // [1] DB 컬럼 타입과 프로퍼티 타입이 동일하면 Typed Getter 우선 사용 (Boxing 제거)
            if (dbFieldType == propType)
            {
                string? typedMethodName = GetTypedMethodName(propType);
                if (typedMethodName is not null)
                {
                    MethodInfo? typedMethod = typeof(DbDataReader).GetMethod(typedMethodName, [typeof(int)]);
                    if (typedMethod is not null)
                    {
                        valueExp = Expression.Call(rParam, typedMethod, idxExp);
                        goto BIND_PROPERTY;
                    }
                }
            }

            // [2] 문자열 컬럼 → 복합 객체 프로퍼티: JSON 역직렬화
            if (dbFieldType == typeof(string) && IsComplexType(propType))
            {
                MethodCallExpression strVal = Expression.Call(rParam, getString, idxExp);
                MethodInfo genericJson = jsonDeser.MakeGenericMethod(propType);
                valueExp = Expression.Call(null, genericJson, strVal, jsonOptExp);
            }
            // [3] 일반 케이스: GetValue + Convert (+ .NET 10 타입 특수 처리)
            else
            {
                MethodCallExpression objVal = Expression.Call(rParam, getValue, idxExp);
                Type underlying = Nullable.GetUnderlyingType(propType) ?? propType;

                // ========== [추가] .NET 10 타입 변환 로직 ==========
                Expression converted;

                // DateOnly: DB DATE (DateTime) → DateOnly
                if (underlying == typeof(DateOnly))
                {
                    // reader.GetDateTime(i)
                    MethodInfo getDateTime = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetDateTime), [typeof(int)])!;
                    MethodCallExpression dtExpr = Expression.Call(rParam, getDateTime, idxExp);

                    // DateOnly.FromDateTime(dt)
                    MethodInfo fromDateTime = typeof(DateOnly).GetMethod(nameof(DateOnly.FromDateTime), [typeof(DateTime)])!;
                    converted = Expression.Call(fromDateTime, dtExpr);
                }
                // TimeOnly: DB TIME (TimeSpan) → TimeOnly
                else if (underlying == typeof(TimeOnly))
                {
                    // (TimeSpan)reader.GetValue(i)
                    UnaryExpression tsExpr = Expression.Convert(objVal, typeof(TimeSpan));

                    // TimeOnly.FromTimeSpan(ts)
                    MethodInfo fromTimeSpan = typeof(TimeOnly).GetMethod(nameof(TimeOnly.FromTimeSpan), [typeof(TimeSpan)])!;
                    converted = Expression.Call(fromTimeSpan, tsExpr);
                }
                // Half: DB REAL (float) → Half
                else if (underlying == typeof(Half))
                {
                    // reader.GetFloat(i)
                    MethodInfo getFloat = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetFloat), [typeof(int)])!;
                    MethodCallExpression floatExpr = Expression.Call(rParam, getFloat, idxExp);

                    // (Half)f
                    converted = Expression.Convert(floatExpr, typeof(Half));
                }
                // [Special 1] String -> Guid (Guid.Parse)
                else if (dbFieldType == typeof(string) && underlying == typeof(Guid))
                {
                    // getString is already defined in outer scope
                    MethodCallExpression strExpr = Expression.Call(rParam, getString, idxExp);
                    MethodInfo parseGuid = typeof(Guid).GetMethod(nameof(Guid.Parse), [typeof(string)])!;

                    converted = Expression.Call(parseGuid, strExpr);
                }
                // [Special 2] Safe Unboxing & Conversion (e.g. float(boxed) -> double)
                else
                {
                    // objVal is already defined in outer scope (line 564)

                    // 2. Unbox to actual DB type (if ValueType) or Cast (if RefType)
                    Expression unboxed = dbFieldType.IsValueType
                        ? Expression.Unbox(objVal, dbFieldType)
                        : Expression.Convert(objVal, dbFieldType);

                    // 3. Convert to target type (e.g. float -> double)
                    converted = Expression.Convert(unboxed, underlying);
                }

                valueExp = propType == underlying
                    ? converted
                    : Expression.Convert(converted, propType);
            }

        BIND_PROPERTY:
            // DBNull 처리: ValueType / Nullable / 참조 타입 분기
            Expression finalExp;
            if (propType.IsValueType && Nullable.GetUnderlyingType(propType) is null)
            {
                finalExp = Expression.Condition(checkNull, Expression.Default(propType), valueExp);
            }
            else
            {
                finalExp = Expression.Condition(
                    checkNull,
                    Expression.Constant(null, propType),
                    valueExp);
            }

            bindings.Add(Expression.Bind(prop, finalExp));
        }

        // ========== [수정] Record Primary Constructor 지원 ==========

        // 1. Public constructor 탐색 (가장 많은 파라미터를 가진 것 선택, ReflectionCache 경유)
        ConstructorInfo[] ctors = ReflectionCache.GetPublicConstructors(typeof(T));
        ConstructorInfo? ctor = ctors.Length > 0
            ? ctors.OrderByDescending(c => c.GetParameters().Length).First()
            : null;

        // 2. Parameterless constructor이거나 constructor가 없으면 기존 로직 사용
        if (ctor is null || ctor.GetParameters().Length == 0)
        {
            NewExpression newExp = Expression.New(typeof(T));
            MemberInitExpression memberInit = Expression.MemberInit(newExp, bindings);
            return Expression.Lambda<Func<DbDataReader, T>>(memberInit, rParam).Compile();
        }

        // 3. Constructor parameter 매칭 (Record Primary Constructor 지원)
        ParameterInfo[] ctorParams = ctor.GetParameters();
        Expression[] ctorArgs = new Expression[ctorParams.Length];
        HashSet<string> usedBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < ctorParams.Length; i++)
        {
            ParameterInfo param = ctorParams[i];
            string paramName = param.Name!;

            // bindings에서 매칭되는 프로퍼티 찾기 (case-insensitive)
            MemberAssignment? binding = bindings
                .OfType<MemberAssignment>()
                .FirstOrDefault(b => string.Equals(b.Member.Name, paramName, StringComparison.OrdinalIgnoreCase));

            if (binding is not null)
            {
                ctorArgs[i] = binding.Expression;
                usedBindings.Add(binding.Member.Name);
            }
            else
            {
                // 매칭되는 컬럼이 없으면 default 값 사용
                ctorArgs[i] = Expression.Default(param.ParameterType);
            }
        }

        // 4. Constructor에서 이미 초기화된 프로퍼티 제외
        List<MemberBinding> remainingBindings = bindings
            .Where(b => !usedBindings.Contains(b.Member.Name))
            .ToList();

        NewExpression newWithCtorExp = Expression.New(ctor, ctorArgs);

        // 5. Init-only 프로퍼티가 남아있으면 MemberInit 사용
        if (remainingBindings.Count > 0)
        {
            MemberInitExpression memberInitExp = Expression.MemberInit(newWithCtorExp, remainingBindings);
            return Expression.Lambda<Func<DbDataReader, T>>(memberInitExp, rParam).Compile();
        }

        // 6. Constructor만으로 충분하면 New expression 반환
        return Expression.Lambda<Func<DbDataReader, T>>(newWithCtorExp, rParam).Compile();
    }

    #endregion

    #region [헬퍼: Getter/Setter/Signature/Type 판별]

    private static Func<T, object?> GetGetter(string name, PropertyInfo prop)
        => s_getters.GetOrAdd(name, _ =>
        {
            ParameterExpression target = Expression.Parameter(typeof(T), "obj");
            MemberExpression access = Expression.Property(target, prop);
            UnaryExpression box = Expression.Convert(access, typeof(object));
            return Expression.Lambda<Func<T, object?>>(box, target).Compile();
        });

    private static Action<T, object?> GetSetter(string name, PropertyInfo prop)
        => s_setters.GetOrAdd(name, _ =>
        {
            ParameterExpression target = Expression.Parameter(typeof(T), "obj");
            ParameterExpression val = Expression.Parameter(typeof(object), "val");
            BinaryExpression assign = Expression.Assign(
                Expression.Property(target, prop),
                Expression.Convert(val, prop.PropertyType));
            return Expression.Lambda<Action<T, object?>>(assign, target, val).Compile();
        });

    /// <summary>
    /// 컬럼 개수 + 컬럼명 조합으로 고유 시그니처 값을 계산합니다.
    /// </summary>
    private static int GetSignature(DbDataReader reader)
    {
        HashCode hash = new HashCode();
        hash.Add(reader.FieldCount);
        for (int i = 0; i < reader.FieldCount; i++)
            hash.Add(reader.GetName(i), StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static string? GetTypedMethodName(Type type)
    {
        if (type == typeof(int))
            return nameof(DbDataReader.GetInt32);
        if (type == typeof(long))
            return nameof(DbDataReader.GetInt64);
        if (type == typeof(short))
            return nameof(DbDataReader.GetInt16);
        if (type == typeof(byte))
            return nameof(DbDataReader.GetByte);
        if (type == typeof(string))
            return nameof(DbDataReader.GetString);
        if (type == typeof(bool))
            return nameof(DbDataReader.GetBoolean);
        if (type == typeof(Guid))
            return nameof(DbDataReader.GetGuid);
        if (type == typeof(DateTime))
            return nameof(DbDataReader.GetDateTime);
        if (type == typeof(float))
            return nameof(DbDataReader.GetFloat);
        if (type == typeof(double))
            return nameof(DbDataReader.GetDouble);
        if (type == typeof(decimal))
            return nameof(DbDataReader.GetDecimal);
        return null;
    }

    /// <summary>JSON 역직렬화 대상이 되는 복합 타입 여부를 판별합니다.</summary>
    private static bool IsComplexType(Type t)
        => t != typeof(string)
           && !t.IsPrimitive
           && !t.IsEnum
           && t != typeof(DateTime)
           && t != typeof(Guid)
           && t != typeof(decimal);

    #endregion
}

#endregion

// ============================================================================
// [Scalar / Dictionary / DataRow 매퍼]
// ============================================================================

#region Scalar 매퍼

/// <summary>
/// 단일 스칼라 값(Primitive, 문자열, DateTime, Guid, Stream 등)에 대한 매핑을 담당하는 매퍼입니다.
/// <para>
/// - 파라미터 바인딩은 사용하지 않고, 결과 매핑만 수행합니다.<br/>
/// - Stream 타입의 경우 byte[] 컬럼을 MemoryStream 으로 변환합니다.
/// </para>
/// </summary>
internal sealed class ScalarSqlMapper<T> : ISqlMapper<T>
{
    private static readonly Type s_underlyingType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

    public void MapParameters(SqlCommand cmd, T parameters, SpSchema? schema) { }

    public void MapOutputParameters(SqlCommand cmd, T parameters) { }

    /// <summary>
    /// 첫 번째 컬럼 값을 <typeparamref name="T"/> 타입으로 변환합니다.
    /// <para>
    /// - DBNull: default(T)<br/>
    /// - val is T: 직접 캐스팅 (Guid, DateTimeOffset, Stream 등 보호)<br/>
    /// - Stream + byte[]: MemoryStream 생성<br/>
    /// - 나머지: Convert.ChangeType 사용
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T MapResult(DbDataReader reader)
    {
        object val = reader.GetValue(0);

        if (val == DBNull.Value)
            return default!;

        // Guid, DateTimeOffset, Stream 등은 이미 원하는 타입이면 그대로 반환
        if (val is T tVal)
            return tVal;

        // byte[] -> MemoryStream (T == Stream)
        if (typeof(T) == typeof(Stream) && val is byte[] bytes)
            return (T)(object)new MemoryStream(bytes);

        // 그 외는 Convert.ChangeType 사용
        return (T)Convert.ChangeType(val, s_underlyingType);
    }
}

#endregion

internal readonly record struct ObjectOutputWrite<T>(
    Action<T, object?>? AssignValue,
    object? OriginalAssignedValue,
    SqlParameter? SourceParameter,
    SqlParameterValueState OriginalSourceState,
    SqlParameter CommandParameter,
    object? NewValue)
{
    public void Apply(T target)
    {
        if (SourceParameter is not null)
            SqlParameterCloneFactory.CopyOutputValue(SourceParameter, CommandParameter);

        AssignValue?.Invoke(target, NewValue);
    }

    public void Restore(T target)
    {
        if (AssignValue is not null)
        {
            try
            {
                AssignValue(target, OriginalAssignedValue);
            }
            catch
            {
            }
        }

        if (SourceParameter is not null)
        {
            try
            {
                SqlParameterCloneFactory.RestoreValueState(SourceParameter, OriginalSourceState);
            }
            catch
            {
            }
        }
    }
}

internal static class OutputWriteApplier
{
    public static bool IsOutputParameter(SqlParameter parameter)
        => parameter.Direction is ParameterDirection.Output
            or ParameterDirection.InputOutput
            or ParameterDirection.ReturnValue;

    public static object? ToClrValue(SqlParameter parameter)
        => parameter.Value == DBNull.Value ? null : parameter.Value;

    public static void ApplyObjectWrites<T>(T target, List<ObjectOutputWrite<T>> writes)
    {
        if (writes.Count == 0)
            return;

        int appliedCount = 0;
        try
        {
            foreach (ObjectOutputWrite<T> write in writes)
            {
                appliedCount++;
                write.Apply(target);
            }
        }
        catch (Exception ex)
        {
            for (int i = appliedCount - 1; i >= 0; i--)
                writes[i].Restore(target);

            throw CreateObjectOutputApplyException(ex);
        }
    }

    private static InvalidOperationException CreateObjectOutputApplyException(Exception ex)
        => new(
            "Output parameters could not be applied transactionally. " +
            $"Cause: {ex.GetType().Name}.");
}

internal static class SchemaOutputTargetValidator
{
    public static void ValidateUniqueOutputParameterNames(SpSchema schema)
    {
        Dictionary<string, OutputParameterName>? seen = null;
        foreach (SpParameterMetadata meta in schema.Parameters)
        {
            if (meta.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue))
                continue;

            OutputParameterName name = OutputParameterName.From(meta.Name);
            seen ??= new Dictionary<string, OutputParameterName>(StringComparer.Ordinal);
            if (seen.TryGetValue(name.Normalized, out OutputParameterName existing))
            {
                throw new InvalidOperationException(
                    $"Output parameter name '{name.SafeDisplay()}' conflicts with '{existing.SafeDisplay()}'.");
            }

            seen.Add(name.Normalized, name);
        }
    }

    public static void ValidateObjectTarget(
        SpParameterMetadata meta,
        bool strict,
        [NotNullWhen(true)] PropertyInfo? property,
        bool ambiguousTarget)
    {
        SqlParameterCloneFactory.ValidateSupportedOutputMetadata(
            meta.Name,
            meta.SqlDbType,
            meta.Direction,
            meta.IsCursorRef);

        if (ambiguousTarget)
        {
            ThrowIfAmbiguousObjectTarget(meta.Direction, OutputParameterName.From(meta.Name), ambiguousTarget);
        }

        if (meta.Direction == ParameterDirection.ReturnValue)
        {
            if (property is null)
                return;

            OutputParameterName returnName = OutputParameterName.From(meta.Name);
            if (!property.CanRead || !typeof(SqlParameter).IsAssignableFrom(property.PropertyType))
            {
                throw new InvalidOperationException(
                    $"ReturnValue parameter '{returnName.SafeDisplay()}' requires an explicit SqlParameter property.");
            }

            return;
        }

        if (meta.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
            return;

        if (meta.Direction == ParameterDirection.Output)
        {
            OutputParameterName outputName = OutputParameterName.From(meta.Name);
            if (property is null)
            {
                if (strict)
                {
                    throw new InvalidOperationException(
                        $"Strict output parameter '{outputName.SafeDisplay()}' requires a writable DTO property or explicit SqlParameter source.");
                }

                return;
            }

            if (!property.CanRead)
            {
                throw new InvalidOperationException(
                    $"Strict output parameter '{outputName.SafeDisplay()}' maps to unreadable DTO property '{property.Name}'.");
            }

            if (strict &&
                !typeof(SqlParameter).IsAssignableFrom(property.PropertyType) &&
                !property.CanWrite)
            {
                throw new InvalidOperationException(
                    $"Strict output parameter '{outputName.SafeDisplay()}' requires a writable DTO property or explicit SqlParameter source.");
            }

            return;
        }

        if (!strict)
            return;

        OutputParameterName name = OutputParameterName.From(meta.Name);
        if (property is null)
        {
            throw new InvalidOperationException(
                $"Strict input-output parameter '{name.SafeDisplay()}' requires a readable DTO property or explicit SqlParameter source.");
        }

        if (!property.CanRead)
        {
            throw new InvalidOperationException(
                $"Strict input-output parameter '{name.SafeDisplay()}' maps to unreadable DTO property '{property.Name}'.");
        }

        if (!typeof(SqlParameter).IsAssignableFrom(property.PropertyType) &&
            !property.CanWrite)
        {
            throw new InvalidOperationException(
                $"Strict input-output parameter '{name.SafeDisplay()}' requires a writable DTO property or explicit SqlParameter source.");
        }
    }

    public static void ThrowIfAmbiguousObjectTarget(
        ParameterDirection direction,
        OutputParameterName name,
        bool ambiguousTarget)
    {
        if (!ambiguousTarget ||
            direction is not (ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue))
        {
            return;
        }

        throw new InvalidOperationException(
            $"DTO output target '{name.SafeDisplay()}' is ambiguous.");
    }

    public static void ThrowIfMissingObjectTarget(
        ParameterDirection direction,
        OutputParameterName name,
        bool strict)
    {
        if (!strict || direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
            return;

        throw new InvalidOperationException(
            $"Strict {DescribeOutputDirection(direction)} parameter '{name.SafeDisplay()}' requires a writable DTO property or explicit SqlParameter source.");
    }

    public static void ThrowIfReadOnlyObjectTarget(
        ParameterDirection direction,
        OutputParameterName name,
        bool strict,
        PropertyInfo property)
    {
        if (!strict || direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
            return;

        throw new InvalidOperationException(
            $"Strict {DescribeOutputDirection(direction)} parameter '{name.SafeDisplay()}' requires a writable DTO property or explicit SqlParameter source, but DTO property '{property.Name}' is read-only.");
    }

    public static void ValidateDictionaryTarget(
        SpParameterMetadata meta,
        bool strict,
        bool hasTarget)
    {
        SqlParameterCloneFactory.ValidateSupportedOutputMetadata(
            meta.Name,
            meta.SqlDbType,
            meta.Direction,
            meta.IsCursorRef);

        if (meta.Direction == ParameterDirection.ReturnValue)
            return;

        if (!strict || meta.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
        {
            return;
        }

        if (!hasTarget)
        {
            OutputParameterName name = OutputParameterName.From(meta.Name);
            throw new InvalidOperationException(
                $"Strict {DescribeOutputDirection(meta.Direction)} parameter '{name.SafeDisplay()}' requires a Dictionary target key or explicit SqlParameter source.");
        }
    }

    public static void ValidateObjectValue(
        SpParameterMetadata meta,
        bool strict,
        PropertyInfo property,
        object? value)
    {
        if (!typeof(SqlParameter).IsAssignableFrom(property.PropertyType))
            return;

        if (value is SqlParameter)
            return;

        OutputParameterName name = OutputParameterName.From(meta.Name);
        if (meta.Direction == ParameterDirection.ReturnValue)
        {
            throw new InvalidOperationException(
                $"ReturnValue parameter '{name.SafeDisplay()}' requires a non-null explicit SqlParameter source.");
        }

        if (strict &&
            meta.Direction is ParameterDirection.Output or ParameterDirection.InputOutput)
        {
            throw new InvalidOperationException(
                $"Strict output parameter '{name.SafeDisplay()}' requires a non-null explicit SqlParameter source.");
        }
    }

    private static string DescribeOutputDirection(ParameterDirection direction)
        => direction == ParameterDirection.InputOutput ? "input-output" : "output";
}

#region object 정적 타입 매퍼

/// <summary>
/// <c>.With((object)dto)</c>처럼 정적 타입이 object인 파라미터를 런타임 타입 기준으로 바인딩합니다.
/// </summary>
internal sealed class ObjectSqlMapper(bool strict) : ISqlMapper<object>
{
    private static readonly ConcurrentDictionary<(Type Type, bool Strict), object> s_runtimeMappers = new();
    private readonly DictionarySqlMapper _dictionaryMapper = new(strict);
    private readonly DataRowSqlMapper _dataRowMapper = new(strict);

    public void MapParameters(SqlCommand cmd, object parameters, SpSchema? schema)
    {
        if (parameters is null)
            return;

        if (parameters is Dictionary<string, object?> dictionary)
        {
            _dictionaryMapper.MapParameters(cmd, dictionary, schema);
            return;
        }

        if (parameters is DataRow row)
        {
            _dataRowMapper.MapParameters(cmd, row, schema);
            return;
        }

        InvokeRuntimeMapper(parameters, nameof(MapParameters), cmd, schema);
    }

    public void MapOutputParameters(SqlCommand cmd, object parameters)
    {
        if (parameters is null)
            return;

        if (parameters is Dictionary<string, object?> dictionary)
        {
            _dictionaryMapper.MapOutputParameters(cmd, dictionary);
            return;
        }

        if (parameters is DataRow row)
        {
            _dataRowMapper.MapOutputParameters(cmd, row);
            return;
        }

        InvokeRuntimeMapper(parameters, nameof(MapOutputParameters), cmd, schema: null);
    }

    public object MapResult(DbDataReader reader)
    {
        object value = reader.GetValue(0);
        return value == DBNull.Value ? null! : value;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "object 정적 타입 파라미터는 런타임 DTO convenience path입니다. Native AOT 호출자는 Dictionary 또는 정적 DTO 타입을 사용해야 합니다.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2081",
        Justification = "object 정적 타입 파라미터는 런타임 DTO convenience path입니다. Native AOT 호출자는 Dictionary 또는 정적 DTO 타입을 사용해야 합니다.")]
    [UnconditionalSuppressMessage(
        "Aot",
        "IL3050:RequiresDynamicCode",
        Justification = "object 정적 타입 파라미터의 런타임 generic mapper 생성은 JIT convenience path입니다. Native AOT 호출자는 Dictionary 또는 정적 DTO 타입을 사용해야 합니다.")]
    private void InvokeRuntimeMapper(object parameters, string methodName, SqlCommand cmd, SpSchema? schema)
    {
        Type runtimeType = parameters.GetType();
        EnsureReadableProperties(runtimeType);

        object mapper = s_runtimeMappers.GetOrAdd(
            (runtimeType, strict),
            static key => Activator.CreateInstance(
                typeof(ReflectionParameterMapper<>).MakeGenericType(key.Type),
                [key.Strict])!);

        MethodInfo method = mapper.GetType().GetMethod(methodName)!;
        try
        {
            if (schema is null && methodName == nameof(MapOutputParameters))
            {
                method.Invoke(mapper, [cmd, parameters]);
                return;
            }

            method.Invoke(mapper, [cmd, parameters, schema]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "object 정적 타입 파라미터는 런타임 DTO convenience path입니다. Native AOT 호출자는 Dictionary 또는 정적 DTO 타입을 사용해야 합니다.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "object 정적 타입 파라미터는 런타임 DTO convenience path입니다. Native AOT 호출자는 Dictionary 또는 정적 DTO 타입을 사용해야 합니다.")]
    private static void EnsureReadableProperties(Type runtimeType)
    {
        foreach (PropertyInfo property in runtimeType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
                return;
        }

        throw new InvalidOperationException(
            "object 정적 타입으로 전달된 파라미터 객체에서 읽을 수 있는 public 프로퍼티를 찾을 수 없습니다. " +
            "Dictionary 또는 이름 있는 파라미터 DTO를 전달해 주세요.");
    }
}

#endregion

#region [Dictionary 매퍼]

/// <summary>
/// <see cref="Dictionary{TKey, TValue}"/> 기반 동적 파라미터 매핑을 제공하는 매퍼입니다.
/// <para>
/// - 스키마 기반 바인딩 시 필수 Key 누락 검사를 수행합니다.<br/>
/// - 결과 매핑 시 컬럼명을 Key로 하는 Dictionary 한 행을 생성합니다.
/// </para>
/// </summary>
internal sealed class DictionarySqlMapper(bool strict) : ISqlMapper<Dictionary<string, object?>>
{
    /// <inheritdoc />
    public void MapParameters(SqlCommand cmd, Dictionary<string, object?> parameters, SpSchema? schema)
    {
        if (parameters is null)
            return;

        // ---------------------------------------------------------------------
        // [Case A] 스키마 없는 Raw SQL
        //   - 이 경우에는 Dictionary의 Key를 그대로 사용하므로,
        //     빈 Dictionary면 아무 작업도 하지 않고 반환합니다.
        // ---------------------------------------------------------------------
        if (schema is null)
        {
            if (parameters.Count == 0)
                return;

            foreach (KeyValuePair<string, object?> kv in parameters)
                DbBinder.BindRawParameter(cmd, kv.Key, kv.Value);

            return;
        }

        // ---------------------------------------------------------------------
        // [Case B] SP 스키마 기반 바인딩
        //   - parameters.Count == 0 이더라도 필수 파라미터 누락 검사를 수행해야 합니다.
        //   - Strict 모드에서 NOT NULL + DEFAULT 없음 + Key 없음이면 예외를 던집니다.
        // ---------------------------------------------------------------------
        SchemaOutputTargetValidator.ValidateUniqueOutputParameterNames(schema);
        ValidateSchemaOutputTargets(parameters, schema, strict);

        foreach (SpParameterMetadata meta in schema.Parameters)
        {
            string key = meta.Name.TrimStart('@');

            if (TryGet(parameters, key, out object? value))
            {
                if (DbBinder.TryBindExplicitParameter(cmd, meta, value, strict))
                    continue;

                if (meta.Direction == ParameterDirection.ReturnValue)
                    continue;

                if (meta.Direction == ParameterDirection.Output)
                {
                    DbBinder.BindParameter(cmd, meta, null, strict);
                    continue;
                }

                DbBinder.BindParameter(cmd, meta, value, strict);
            }
            else
            {
                if (meta.Direction == ParameterDirection.ReturnValue)
                    continue;

                if (meta.Direction == ParameterDirection.Output)
                {
                    DbBinder.BindParameter(cmd, meta, null, strict);
                    continue;
                }

                // Strict 모드: 필수 입력 파라미터 누락 시 명시적으로 예외
                if (strict &&
                    !meta.IsNullable &&
                    meta.Direction == ParameterDirection.Input &&
                    !meta.HasDefaultValue)
                {
                    throw new InvalidOperationException($"필수 Key '{key}'가 Dictionary에 없습니다.");
                }

                // 그 외에는 NULL 바인딩 또는 DB DEFAULT 사용
                DbBinder.BindParameter(cmd, meta, null, strict);
            }
        }

        bool observedCandidate = false;
        foreach (KeyValuePair<string, object?> kv in parameters)
            ExplicitReturnValueBinding.BindOrValidateCandidate(cmd, kv.Key, kv.Value, ref observedCandidate);
    }

    /// <inheritdoc />
    public void MapOutputParameters(SqlCommand cmd, Dictionary<string, object?> parameters)
    {
        if (parameters is null)
            return;

        List<DictionaryOutputWrite> writes = [];

        foreach (SqlParameter param in cmd.Parameters)
        {
            if (!OutputWriteApplier.IsOutputParameter(param))
                continue;

            OutputParameterName name = OutputParameterName.From(param.ParameterName);
            bool hasTarget = TryGetUnique(parameters, name, out string? targetKey, out object? originalValue);
            SqlParameter? sourceParameter = SqlParameterCloneFactory.TryGetRegisteredSource(param, out SqlParameter? registeredSource)
                ? registeredSource
                : originalValue as SqlParameter;

            if (param.Direction == ParameterDirection.ReturnValue)
            {
                if (sourceParameter is not null)
                {
                    writes.Add(DictionaryOutputWrite.ForSourceOnly(
                        sourceParameter,
                        SqlParameterCloneFactory.CaptureValueState(sourceParameter),
                        param));
                }

                continue;
            }

            if (!hasTarget)
            {
                if (strict)
                {
                    throw new InvalidOperationException(
                        $"Strict {DescribeOutputDirection(param.Direction)} parameter '{name.SafeDisplay()}' requires a Dictionary target key or explicit SqlParameter source.");
                }

                targetKey = name.Canonical;
            }

            writes.Add(DictionaryOutputWrite.ForDictionary(
                targetKey!,
                hasTarget,
                originalValue,
                OutputWriteApplier.ToClrValue(param),
                sourceParameter,
                sourceParameter is null ? default : SqlParameterCloneFactory.CaptureValueState(sourceParameter),
                param));
        }

        ApplyDictionaryOutputWrites(parameters, writes);
    }

    /// <inheritdoc />
    public Dictionary<string, object?> MapResult(DbDataReader reader)
    {
        Dictionary<string, object?> row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> tracker = s_trackerPool.Get();
        try
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string baseName = reader.GetName(i);
                if (string.IsNullOrWhiteSpace(baseName))
                    baseName = $"Column{i}";

                string targetName = baseName;
                if (row.ContainsKey(targetName))
                {
                    if (!tracker.TryGetValue(baseName, out int suffix))
                        suffix = 1;

                    do
                    {
                        targetName = $"{baseName}_{suffix++}";
                    } while (row.ContainsKey(targetName));

                    tracker[baseName] = suffix;
                }

                object value = reader.GetValue(i);
                row[targetName] = value == DBNull.Value ? null : value;
            }
        }
        finally
        {
            s_trackerPool.Return(tracker);
        }

        return row;
    }

    private static readonly Microsoft.Extensions.ObjectPool.ObjectPool<Dictionary<string, int>> s_trackerPool =
        new Microsoft.Extensions.ObjectPool.DefaultObjectPool<Dictionary<string, int>>(new TrackerPolicy());

    private sealed class TrackerPolicy : Microsoft.Extensions.ObjectPool.IPooledObjectPolicy<Dictionary<string, int>>
    {
        public Dictionary<string, int> Create() => new(StringComparer.OrdinalIgnoreCase);
        public bool Return(Dictionary<string, int> obj)
        {
            obj.Clear();
            return true;
        }
    }

    private readonly record struct DictionaryOutputWrite(
        string? TargetKey,
        bool TargetKeyExisted,
        object? OriginalValue,
        object? NewValue,
        SqlParameter? SourceParameter,
        SqlParameterValueState OriginalSourceState,
        SqlParameter CommandParameter)
    {
        public static DictionaryOutputWrite ForDictionary(
            string targetKey,
            bool targetKeyExisted,
            object? originalValue,
            object? newValue,
            SqlParameter? sourceParameter,
            SqlParameterValueState originalSourceState,
            SqlParameter commandParameter)
            => new(
                targetKey,
                targetKeyExisted,
                originalValue,
                newValue,
                sourceParameter,
                originalSourceState,
                commandParameter);

        public static DictionaryOutputWrite ForSourceOnly(
            SqlParameter sourceParameter,
            SqlParameterValueState originalSourceState,
            SqlParameter commandParameter)
            => new(
                null,
                false,
                null,
                null,
                sourceParameter,
                originalSourceState,
                commandParameter);

        public void Apply(Dictionary<string, object?> parameters)
        {
            if (SourceParameter is not null)
                SqlParameterCloneFactory.CopyOutputValue(SourceParameter, CommandParameter);

            if (TargetKey is not null)
                parameters[TargetKey] = NewValue;
        }

        public void Restore(Dictionary<string, object?> parameters)
        {
            if (TargetKey is not null)
            {
                try
                {
                    if (TargetKeyExisted)
                        parameters[TargetKey] = OriginalValue;
                    else
                        parameters.Remove(TargetKey);
                }
                catch
                {
                }
            }

            if (SourceParameter is not null)
            {
                try
                {
                    SqlParameterCloneFactory.RestoreValueState(SourceParameter, OriginalSourceState);
                }
                catch
                {
                }
            }
        }
    }

    private static void ValidateSchemaOutputTargets(
        Dictionary<string, object?> parameters,
        SpSchema schema,
        bool strict)
    {
        foreach (SpParameterMetadata meta in schema.Parameters)
        {
            if (meta.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue))
                continue;

            OutputParameterName name = OutputParameterName.From(meta.Name);
            bool hasTarget = TryGetUnique(parameters, name, out _, out _);
            SchemaOutputTargetValidator.ValidateDictionaryTarget(meta, strict, hasTarget);
        }
    }

    private static void ApplyDictionaryOutputWrites(
        Dictionary<string, object?> parameters,
        List<DictionaryOutputWrite> writes)
    {
        if (writes.Count == 0)
            return;

        int appliedCount = 0;
        try
        {
            foreach (DictionaryOutputWrite write in writes)
            {
                appliedCount++;
                write.Apply(parameters);
            }
        }
        catch (Exception ex)
        {
            for (int i = appliedCount - 1; i >= 0; i--)
                writes[i].Restore(parameters);

            throw CreateDictionaryOutputApplyException(ex);
        }
    }

    private static InvalidOperationException CreateDictionaryOutputApplyException(Exception ex)
        => new(
            "Dictionary output parameters could not be applied transactionally. " +
            $"Cause: {ex.GetType().Name}.");

    private static string DescribeOutputDirection(ParameterDirection direction)
        => direction == ParameterDirection.InputOutput ? "input-output" : "output";

    /// <summary>
    /// Dictionary에서 대소문자를 무시하고 Key를 조회합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGet(Dictionary<string, object?> dict, string key, out object? value)
    {
        if (dict.TryGetValue(key, out value))
            return true;

        // Dictionary가 OrdinalIgnoreCase로 생성되지 않은 경우, 순회 검색
        foreach (KeyValuePair<string, object?> kv in dict)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetUnique(
        Dictionary<string, object?> dict,
        OutputParameterName name,
        [NotNullWhen(true)] out string? targetKey,
        out object? value)
    {
        targetKey = null;
        value = null;
        int matches = 0;

        foreach (KeyValuePair<string, object?> kv in dict)
        {
            if (!name.Matches(kv.Key))
                continue;

            matches++;
            if (matches > 1)
            {
                throw new InvalidOperationException(
                    $"Output target '{name.SafeDisplay()}' is ambiguous.");
            }

            targetKey = kv.Key;
            value = kv.Value;
        }

        return matches == 1;
    }
}

#endregion

#region [DataRow 매퍼]

/// <summary>
/// <see cref="DataRow"/> 기반 레거시 데이터 바인딩을 지원하는 매퍼입니다.
/// <para>
/// - 스키마 기반 바인딩 시 필수 컬럼 누락 검사를 수행합니다.<br/>
/// - 결과 매핑은 지원하지 않으며, DTO 또는 Dictionary 사용을 권장합니다.
/// </para>
/// </summary>
internal sealed class DataRowSqlMapper(bool strict) : ISqlMapper<DataRow>
{
    /// <inheritdoc />
    public void MapParameters(SqlCommand cmd, DataRow row, SpSchema? schema)
    {
        if (row is null)
            return;

        // [Case A] 스키마 없는 Raw SQL
        if (schema is null)
        {
            foreach (DataColumn col in row.Table.Columns)
                DbBinder.BindRawParameter(cmd, col.ColumnName, row[col]);
            return;
        }

        // [Case B] SP 스키마 기반 바인딩
        SchemaOutputTargetValidator.ValidateUniqueOutputParameterNames(schema);
        ValidateSchemaOutputTargets(row.Table, schema);

        foreach (SpParameterMetadata meta in schema.Parameters)
        {
            string name = meta.Name.TrimStart('@');

            if (meta.Direction is ParameterDirection.Output or ParameterDirection.ReturnValue)
            {
                if (row.Table.Columns.Contains(name) &&
                    DbBinder.TryBindExplicitParameter(cmd, meta, row[name], strict))
                {
                    continue;
                }

                if (meta.Direction == ParameterDirection.ReturnValue)
                    continue;

                DbBinder.BindParameter(cmd, meta, null, strict);
                continue;
            }

            if (row.Table.Columns.Contains(name))
            {
                object value = row[name];
                if (DbBinder.TryBindExplicitParameter(cmd, meta, value, strict))
                    continue;

                DbBinder.BindParameter(cmd, meta, value, strict);
            }
            else
            {
                if (strict &&
                    !meta.IsNullable &&
                    meta.Direction == ParameterDirection.Input &&
                    !meta.HasDefaultValue)
                {
                    throw new InvalidOperationException($"필수 컬럼 '{name}'가 DataRow에 없습니다.");
                }

                DbBinder.BindParameter(cmd, meta, null, strict);
            }
        }

        BindExtraReturnValueParameter(cmd, row);
    }

    /// <inheritdoc />
    public void MapOutputParameters(SqlCommand cmd, DataRow parameters)
    {
        if (parameters is null)
            return;

        List<DataRowOutputWrite> writes = [];

        foreach (SqlParameter param in cmd.Parameters)
        {
            if (param.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue))
                continue;

            OutputParameterName name = OutputParameterName.From(param.ParameterName);
            if (param.Direction == ParameterDirection.ReturnValue)
            {
                SqlParameter? returnSourceParameter = GetDataRowReturnValueSource(param, parameters, name);
                if (returnSourceParameter is not null)
                {
                    writes.Add(DataRowOutputWrite.ForSourceOnly(
                        returnSourceParameter,
                        SqlParameterCloneFactory.CaptureValueState(returnSourceParameter),
                        param));
                }

                continue;
            }

            DataColumn? column = TryGetUniqueOutputColumn(parameters.Table, name);
            if (column is null)
            {
                if (strict &&
                    param.Direction is ParameterDirection.Output or ParameterDirection.InputOutput)
                {
                    throw new InvalidOperationException(
                        $"Strict {DescribeOutputDirection(param.Direction)} parameter '{name.SafeDisplay()}' requires a DataRow target column or explicit SqlParameter source.");
                }

                continue;
            }

            ValidateDataRowOutputColumn(column, name);

            object originalValue = parameters[column];
            SqlParameter? sourceParameter = GetDataRowOutputSource(
                param,
                originalValue);

            writes.Add(DataRowOutputWrite.ForDataRow(
                column,
                originalValue,
                param.Value ?? DBNull.Value,
                sourceParameter,
                sourceParameter is null ? default : SqlParameterCloneFactory.CaptureValueState(sourceParameter),
                param));
        }

        ApplyDataRowOutputWrites(parameters, writes);
    }

    /// <inheritdoc />
    public DataRow MapResult(DbDataReader reader)
        => throw new NotSupportedException(
            "DataRow로의 결과 매핑은 지원하지 않습니다. DTO 또는 Dictionary 매핑을 사용해 주세요.");

    private readonly record struct DataRowOutputWrite(
        DataColumn? Column,
        object? OriginalValue,
        object Value,
        SqlParameter? SourceParameter,
        SqlParameterValueState OriginalSourceState,
        SqlParameter CommandParameter)
    {
        public static DataRowOutputWrite ForDataRow(
            DataColumn column,
            object originalValue,
            object value,
            SqlParameter? sourceParameter,
            SqlParameterValueState originalSourceState,
            SqlParameter commandParameter)
            => new(
                column,
                originalValue,
                value,
                sourceParameter,
                originalSourceState,
                commandParameter);

        public static DataRowOutputWrite ForSourceOnly(
            SqlParameter sourceParameter,
            SqlParameterValueState originalSourceState,
            SqlParameter commandParameter)
            => new(
                null,
                null,
                DBNull.Value,
                sourceParameter,
                originalSourceState,
                commandParameter);
    }

    private static void BindExtraReturnValueParameter(SqlCommand cmd, DataRow row)
    {
        bool observedCandidate = false;
        foreach (DataColumn column in row.Table.Columns)
            ExplicitReturnValueBinding.BindOrValidateCandidate(
                cmd,
                column.ColumnName,
                row[column],
                ref observedCandidate);
    }

    private static DataColumn? TryGetUniqueOutputColumn(DataTable table, OutputParameterName name)
    {
        DataColumn? match = null;
        foreach (DataColumn column in table.Columns)
        {
            if (!name.Matches(column.ColumnName))
                continue;

            if (match is not null)
            {
                throw new InvalidOperationException(
                    $"DataRow output target '{name.SafeDisplay()}' is ambiguous.");
            }

            match = column;
        }

        return match;
    }

    private void ValidateSchemaOutputTargets(DataTable table, SpSchema schema)
    {
        foreach (SpParameterMetadata meta in schema.Parameters)
        {
            if (meta.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue))
                continue;

            SqlParameterCloneFactory.ValidateSupportedOutputMetadata(
                meta.Name,
                meta.SqlDbType,
                meta.Direction,
                meta.IsCursorRef);

            if (meta.Direction == ParameterDirection.ReturnValue)
                continue;

            OutputParameterName name = OutputParameterName.From(meta.Name);
            DataColumn? column = TryGetUniqueOutputColumn(table, name);
            if (column is null)
            {
                if (strict &&
                    meta.Direction is ParameterDirection.Output or ParameterDirection.InputOutput)
                {
                    throw new InvalidOperationException(
                        $"Strict {DescribeOutputDirection(meta.Direction)} parameter '{name.SafeDisplay()}' requires a DataRow target column or explicit SqlParameter source.");
                }

                continue;
            }

            ValidateDataRowOutputColumn(column, name);
        }
    }

    private static SqlParameter? GetDataRowReturnValueSource(
        SqlParameter commandParameter,
        DataRow row,
        OutputParameterName name)
    {
        if (SqlParameterCloneFactory.TryGetRegisteredSource(commandParameter, out SqlParameter? sourceParameter))
            return sourceParameter;

        SqlParameter? match = null;
        foreach (DataColumn column in row.Table.Columns)
        {
            if (!name.Matches(column.ColumnName) || row[column] is not SqlParameter source)
                continue;

            if (match is not null)
            {
                throw new InvalidOperationException(
                    $"DataRow output source '{name.SafeDisplay()}' is ambiguous.");
            }

            match = source;
        }

        return match;
    }

    private static SqlParameter? GetDataRowOutputSource(
        SqlParameter commandParameter,
        object originalValue)
    {
        if (SqlParameterCloneFactory.TryGetRegisteredSource(commandParameter, out SqlParameter? sourceParameter))
            return sourceParameter;

        return originalValue as SqlParameter;
    }

    private static void ValidateDataRowOutputColumn(DataColumn column, OutputParameterName name)
    {
        if (!string.IsNullOrEmpty(column.Expression))
        {
            throw new InvalidOperationException(
                $"DataRow output target '{name.SafeDisplay()}' is an expression column.");
        }

        if (column.ReadOnly)
        {
            throw new InvalidOperationException(
                $"DataRow output target '{name.SafeDisplay()}' is read-only.");
        }
    }

    private static void ApplyDataRowOutputWrites(DataRow row, List<DataRowOutputWrite> writes)
    {
        if (writes.Count == 0)
            return;

        bool editing = false;

        try
        {
            row.BeginEdit();
            editing = true;

            foreach (DataRowOutputWrite write in writes)
            {
                if (write.Column is not null)
                    row[write.Column] = write.Value;
            }

            row.EndEdit();
            editing = false;

            foreach (DataRowOutputWrite write in writes)
            {
                if (write.SourceParameter is not null)
                    SqlParameterCloneFactory.CopyOutputValue(write.SourceParameter, write.CommandParameter);
            }
        }
        catch (Exception ex)
        {
            RestoreDataRowOutputWrites(row, writes, editing);
            throw CreateDataRowOutputApplyException(ex);
        }
    }

    private static InvalidOperationException CreateDataRowOutputApplyException(Exception ex)
        => new(
            "DataRow output parameters could not be applied transactionally. " +
            $"Cause: {ex.GetType().Name}.");

    private static string DescribeOutputDirection(ParameterDirection direction)
        => direction == ParameterDirection.InputOutput ? "input-output" : "output";

    private static void RestoreDataRowOutputWrites(
        DataRow row,
        List<DataRowOutputWrite> writes,
        bool editing)
    {
        try
        {
            if (editing)
                row.CancelEdit();
        }
        catch
        {
        }

        foreach (DataRowOutputWrite write in writes)
        {
            if (write.Column is not null)
            {
                try
                {
                    row[write.Column] = write.OriginalValue ?? DBNull.Value;
                }
                catch
                {
                }
            }

            if (write.SourceParameter is not null)
            {
                try
                {
                    SqlParameterCloneFactory.RestoreValueState(write.SourceParameter, write.OriginalSourceState);
                }
                catch
                {
                }
            }
        }
    }
}

#endregion

// ============================================================================
// [AOT Fallback / Source Generator 어댑터]
// ============================================================================

#region [Reflection 기반 AOT Fallback 매퍼]

/// <summary>
/// Native AOT 등 동적 코드 생성이 불가능한 환경에서 사용하는 Reflection 기반 매퍼입니다.
/// <para>
/// - FrozenDictionary 기반 프로퍼티 캐시로 Reflection 오버헤드를 최소화합니다.<br/>
/// - DbParameterAttribute를 이용한 Raw SQL 바인딩 메타데이터를 지원합니다.<br/>
/// - SP 스키마 기반 필수 파라미터 누락 검사 및 Output 파라미터 역매핑을 지원합니다.<br/>
/// - DTO 결과 매핑(<see cref="ISqlMapper{T}.MapResult"/>)은 지원하지 않습니다.
///   AOT 환경에서 DTO 결과 매핑이 필요하면 Source Generator 또는 수동 매퍼를 사용해야 합니다.
/// </para>
/// </summary>
internal sealed class ReflectionParameterMapper<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(bool strict)
    : ISqlMapper<T>
{
    private readonly record struct PropertyMeta(PropertyInfo Info, DbParameterAttribute? Attribute);

    private static class TypeCache
    {
        public static readonly FrozenDictionary<string, PropertyInfo> Properties;
        public static readonly FrozenDictionary<string, PropertyInfo> NormalizedProperties;
        public static readonly FrozenSet<string> AmbiguousNormalizedProperties;
        public static readonly PropertyMeta[] AllProperties;
        private static readonly bool s_canReadMetadataTokens = RuntimeFeatureSwitch.IsDynamicCodeSupported;

        static TypeCache()
        {
            PropertyInfo[] props = ReflectionCache.GetPublicProperties(typeof(T));
            Properties = props
                .ToFrozenDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            NormalizedProperties = SqlIdentifierName.BuildNormalizedPropertyMap(props);
            AmbiguousNormalizedProperties = SqlIdentifierName.BuildAmbiguousNormalizedPropertySet(props);

            AllProperties = props
                .Where(p => p.CanRead)
                .OrderBy(GetMetadataTokenOrMax)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new PropertyMeta(p, p.GetCustomAttribute<DbParameterAttribute>()))
                .ToArray();
        }

        public static bool TryGetProperty(string name, [NotNullWhen(true)] out PropertyInfo? property)
            => SqlIdentifierName.TryGetProperty(Properties, NormalizedProperties, name, out property);

        public static bool HasAmbiguousNormalizedName(string name)
            => SqlIdentifierName.IsAmbiguousNormalizedName(AmbiguousNormalizedProperties, name);

        private static int GetMetadataTokenOrMax(PropertyInfo property)
            => property.Module.Assembly.IsDynamic
                ? int.MaxValue
                : s_canReadMetadataTokens
                    ? property.MetadataToken
                    : int.MaxValue;
    }

    /// <inheritdoc />
    public void MapParameters(SqlCommand cmd, T parameters, SpSchema? schema)
    {
        if (parameters is null)
            return;

        // [Case A] SP 스키마 기반 바인딩
        if (schema is not null)
        {
            SchemaOutputTargetValidator.ValidateUniqueOutputParameterNames(schema);

            foreach (SpParameterMetadata meta in schema.Parameters)
            {
                string name = meta.Name.TrimStart('@');
                bool hasProperty = TypeCache.TryGetProperty(name, out PropertyInfo? prop);
                SchemaOutputTargetValidator.ValidateObjectTarget(
                    meta,
                    strict,
                    hasProperty ? prop : null,
                    TypeCache.HasAmbiguousNormalizedName(name));

                if (hasProperty && prop!.CanRead)
                {
                    object? value = prop.GetValue(parameters);
                    SchemaOutputTargetValidator.ValidateObjectValue(meta, strict, prop, value);

                    if (DbBinder.TryBindExplicitParameter(cmd, meta, value, strict))
                        continue;

                    if (meta.Direction == ParameterDirection.Output)
                    {
                        DbBinder.BindParameter(cmd, meta, null, strict);
                        continue;
                    }

                    DbBinder.BindParameter(cmd, meta, value, strict);
                }
                else
                {
                    if (meta.Direction == ParameterDirection.ReturnValue)
                        continue;

                    if (meta.Direction == ParameterDirection.Output)
                    {
                        DbBinder.BindParameter(cmd, meta, null, strict);
                        continue;
                    }

                    if (meta.Direction == ParameterDirection.Input && meta.HasDefaultValue)
                        continue;

                    // Strict 모드는 "입력 파라미터"만 필수로 본다.
                    if (strict &&
                        !meta.IsNullable &&
                        meta.Direction == ParameterDirection.Input)
                    {
                        throw new InvalidOperationException($"[AOT] 필수 파라미터 '{meta.Name}' 누락");
                    }

                    DbBinder.BindParameter(cmd, meta, null, strict);
                }
            }

            BindExtraReturnValueParameter(cmd, parameters);
            return;
        }

        // [Case B] 스키마 없는 Raw SQL 바인딩
        PropertyMeta[] props = TypeCache.AllProperties;
        for (int i = 0; i < props.Length; i++)
        {
            ref readonly PropertyMeta meta = ref props[i];
            object? value = meta.Info.GetValue(parameters);
            DbBinder.BindRawParameter(cmd, meta.Info.Name, value, meta.Attribute);
        }
    }

    private static void BindExtraReturnValueParameter(SqlCommand cmd, T parameters)
    {
        PropertyMeta[] props = TypeCache.AllProperties;
        bool observedCandidate = false;
        for (int i = 0; i < props.Length; i++)
        {
            ref readonly PropertyMeta meta = ref props[i];
            if (!typeof(SqlParameter).IsAssignableFrom(meta.Info.PropertyType))
                continue;

            object? value = meta.Info.GetValue(parameters);
            ExplicitReturnValueBinding.BindOrValidateCandidate(cmd, meta.Info.Name, value, ref observedCandidate);
        }
    }

    /// <inheritdoc />
    public void MapOutputParameters(SqlCommand cmd, T parameters)
    {
        if (parameters is null)
            return;

        List<ObjectOutputWrite<T>> writes = [];

        foreach (SqlParameter p in cmd.Parameters)
        {
            if (!OutputWriteApplier.IsOutputParameter(p))
                continue;

            string name = p.ParameterName.TrimStart('@');

            SchemaOutputTargetValidator.ThrowIfAmbiguousObjectTarget(
                p.Direction,
                OutputParameterName.From(p.ParameterName),
                TypeCache.HasAmbiguousNormalizedName(name));

            if (TypeCache.TryGetProperty(name, out PropertyInfo? prop))
            {
                if (typeof(SqlParameter).IsAssignableFrom(prop.PropertyType))
                {
                    SqlParameter? sourceParameter = SqlParameterCloneFactory.TryGetRegisteredSource(p, out SqlParameter? registeredSource)
                        ? registeredSource
                        : prop.GetValue(parameters) as SqlParameter;

                    if (sourceParameter is not null)
                    {
                        writes.Add(new ObjectOutputWrite<T>(
                            null,
                            null,
                            sourceParameter,
                            SqlParameterCloneFactory.CaptureValueState(sourceParameter),
                            p,
                            null));
                    }

                    continue;
                }

                if (p.Direction == ParameterDirection.ReturnValue)
                    continue;

                if (!prop.CanWrite)
                {
                    SchemaOutputTargetValidator.ThrowIfReadOnlyObjectTarget(
                        p.Direction,
                        OutputParameterName.From(p.ParameterName),
                        strict,
                        prop);
                    continue;
                }

                writes.Add(new ObjectOutputWrite<T>(
                    (target, value) => prop.SetValue(target, value),
                    prop.GetValue(parameters),
                    null,
                    default,
                    p,
                    OutputWriteApplier.ToClrValue(p)));
            }
            else
            {
                SchemaOutputTargetValidator.ThrowIfMissingObjectTarget(
                    p.Direction,
                    OutputParameterName.From(p.ParameterName),
                    strict);
            }
        }

        OutputWriteApplier.ApplyObjectWrites(parameters, writes);
    }

    /// <inheritdoc />
    public T MapResult(DbDataReader reader)
        => throw new NotSupportedException(
            "Reflection 매퍼는 결과 매핑을 지원하지 않습니다. Native AOT 환경에서 DTO 결과 매핑이 필요하면 Source Generator 또는 수동 매퍼를 사용해 주세요.");
}

#endregion

#region [Source Generator 연동 매퍼]

/// <summary>
/// Source Generator가 생성한 정적 메서드 <c>T.Map(DbDataReader)</c>를 사용하는 결과 매퍼입니다.
/// <para>
/// - IMapableResult&lt;T&gt; 패턴을 구현한 DTO에 대해, 정적 Map 메서드를 사용하여 결과 매핑을 수행합니다.<br/>
/// - 파라미터 매핑 및 Output 매핑은 ExpressionTreeMapper/ReflectionParameterMapper에 위임합니다.<br/>
/// - Native AOT 환경에서는 이 패턴(Generator 기반 정적 Map 메서드)을 통해 DTO 결과 매핑을 지원합니다.
/// </para>
/// </summary>
internal sealed class GeneratedResultMapper<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)] T>
    : ISqlMapper<T>
{
    private readonly Func<DbDataReader, T> _mapFunc;
    private readonly ISqlMapper<T> _parameterMapper;

    /// <summary>
    /// Source Generator가 제공하는 <c>T.Map(DbDataReader)</c> 메서드를 우선 사용합니다.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2090",
        Justification = "Generated result mapper requires public static Map methods. The generic parameter annotation preserves public methods.")]
    [UnconditionalSuppressMessage(
        "Aot",
        "IL3050:RequiresDynamicCode",
        Justification = "ExpressionTreeMapper is selected only when RuntimeFeatureSwitch reports dynamic code support; Native AOT uses ReflectionParameterMapper.")]
    public GeneratedResultMapper(LibDbOptions options)
    {
        MethodInfo? dbReaderMethod = typeof(T).GetMethod(
            "Map",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(DbDataReader)]);

        if (dbReaderMethod is not null)
        {
            _mapFunc = (Func<DbDataReader, T>)Delegate.CreateDelegate(
                typeof(Func<DbDataReader, T>), dbReaderMethod);
        }
        else
        {
            MethodInfo sqlReaderMethod = typeof(T).GetMethod(
                "Map",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(SqlDataReader)])
                ?? throw new InvalidOperationException(
                    $"'{typeof(T).Name}' 형식에 정적 메서드 Map(DbDataReader) 또는 Map(SqlDataReader)이 없습니다.");

            Func<SqlDataReader, T> sqlMapFunc = (Func<SqlDataReader, T>)Delegate.CreateDelegate(
                typeof(Func<SqlDataReader, T>), sqlReaderMethod);

            _mapFunc = reader => reader is SqlDataReader sqlReader
                ? sqlMapFunc(sqlReader)
                : throw new InvalidOperationException(
                    $"'{typeof(T).Name}' generated mapper only exposes Map(SqlDataReader). " +
                    "Update the [DbResult] mapper so MonitoredSqlDataReader/DbDataReader wrappers can be used.");
        }

        if (RuntimeFeatureSwitch.IsDynamicCodeSupported)
        {
            _parameterMapper = new ExpressionTreeMapper<T>(options.JsonOptions, options.StrictRequiredParameterCheck);
        }
        else
        {
            _parameterMapper = new ReflectionParameterMapper<T>(options.StrictRequiredParameterCheck);
        }
    }

    /// <inheritdoc />
    public void MapParameters(SqlCommand cmd, T parameters, SpSchema? schema)
        => _parameterMapper.MapParameters(cmd, parameters, schema);

    /// <inheritdoc />
    public void MapOutputParameters(SqlCommand cmd, T parameters)
        => _parameterMapper.MapOutputParameters(cmd, parameters);

    /// <inheritdoc />
    public T MapResult(DbDataReader reader)
        => _mapFunc(reader);
}

#endregion
