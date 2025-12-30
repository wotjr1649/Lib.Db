// ============================================================================
// File : Lib.Db/Repository/Lib.Db.Infrastructure.cs
// Role : 인프라스트럭처 구현체 모음(연결 팩토리 + 스키마 리포지토리)
// Env  : .NET 10 / C# 14 (예정) + SqlClient
// Notes:
//   - DbConnectionFactory : 3-Tier 연결 문자열 해결(Ad-hoc > Raw: > Options)
//   - SqlSchemaRepository : sys.* 뷰 기반 메타데이터 조회(버전/파라미터/TVP/일괄)
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Infrastructure;

namespace Lib.Db.Repository;

#region [구현: DbConnectionFactory] - 연결 문자열 해결 및 SqlConnection 생성

/// <summary>
/// <see cref="IDbConnectionFactory"/>의 구현체로,
/// 인스턴스 식별자(해시/이름)를 입력받아 <see cref="SqlConnection"/>을 생성·오픈하여 반환합니다.
/// </summary>
/// <remarks>
/// <para><b>핵심 목표</b></para>
/// <list type="bullet">
///   <item><description><b>단일 진입점</b>: DB 연결 생성 정책을 한 곳에 모아 호출자 복잡도를 낮춥니다.</description></item>
///   <item><description><b>성능</b>: 최소한의 분기와 락 없는 조회(ConcurrentDictionary)를 사용합니다.</description></item>
///   <item><description><b>안전성</b>: 연결 오픈 실패 시 즉시 Dispose하여 리소스 누수를 차단합니다.</description></item>
/// </list>
///
/// <para><b>연결 문자열 해결(3-Tier) 우선순위</b></para>
/// <list type="number">
///   <item>
///     <description>
///     <b>Ad-hoc 등록</b>:
///     <see cref="RegisterAdHocInstance(string, string)"/>로 등록된 연결 문자열을 최우선으로 사용합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Raw 접두사</b>:
///     <paramref name="instanceHash"/>가 <c>"Raw:"</c>로 시작하면 뒤의 문자열을 연결 문자열로 간주합니다.
///     (레거시/테스트/임시 연결 호환)
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>설정 기반</b>:
///     <see cref="LibDbOptions.ConnectionStrings"/>에서 키로 조회합니다.
///     </description>
///   </item>
/// </list>
///
/// <para><b>예외 정책</b></para>
/// <list type="bullet">
///   <item><description>연결 문자열을 찾지 못한 경우 <see cref="ArgumentException"/>을 발생시킵니다.</description></item>
///   <item><description><see cref="SqlConnection.OpenAsync(CancellationToken)"/> 실패 시 예외를 래핑하지 않고 그대로 전파합니다(스택 트레이스 보존).</description></item>
/// </list>
/// </remarks>
internal sealed class DbConnectionFactory(LibDbOptions options) : IDbConnectionFactory
{
    #region [필드] Ad-hoc 연결 문자열 저장소(스레드 안전)

    /// <summary>
    /// Ad-hoc 인스턴스(동적 등록) 연결 문자열 저장소입니다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// - 키: 인스턴스 이름/해시(<paramref name="instanceHash"/>로 들어오는 값과 동일한 규칙 사용)<br/>
    /// - 값: 연결 문자열(Connection String)
    /// </para>
    /// <para>
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>를 사용하여
    /// 멀티 스레드 환경에서 락 없이 안전한 등록/조회/해제를 지원합니다.
    /// </para>
    /// </remarks>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _adhocConnections = new();

    #endregion

    #region [공개 API] 연결 생성

    /// <summary>
    /// 인스턴스 식별자(<paramref name="instanceHash"/>)를 기반으로 연결 문자열을 결정한 뒤,
    /// <see cref="SqlConnection"/>을 생성하고 <b>즉시 Open</b>하여 반환합니다.
    /// </summary>
    /// <param name="instanceHash">
    /// 연결 문자열을 찾기 위한 키입니다.
    /// <para>
    /// - Ad-hoc에 등록된 키일 수 있습니다.<br/>
    /// - 또는 <c>"Raw:"</c> 접두사로 시작하여 연결 문자열이 직접 포함될 수 있습니다.<br/>
    /// - 또는 <see cref="LibDbOptions.ConnectionStrings"/>의 키일 수 있습니다.
    /// </para>
    /// </param>
    /// <param name="ct">취소 토큰입니다.</param>
    /// <returns>Open된 <see cref="SqlConnection"/>을 반환합니다.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="instanceHash"/>에 해당하는 연결 문자열을 Ad-hoc/Raw/Options 어디에서도 찾지 못한 경우 발생합니다.
    /// </exception>
    /// <remarks>
    /// <para><b>중요</b></para>
    /// <para>
    /// 이 메서드는 연결을 <b>오픈(Open)</b>한 상태로 반환합니다.
    /// 호출자는 <c>await using</c> 또는 <c>Dispose/DisposeAsync</c>로 반드시 해제해야 합니다.
    /// </para>
    /// <para><b>리소스 누수 방지</b></para>
    /// <para>
    /// Open 중 예외가 발생하면 즉시 <see cref="SqlConnection.DisposeAsync"/>를 호출한 뒤 예외를 다시 던집니다.
    /// </para>
    /// </remarks>
    public async Task<SqlConnection> CreateConnectionAsync(string instanceHash, CancellationToken ct)
    {
        #region [1] 연결 문자열 결정(3-Tier)

        string connStr;

        // [1순위] Ad-hoc 연결(동적 등록)
        if (_adhocConnections.TryGetValue(instanceHash, out connStr!))
        {
            // Ad-hoc 등록된 연결 문자열을 그대로 사용합니다.
        }
        // [2순위] Raw: 접두사(레거시/테스트 호환)
        else if (instanceHash.StartsWith("Raw:", StringComparison.Ordinal))
        {
            connStr = instanceHash[4..]; // "Raw:" 제거 후 나머지를 연결 문자열로 간주
        }
        // [3순위] 옵션 기반(설정)
        else
        {
            if (!options.ConnectionStrings.TryGetValue(instanceHash, out connStr!))
            {
                throw new ArgumentException($"인스턴스 '{instanceHash}'에 대한 연결 문자열을 찾을 수 없습니다.");
            }
        }

        #endregion

        #region [2] SqlConnection 생성 및 Open(실패 시 즉시 Dispose)

        var conn = new SqlConnection(connStr);

        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Open 실패 시 커넥션 핸들/소켓 등 리소스 누수를 막기 위해 즉시 해제합니다.
            await conn.DisposeAsync().ConfigureAwait(false);
            throw; // 원본 예외(스택 트레이스) 보존
        }

        return conn;

        #endregion
    }

    #endregion

    #region [공개 API] Ad-hoc 인스턴스 등록/해제

    /// <summary>
    /// 런타임에 Ad-hoc 인스턴스 연결 문자열을 등록합니다.
    /// </summary>
    /// <param name="instanceName">등록 키(인스턴스 이름/해시)입니다.</param>
    /// <param name="connectionString">연결 문자열입니다.</param>
    /// <remarks>
    /// <para>
    /// 같은 <paramref name="instanceName"/>이 이미 존재하면 값을 덮어씁니다.
    /// </para>
    /// <para>
    /// 운영 환경에서는 외부 입력을 그대로 등록하는 경우 보안(비밀 값 노출/로그 기록) 정책을 반드시 적용하십시오.
    /// </para>
    /// </remarks>
    public void RegisterAdHocInstance(string instanceName, string connectionString)
    {
        _adhocConnections[instanceName] = connectionString;
    }

    /// <summary>
    /// Ad-hoc 인스턴스 연결 문자열 등록을 해제합니다.
    /// </summary>
    /// <param name="instanceName">해제할 키(인스턴스 이름/해시)입니다.</param>
    /// <remarks>
    /// <para>
    /// 대상 키가 존재하지 않으면 아무 작업도 하지 않습니다(멱등 동작).
    /// </para>
    /// </remarks>
    public void UnregisterAdHocInstance(string instanceName)
    {
        _adhocConnections.TryRemove(instanceName, out _);
    }

    #endregion
}

#endregion

#region [구현: SqlSchemaRepository] - SQL Server 시스템 뷰 기반 메타데이터 조회

/// <summary>
/// SQL Server 시스템 카탈로그(sys.*)를 조회하여
/// 저장 프로시저(SP) 및 TVP(Table-Valued Parameter) 스키마 메타데이터를 제공하는 <see cref="ISchemaRepository"/> 구현체입니다.
/// </summary>
/// <remarks>
/// <para><b>이 구현체가 제공하는 정보</b></para>
/// <list type="bullet">
///   <item><description><b>버전(Version)</b>: 객체 수정 시각(modify_date)을 기준으로 마이크로초 단위 epoch 값을 생성하여 반환합니다.</description></item>
///   <item><description><b>SP 파라미터</b>: 이름/타입/길이/정밀도/스케일/OUT 여부/NULL 허용/기본값 존재 여부/UDT명 등</description></item>
///   <item><description><b>TVP 컬럼</b>: 이름/타입/순서/길이/정밀도/스케일/Identity/Computed/NULL 허용 등</description></item>
/// </list>
///
/// <para><b>성능/동작 정책</b></para>
/// <list type="bullet">
///   <item><description><b>DataReader 직접 매핑</b>: DataTable/Adapter를 사용하지 않고 즉시 객체로 매핑합니다.</description></item>
///   <item><description><b>MARS</b>: 하나의 커넥션에서 다중 ResultSet을 처리하는 시나리오를 가정합니다(연결 문자열 옵션에 의해 동작).</description></item>
///   <item><description><b>READ UNCOMMITTED</b>: 스키마 조회는 Dirty Read를 허용하여 락 경합을 줄입니다.</description></item>
/// </list>
///
/// <para><b>주의</b></para>
/// <para>
/// MARS(다중 활성 결과 집합)는 연결 문자열 옵션(<c>MultipleActiveResultSets=True</c>)에 의해 활성화됩니다.
/// 본 구현은 “가능하면 활용”하는 형태이며, 환경에 따라 단일 ResultSet만으로도 동작합니다.
/// </para>
/// </remarks>
internal sealed class SqlSchemaRepository(IDbConnectionFactory connFactory) : ISchemaRepository
{
    #region [상수 SQL] 단일 조회(버전)

    /// <summary>
    /// SP/함수/뷰 등 sys.objects 기반 객체의 수정 시각을 epoch 마이크로초 값으로 변환하여 조회하는 SQL입니다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 반환 값이 <c>NULL</c>이면(대상이 없으면) 호출부에서 0으로 처리합니다.
    /// </para>
    /// </remarks>
    private const string SqlGetVersion = """
        SELECT CAST(DATEDIFF_BIG(microsecond, '1970-01-01', o.modify_date) AS bigint)
        FROM sys.objects o 
        JOIN sys.schemas s ON o.schema_id = s.schema_id
        WHERE s.name + '.' + o.name = @Name
        """;

    /// <summary>
    /// TVP(sys.table_types) 객체의 수정 시각을 epoch 마이크로초 값으로 변환하여 조회하는 SQL입니다.
    /// </summary>
    private const string SqlGetTvpVersion = """
        SELECT CAST(DATEDIFF_BIG(microsecond, '1970-01-01', o.modify_date) AS bigint)
        FROM sys.table_types tt 
        JOIN sys.schemas s ON tt.schema_id = s.schema_id
        JOIN sys.objects o ON tt.type_table_object_id = o.object_id
        WHERE s.name + '.' + tt.name = @Name
        """;

    #endregion

    #region [버전 조회] 객체/TVP

    /// <summary>
    /// 지정된 객체(주로 SP)의 “버전 값”을 조회합니다.
    /// </summary>
    /// <param name="name">스키마 포함 2부 이름(예: <c>dbo.usp_Test</c>)</param>
    /// <param name="instanceHash">DB 인스턴스 식별자(연결 문자열 키)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>버전 값(epoch 마이크로초). 대상이 없으면 0을 반환합니다.</returns>
    /// <remarks>
    /// <para>
    /// 버전은 스키마 캐싱/무효화 판단에 사용됩니다.
    /// </para>
    /// </remarks>
    public async Task<long> GetObjectVersionAsync(string name, string instanceHash, CancellationToken ct)
    {
        await using var conn = await connFactory.CreateConnectionAsync(instanceHash, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(SqlGetVersion, conn);
        cmd.Parameters.AddWithValue("@Name", name);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is long val ? val : 0;
    }

    /// <summary>
    /// 지정된 TVP 객체의 “버전 값”을 조회합니다.
    /// </summary>
    /// <param name="name">스키마 포함 2부 이름(예: <c>dbo.UserTvpType</c>)</param>
    /// <param name="instanceHash">DB 인스턴스 식별자(연결 문자열 키)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>버전 값(epoch 마이크로초). 대상이 없으면 0을 반환합니다.</returns>
    public async Task<long> GetTvpVersionAsync(string name, string instanceHash, CancellationToken ct)
    {
        await using var conn = await connFactory.CreateConnectionAsync(instanceHash, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(SqlGetTvpVersion, conn);
        cmd.Parameters.AddWithValue("@Name", name);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is long val ? val : 0;
    }

    #endregion

    #region [메타데이터 조회] SP(버전 + 파라미터)

    /// <summary>
    /// 저장 프로시저(SP)의 버전과 파라미터 목록을 조회하여 <see cref="SpMetadata"/>로 반환합니다.
    /// </summary>
    /// <param name="name">스키마 포함 2부 이름(예: <c>dbo.usp_OrderPlace</c>)</param>
    /// <param name="instanceHash">DB 인스턴스 식별자(연결 문자열 키)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>버전과 파라미터 목록을 포함한 <see cref="SpMetadata"/>를 반환합니다.</returns>
    /// <remarks>
    /// <para><b>다중 ResultSet(MARS) 활용</b></para>
    /// <para>
    /// 한 번의 명령 실행으로
    /// (1) 버전, (2) 파라미터 목록을 연속 ResultSet으로 읽습니다.
    /// </para>
    /// <para><b>READ UNCOMMITTED</b></para>
    /// <para>
    /// 스키마 조회는 일반적으로 강한 일관성이 필요하지 않으므로
    /// 트랜잭션 격리를 낮춰 락 경합을 줄입니다.
    /// </para>
    /// </remarks>
    public async Task<SpMetadata> GetSpMetadataAsync(string name, string instanceHash, CancellationToken ct)
    {
        // MARS를 활용해 버전과 파라미터를 한 번에 조회 (기존 로직 100% 통합)
        const string sql = """
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            
            -- 1. Version
            SELECT CAST(DATEDIFF_BIG(microsecond, '1970-01-01', o.modify_date) AS bigint)
            FROM sys.objects o JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE s.name + '.' + o.name = @Name;

            -- 2. Parameters
            SELECT 
                p.name,
                CASE WHEN t_user.is_table_type = 1 THEN 'Structured' ELSE t_sys.name END,
                p.max_length, p.precision, p.scale, p.is_output, p.is_nullable, p.has_default_value,
                CASE WHEN t_user.is_table_type = 1 THEN SCHEMA_NAME(t_user.schema_id) + '.' + t_user.name ELSE TYPE_NAME(p.user_type_id) END
            FROM sys.parameters p
            JOIN sys.objects o ON p.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            JOIN sys.types t_user ON p.user_type_id = t_user.user_type_id 
            LEFT JOIN sys.types t_sys ON p.system_type_id = t_sys.system_type_id AND t_sys.user_type_id = t_sys.system_type_id
            WHERE s.name + '.' + o.name = @Name
            ORDER BY p.parameter_id;
            """;

        await using var conn = await connFactory.CreateConnectionAsync(instanceHash, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Name", name);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        #region [1] Version ResultSet

        long version = 0;
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            version = reader.GetInt64(0);
        }

        await reader.NextResultAsync(ct).ConfigureAwait(false);

        #endregion

        #region [2] Parameters ResultSet

        var parameters = new List<SpParameterInfo>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            parameters.Add(new SpParameterInfo(
                Name: reader.GetString(0),
                TypeName: reader.GetString(1),
                MaxLength: reader.GetInt16(2), // smallint -> short
                Precision: reader.GetByte(3),
                Scale: reader.GetByte(4),
                IsOutput: reader.GetBoolean(5),
                IsNullable: reader.GetBoolean(6),
                HasDefault: reader.GetBoolean(7),
                UdtName: reader.IsDBNull(8) ? null : reader.GetString(8)
            ));
        }

        #endregion

        return new SpMetadata(version, parameters);
    }

    #endregion

    #region [메타데이터 조회] TVP(버전 + 컬럼)

    /// <summary>
    /// TVP(Table Type)의 버전과 컬럼 목록을 조회하여 <see cref="TvpMetadata"/>로 반환합니다.
    /// </summary>
    /// <param name="name">스키마 포함 2부 이름(예: <c>dbo.OrderItemTvpType</c>)</param>
    /// <param name="instanceHash">DB 인스턴스 식별자(연결 문자열 키)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>버전과 컬럼 목록을 포함한 <see cref="TvpMetadata"/>를 반환합니다.</returns>
    /// <remarks>
    /// <para>
    /// TVP는 <see cref="sys.table_types"/>와 해당 타입의 내부 테이블 객체(<see cref="sys.objects"/>)를 조인하여 수정 시각을 산출합니다.
    /// </para>
    /// </remarks>
    public async Task<TvpMetadata> GetTvpMetadataAsync(string name, string instanceHash, CancellationToken ct)
    {
        const string sql = """
            -- 1. Version
            SELECT CAST(DATEDIFF_BIG(microsecond, '1970-01-01', o.modify_date) AS bigint)
            FROM sys.table_types tt 
            JOIN sys.schemas s ON tt.schema_id = s.schema_id
            JOIN sys.objects o ON tt.type_table_object_id = o.object_id
            WHERE s.name + '.' + tt.name = @Name;

            -- 2. Columns
            SELECT 
                c.name, t_sys.name, c.column_id, c.max_length, c.precision, c.scale, c.is_identity, c.is_computed, c.is_nullable
            FROM sys.columns c
            JOIN sys.table_types tt ON c.object_id = tt.type_table_object_id
            JOIN sys.schemas s ON tt.schema_id = s.schema_id
            JOIN sys.types t_sys ON c.system_type_id = t_sys.system_type_id AND t_sys.user_type_id = t_sys.system_type_id
            WHERE s.name + '.' + tt.name = @Name
            ORDER BY c.column_id;
            """;

        await using var conn = await connFactory.CreateConnectionAsync(instanceHash, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Name", name);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        #region [1] Version ResultSet

        long version = 0;
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            version = reader.GetInt64(0);

        await reader.NextResultAsync(ct).ConfigureAwait(false);

        #endregion

        #region [2] Columns ResultSet

        var columns = new List<TvpColumnInfo>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            columns.Add(new TvpColumnInfo(
                Name: reader.GetString(0),
                TypeName: reader.GetString(1),
                Ordinal: reader.GetInt32(2),
                MaxLength: reader.GetInt16(3),
                Precision: reader.GetByte(4),
                Scale: reader.GetByte(5),
                IsIdentity: reader.GetBoolean(6),
                IsComputed: reader.GetBoolean(7),
                IsNullable: reader.GetBoolean(8)
            ));
        }

        #endregion

        return new TvpMetadata(version, columns);
    }

    #endregion

    #region [일괄 조회] 스키마 전체 메타데이터(필터 포함)

    /// <summary>
    /// 지정한 스키마(<paramref name="schemaName"/>)에 대해
    /// SP/TVP 목록과 각 SP의 파라미터, 각 TVP의 컬럼을 <b>한 번의 왕복</b>으로 일괄 조회합니다.
    /// </summary>
    /// <param name="schemaName">스키마 이름(예: <c>dbo</c>)</param>
    /// <param name="instanceHash">DB 인스턴스 식별자(연결 문자열 키)</param>
    /// <param name="includePatterns">
    /// 포함 패턴 목록입니다. (예: <c>*Order*</c>, <c>usp_*</c>)
    /// <para>패턴은 SQL LIKE로 변환되어 적용됩니다. (<c>*</c> → <c>%</c>, <c>?</c> → <c>_</c>)</para>
    /// </param>
    /// <param name="excludePatterns">
    /// 제외 패턴 목록입니다. 포함 조건 이후에 NOT 필터로 적용됩니다.
    /// </param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>
    /// 버전/파라미터/컬럼을 모두 담은 <see cref="SchemaBulkData"/>를 반환합니다.
    /// </returns>
    /// <remarks>
    /// <para><b>보안</b></para>
    /// <para>
    /// 동적 SQL처럼 보이지만, 패턴 값은 모두 파라미터로 바인딩되어 SQL Injection을 방지합니다.
    /// </para>
    /// <para><b>결과 구성</b></para>
    /// <list type="number">
    ///   <item><description>SP 목록 + 버전</description></item>
    ///   <item><description>TVP 목록 + 버전</description></item>
    ///   <item><description>SP 파라미터(전체) - SP 이름으로 그룹핑하여 딕셔너리에 적재</description></item>
    ///   <item><description>TVP 컬럼(전체) - TVP 이름으로 그룹핑하여 딕셔너리에 적재</description></item>
    /// </list>
    /// </remarks>
    public async Task<SchemaBulkData> GetAllSchemaMetadataAsync(
        string schemaName,
        string instanceHash,
        List<string> includePatterns,
        List<string> excludePatterns,
        CancellationToken ct)
    {
        #region [로컬 유틸] 패턴 처리(사용자 패턴 -> SQL LIKE)

        /// <summary>
        /// 사용자가 입력한 와일드카드 패턴을 SQL LIKE 패턴으로 변환합니다.
        /// </summary>
        static string ConvertToSqlPattern(string userPattern)
            => userPattern.Replace("*", "%").Replace("?", "_");

        /// <summary>
        /// 포함 필터(OR) 절을 생성합니다. 패턴이 없으면 빈 문자열을 반환합니다.
        /// </summary>
        static string BuildFilterClause(List<string> patterns, string objectAlias)
        {
            if (patterns == null || patterns.Count == 0)
                return string.Empty;

            var conditions = new List<string>(patterns.Count);
            for (int i = 0; i < patterns.Count; i++)
                conditions.Add($"{objectAlias}.name LIKE @p{i}");

            return $"AND ({string.Join(" OR ", conditions)})";
        }

        /// <summary>
        /// 포함 패턴 파라미터를 바인딩합니다.
        /// </summary>
        static void BindPatternParameters(SqlCommand cmd, List<string> patterns)
        {
            for (int i = 0; i < patterns.Count; i++)
            {
                string sqlPattern = ConvertToSqlPattern(patterns[i]);
                cmd.Parameters.AddWithValue($"@p{i}", sqlPattern);
            }
        }

        /// <summary>
        /// 제외 필터(NOT (OR ...)) 절을 생성합니다. 패턴이 없으면 빈 문자열을 반환합니다.
        /// </summary>
        static string BuildExcludeClause(List<string> patterns, string objectAlias)
        {
            if (patterns == null || patterns.Count == 0)
                return string.Empty;

            var conditions = new List<string>(patterns.Count);
            for (int i = 0; i < patterns.Count; i++)
                conditions.Add($"{objectAlias}.name LIKE @e{i}");

            return $"AND NOT ({string.Join(" OR ", conditions)})";
        }

        /// <summary>
        /// 제외 패턴 파라미터를 바인딩합니다.
        /// </summary>
        static void BindExcludeParameters(SqlCommand cmd, List<string> patterns)
        {
            for (int i = 0; i < patterns.Count; i++)
            {
                string sqlPattern = ConvertToSqlPattern(patterns[i]);
                cmd.Parameters.AddWithValue($"@e{i}", sqlPattern);
            }
        }

        #endregion

        #region [1] 동적 WHERE 절 구성(포함/제외)

        string spInclude = BuildFilterClause(includePatterns, "o");
        string tvpInclude = BuildFilterClause(includePatterns, "tt");

        string spExclude = BuildExcludeClause(excludePatterns, "o");
        string tvpExclude = BuildExcludeClause(excludePatterns, "tt");

        #endregion

        #region [2] 일괄 조회 SQL 구성(4 ResultSets)

        string sql = $"""
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

            -- 1. SP List
            SELECT o.name, CAST(DATEDIFF_BIG(microsecond, '1970-01-01', o.modify_date) AS bigint)
            FROM sys.objects o JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE s.name = @Schema AND o.type = 'P' {spInclude} {spExclude};

            -- 2. TVP List
            SELECT tt.name, CAST(DATEDIFF_BIG(microsecond, '1970-01-01', o.modify_date) AS bigint)
            FROM sys.table_types tt 
            JOIN sys.schemas s ON tt.schema_id = s.schema_id
            JOIN sys.objects o ON tt.type_table_object_id = o.object_id
            WHERE s.name = @Schema {tvpInclude} {tvpExclude};

            -- 3. SP Params (전체)
            SELECT 
                o.name, p.name,
                CASE WHEN t_user.is_table_type = 1 THEN 'Structured' ELSE t_sys.name END,
                p.max_length, p.precision, p.scale, p.is_output, p.is_nullable, p.has_default_value,
                CASE WHEN t_user.is_table_type = 1 THEN SCHEMA_NAME(t_user.schema_id) + '.' + t_user.name ELSE TYPE_NAME(p.user_type_id) END
            FROM sys.parameters p
            JOIN sys.objects o ON p.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            JOIN sys.types t_user ON p.user_type_id = t_user.user_type_id 
            LEFT JOIN sys.types t_sys ON p.system_type_id = t_sys.system_type_id AND t_sys.user_type_id = t_sys.system_type_id
            WHERE s.name = @Schema AND o.type = 'P' {spInclude} {spExclude}
            ORDER BY o.name, p.parameter_id;

            -- 4. TVP Columns (전체)
            SELECT 
                tt.name, c.name, t_sys.name, c.column_id, c.max_length, c.precision, c.scale, c.is_identity, c.is_computed, c.is_nullable
            FROM sys.columns c
            JOIN sys.table_types tt ON c.object_id = tt.type_table_object_id
            JOIN sys.schemas s ON tt.schema_id = s.schema_id
            JOIN sys.types t_sys ON c.system_type_id = t_sys.system_type_id AND t_sys.user_type_id = t_sys.system_type_id
            WHERE s.name = @Schema {tvpInclude} {tvpExclude}
            ORDER BY tt.name, c.column_id;
            """;

        #endregion

        #region [3] 실행 및 파라미터 바인딩

        await using var conn = await connFactory.CreateConnectionAsync(instanceHash, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Schema", schemaName);

        if (includePatterns != null && includePatterns.Count > 0)
            BindPatternParameters(cmd, includePatterns);

        if (excludePatterns != null && excludePatterns.Count > 0)
            BindExcludeParameters(cmd, excludePatterns);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        #endregion

        #region [4] ResultSets 적재(버전/파라미터/컬럼)

        var result = new SchemaBulkData();

        // 1) SP Versions
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.SpVersions[reader.GetString(0)] = reader.GetInt64(1);

        await reader.NextResultAsync(ct).ConfigureAwait(false);

        // 2) TVP Versions
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.TvpVersions[reader.GetString(0)] = reader.GetInt64(1);

        await reader.NextResultAsync(ct).ConfigureAwait(false);

        // 3) SP Parameters (그룹핑)
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            string spName = reader.GetString(0);

            if (!result.SpParameters.TryGetValue(spName, out var list))
            {
                list = []; // C# 12 컬렉션 식
                result.SpParameters[spName] = list;
            }

            list.Add(new SpParameterInfo(
                Name: reader.GetString(1),
                TypeName: reader.GetString(2),
                MaxLength: reader.GetInt16(3),
                Precision: reader.GetByte(4),
                Scale: reader.GetByte(5),
                IsOutput: reader.GetBoolean(6),
                IsNullable: reader.GetBoolean(7),
                HasDefault: reader.GetBoolean(8),
                UdtName: reader.IsDBNull(9) ? null : reader.GetString(9)
            ));
        }

        await reader.NextResultAsync(ct).ConfigureAwait(false);

        // 4) TVP Columns (그룹핑)
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            string tvpName = reader.GetString(0);

            if (!result.TvpColumns.TryGetValue(tvpName, out var list))
            {
                list = [];
                result.TvpColumns[tvpName] = list;
            }

            list.Add(new TvpColumnInfo(
                Name: reader.GetString(1),
                TypeName: reader.GetString(2),
                Ordinal: reader.GetInt32(3),
                MaxLength: reader.GetInt16(4),
                Precision: reader.GetByte(5),
                Scale: reader.GetByte(6),
                IsIdentity: reader.GetBoolean(7),
                IsComputed: reader.GetBoolean(8),
                IsNullable: reader.GetBoolean(9)
            ));
        }

        return result;

        #endregion
    }

    #endregion
}

#endregion
