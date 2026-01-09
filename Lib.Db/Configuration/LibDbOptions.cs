// ============================================================================
// 파일: Lib.Db/Configuration/LibDbOptions.cs
// 설명: Lib.Db 라이브러리 전역 설정 및 동작 제어 옵션
// 대상: .NET 10 / C# 14
// 역할:
//   - Connection String 및 Schema Cache 정책 관리
//   - Resilience(Polly), Observability(OTel), AOT 호환성 설정 중앙화
//   - 유효성 검사(Validation) 및 기본값(Defaults) 제공
// ============================================================================

#nullable enable

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lib.Db.Configuration;

/// <summary>
/// Lib.Db 라이브러리의 동작을 제어하는 전역 설정 클래스입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 이 클래스는 시스템의 물리적 제약(Timeout, Memory)과 논리적 정책(Caching, Resilience)을 정의하는
/// <b>'정책의 원천(Source of Policy)'</b>입니다. 설정의 파편화를 막고, 중앙에서 일관된 유효성 검증(Validation)과 기본값(Defaults)을 제공하여
/// 사용자가 시스템 동작을 예측 가능하게 제어할 수 있도록 설계되었습니다.
/// </para>
/// <para>
/// <b>[주요 특징]</b>
/// <list type="bullet">
/// <item><b>Hot Reload 지원</b>: <see cref="IOptionsMonitor{TOptions}"/>와 연동하여 런타임 설정 변경을 반영하도록 설계되었습니다.</item>
/// <item><b>Self-Validation</b>: C# 14 <c>field</c> 키워드를 활용하여 잘못된 설정값이 주입되는 것을 원천적으로 방지합니다.</item>
/// <item><b>AOT Compatibility</b>: <see cref="JsonIgnoreAttribute"/>를 적절히 배치하여 트리밍 경고를 방지하고 Native AOT 빌드를 지원합니다.</item>
/// </list>
/// </para>
/// </summary>
public class LibDbOptions
{
    #region [1] 연결 및 인프라 설정

    /// <summary>
    /// DB 연결 문자열 목록 (Key: 인스턴스/별칭, Value: 실제 연결 문자열)
    /// </summary>
    [Required(ErrorMessage = "연결 문자열 딕셔너리는 null일 수 없습니다.")]
    public Dictionary<string, string> ConnectionStrings
    {
        get;
        set
        {
            field = value ?? throw new ArgumentNullException(nameof(value), "연결 문자열 딕셔너리는 null일 수 없습니다.");
        }
    } = [];

    #endregion

    #region [2] 연결 별칭 (Smart Pointer)

    /// <summary>
    /// 기본(Default)으로 사용할 연결 문자열의 키(별칭)입니다. (기본값: "Default")
    /// <para>
    /// <b>[설계의도]</b><br/>
    /// 다중 DB 환경에서 소스 코드 수정 없이 설정 변경만으로 주(Primary) 데이터베이스를 스위칭할 수 있도록 돕는 '스마트 포인터'입니다.<br/>
    /// <see cref="ConnectionStrings"/> 딕셔너리 내의 키를 가리킵니다.
    /// </para>
    /// </summary>
    public string ConnectionStringName { get; set; } = "Default";

    #endregion

    #region [3] 스키마 캐싱 및 워밍업

    /// <summary>
    /// 스키마 캐싱 기능 사용 여부 (기본값: true)
    /// <para>false 설정 시 매 쿼리마다 메타데이터를 조회하므로 성능이 저하될 수 있습니다.</para>
    /// </summary>
    public bool EnableSchemaCaching { get; set; } = true;

    /// <summary>
    /// 스키마 변경 사항 감지 주기 (초 단위, 기본값: 60초)
    /// </summary>
    [Range(1, 86400, ErrorMessage = "스키마 갱신 주기는 최소 1초 이상, 최대 1일(86400초) 이내여야 합니다.")]
    public int SchemaRefreshIntervalSeconds
    {
        get;
        set
        {
            if (value is < 1 or > 86400)
                throw new ArgumentOutOfRangeException(nameof(value), value, "스키마 갱신 주기는 1초 이상, 86400초(1일) 이내여야 합니다.");
            field = value;
        }
    } = 60;

    /// <summary>
    /// 스키마 변경을 감시할 대상 인스턴스(ConnectionString Key) 목록
    /// <para>비어있으면 모든 등록된 연결을 감시합니다.</para>
    /// </summary>
    public List<string> WatchedInstances { get; set; } = [];

    /// <summary>
    /// 앱 시작(Warmup) 시 미리 메타데이터를 로드할 스키마 목록 (기본값: ["dbo"])
    /// </summary>
    public List<string> PrewarmSchemas
    {
        get;
        set
        {
            field = value ?? throw new ArgumentNullException(nameof(value), "프리웜 스키마 목록은 null일 수 없습니다.");
        }
    } = ["dbo"];

    /// <summary>
    /// 워밍업 시 포함할 객체 이름 패턴 목록 (사용자 친화적 와일드카드 문법)
    /// <para>
    /// <b>[선택적 캐싱]</b><br/>
    /// 이 목록이 비어있으면 PrewarmSchemas의 모든 객체를 로드합니다 (기존 동작).<br/>
    /// 패턴이 지정되면 해당 패턴에 매칭되는 SP/TVP만 로드합니다.
    /// </para>
    /// <para>
    /// <b>[와일드카드 문법]</b><br/>
    /// <c>*</c> (별표): 0개 이상의 임의 문자와 매칭<br/>
    /// <c>?</c> (물음표): 정확히 1개의 임의 문자와 매칭
    /// </para>
    /// <para>
    /// <b>[패턴 예시]</b><br/>
    /// - <c>"usp_User*"</c>: usp_User로 시작하는 모든 SP<br/>
    /// - <c>"*_Order*"</c>: _Order를 포함하는 모든 객체<br/>
    /// - <c>"*usp_*User*_date*"</c>: 복잡한 매칭 (usp_ 포함 + User 포함 + _date 포함)<br/>
    /// - <c>"Tvp_Common*"</c>: Tvp_Common으로 시작<br/>
    /// - <c>"*usp_Auth?"</c>: usp_Auth + 1글자 (예: usp_AuthN, usp_AuthZ)
    /// </para>
    /// <para>
    /// <b>[내부 변환]</b><br/>
    /// 사용자 패턴은 내부적으로 SQL LIKE 문법으로 자동 변환됩니다:<br/>
    /// - <c>*</c> → <c>%</c> (SQL LIKE 와일드카드)<br/>
    /// - <c>?</c> → <c>_</c> (SQL LIKE 단일 문자)<br/>
    /// 예: <c>"*usp_User*"</c> → <c>"%usp_User%"</c>
    /// </para>
    /// </summary>
    public List<string> PrewarmIncludePatterns
    {
        get;
        set
        {
            field = value ?? throw new ArgumentNullException(nameof(value), "워밍업 패턴 목록은 null일 수 없습니다.");
        }
    } = [];

    /// <summary>
    /// 워밍업 시 제외할 객체 이름 패턴 목록 (* 와일드카드 문법)
    /// <para>
    /// <b>[우선순위 규칙]</b><br/>
    /// 1. Include와 Exclude가 모두 비어있으면 → 모든 객체 로드<br/>
    /// 2. Include만 → Include 매칭만 로드<br/>
    /// 3. Exclude만 → Exclude 제외한 모든 것 로드<br/>
    /// 4. 둘 다 → Include이면서 Exclude 아닌 것만 로드
    /// </para>
    /// <para>
    /// <b>[패턴 예시]</b>: <c>"*_Test*"</c>, <c>"*_Legacy*"</c>, <c>"usp_Internal*"</c>
    /// </para>
    /// </summary>
    public List<string> PrewarmExcludePatterns
    {
        get;
        set
        {
            field = value ?? throw new ArgumentNullException(nameof(value), "제외 패턴 목록은 null일 수 없습니다.");
        }
    } = [];

    #endregion

    #region [4] 쿼리 실행 정책

    /// <summary>
    /// Dry Run(모의 실행) 모드 활성화 여부 (기본값: false)
    /// <para>true 설정 시: DB 연결은 수행하지만, INSERT/UPDATE/DELETE 등의 변경 쿼리는 실제 실행하지 않고 로그만 남깁니다.</para>
    /// </summary>
    public bool EnableDryRun { get; set; } = false;

    /// <summary>
    /// SP 파라미터의 Required/Default 정책을 엄격하게 검사할지 여부 (기본값: true)
    /// <para>true: SP에 정의된 필수 파라미터가 누락되면 호출 전 클라이언트 레벨에서 차단합니다.</para>
    /// </summary>
    public bool StrictRequiredParameterCheck { get; set; } = true;

    #endregion

    #region [5] 데이터 직렬화 및 검증


    /// <summary>
    /// JSON 컬럼 자동 매핑 시 사용할 직렬화 옵션
    /// <para>null일 경우 JsonSerializerDefaults.Web 옵션이 내부적으로 사용됩니다.</para>
    /// </summary>
    /// <remarks>
    /// <b>AOT 호환성:</b><br/>
    /// <see cref="JsonIgnoreAttribute"/>가 적용되어 IConfiguration 바인딩에서 제외됩니다.<br/>
    /// 이 속성을 설정하려면 코드에서 직접 할당하거나, PostConfigure를 사용하세요.
    /// </remarks>
    [JsonIgnore] // AOT: Configuration 바인딩에서 제외하여 SYSLIB1100/1101 경고 방지
    public JsonSerializerOptions? JsonOptions { get; set; }

    /// <summary>
    /// TVP 스키마 검증 모드 설정 (기본값: Strict)
    /// </summary>
    public TvpValidationMode TvpValidationMode { get; set; } = TvpValidationMode.Strict;

    /// <summary>
    /// Source Generator 기반의 Fast TVP Binder 사용 여부 (기본값: true)
    /// <para>
    /// <c>true</c>: SG가 등록한 <c>TvpFactoryRegistry</c>를 통해 Reflection 없이 TVP를 바인딩합니다.<br/>
    /// <c>false</c>: 기존 Reflection 기반 바인딩을 강제합니다. (SG 문제 발생 시 폴백용)
    /// </para>
    /// </summary>
    public bool EnableGeneratedTvpBinder { get; set; } = true;

    #endregion

    #region [6] 타임아웃 및 성능 튜닝


    /// <summary>
    /// 일반 쿼리(Command) 기본 타임아웃 (초 단위, 기본값: 30)
    /// </summary>
    [Range(1, 600, ErrorMessage = "기본 타임아웃은 1초 이상 600초 이내여야 합니다.")]
    public int DefaultCommandTimeoutSeconds
    {
        get;
        set
        {
            if (value is < 1 or > 600)
                throw new ArgumentOutOfRangeException(nameof(value), value, "기본 타임아웃은 1초 이상 600초 이내여야 합니다.");
            field = value;
        }
    } = 30;

    /// <summary>
    /// 대량 데이터 작업(Bulk Insert) 타임아웃 (초 단위, 기본값: 600)
    /// </summary>
    [Range(1, 3600, ErrorMessage = "대량 작업 타임아웃은 1초 이상 3600초(1시간) 이내여야 합니다.")]
    public int BulkCommandTimeoutSeconds
    {
        get;
        set
        {
            if (value is < 1 or > 3600)
                throw new ArgumentOutOfRangeException(nameof(value), value, "대량 작업 타임아웃은 1초 이상 3600초(1시간) 이내여야 합니다.");
            field = value;
        }
    } = 600;

    /// <summary>
    /// Bulk Insert/Update 시 한 번에 전송할 배치 사이즈 (기본값: 5000)
    /// </summary>
    [Range(100, 100_000, ErrorMessage = "배치 사이즈는 100건 이상 100,000건 이내여야 합니다.")]
    public int BulkBatchSize
    {
        get;
        set
        {
            if (value is < 100 or > 100_000)
                throw new ArgumentOutOfRangeException(nameof(value), value, "배치 사이즈는 100건 이상 100,000건 이내여야 합니다.");
            field = value;
        }
    } = 5000;

    #endregion

    #region [7] 리소스 관리 및 메모리


    /// <summary>
    /// TVP 생성 시 메모리 사용량 경고 임계값 (Bytes 단위, 기본값: 10MB)
    /// <para>이 값을 초과하는 TVP 데이터 생성 시 경고 로그가 기록됩니다.</para>
    /// </summary>
    public long TvpMemoryWarningThresholdBytes
    {
        get;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "TVP 메모리 경고 임계값은 0보다 커야 합니다.");
            field = value;
        }
    } = 10 * 1024 * 1024;

    /// <summary>
    /// Resumable(재개 가능) 쿼리 최대 재시도 횟수 (기본값: 5)
    /// <para>일시적인 네트워크 오류나 데드락 발생 시 재시도할 최대 횟수입니다.</para>
    /// </summary>
    [Range(0, 20, ErrorMessage = "재시도 횟수는 0회 이상 20회 이내여야 합니다.")]
    public int ResumableQueryMaxRetries
    {
        get;
        set
        {
            if (value is < 0 or > 20)
                throw new ArgumentOutOfRangeException(nameof(value), value, "재시도 횟수는 0회 이상 20회 이내여야 합니다.");
            field = value;
        }
    } = 5;

    /// <summary>
    /// Resumable 쿼리 재시도 간 기본 지연 시간 (ms, 기본값: 100ms)
    /// <para>Exponential Backoff(지수 백오프) 알고리즘의 초기값으로 사용됩니다.</para>
    /// </summary>
    public int ResumableQueryBaseDelayMs
    {
        get;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "재시도 지연 시간은 0ms 이상이어야 합니다.");
            field = value;
        }
    } = 100;

    /// <summary>
    /// Resumable 쿼리 재시도 간 최대 대기 시간 (ms, 기본값: 5000ms)
    /// </summary>
    public int ResumableQueryMaxDelayMs
    {
        get;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "재시도 최대 대기 시간은 0ms 이상이어야 합니다.");
            field = value;
        }
    } = 5000;

    #endregion

    #region [8] 재시도 및 복구 (Resumable Query)


    /// <summary>
    /// Polly 기반 회복 탄력성 기능 활성화 여부 (기본값: false)
    /// </summary>
    public bool EnableResilience { get; set; } = false;

    /// <summary>
    /// 회복 탄력성 세부 설정
    /// <para>
    /// <b>[중요]</b> EnableResilience가 true일 때만 사용됩니다.
    /// </para>
    /// <para>
    /// <b>[v2.0 Breaking Change]</b><br/>
    /// 기존 루트 레벨 속성이 제거되었습니다. 아래 경로로 변경하세요:<br/>
    /// - CircuitBreakerFailureRatio → Resilience.CircuitBreakerFailureRatio<br/>
    /// - CircuitBreakerDurationSeconds → Resilience.CircuitBreakerBreakDurationMs (단위 변경: 초 → ms)<br/>
    /// - RetryMaxAttempts → Resilience.MaxRetryCount
    /// </para>
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// 재시도 백오프(지연 증가) 방식을 정의합니다.
    /// </summary>
    public enum RetryBackoffType
    {
        /// <summary>지수 백오프 (Exponential): 재시도마다 지연 시간이 지수적으로 증가 (권장)</summary>
        Exponential = 0,
        /// <summary>선형 백오프 (Linear): 재시도마다 일정한 간격으로 지연 시간 증가</summary>
        Linear = 1,
        /// <summary>상수 백오프 (Constant): 재시도마다 동일한 지연 시간 유지</summary>
        Constant = 2
    }

    /// <summary>
    /// 회복 탄력성(Resilience) 정책을 정의하는 옵션 클래스입니다.
    /// </summary>
    public class ResilienceOptions
    {
        // ---------------------------------------------------------------------------------
        // Retry Configuration
        // ---------------------------------------------------------------------------------

        /// <summary>최대 재시도 횟수 (기본값: 3회)</summary>
        /// <remarks>0으로 설정하면 재시도 정책이 비활성화됩니다.</remarks>
        public int MaxRetryCount
        {
            get;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "재시도 횟수는 0 이상이어야 합니다.");
                field = value;
            }
        } = 3;

        /// <summary>재시도 기본 지연 시간 (밀리초, 기본값: 100ms)</summary>
        /// <remarks>지수 백오프 시 초기 지연으로 사용됩니다.</remarks>
        public int BaseRetryDelayMs
        {
            get;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "지연 시간은 0ms 이상이어야 합니다.");
                field = value;
            }
        } = 100;

        /// <summary>재시도 최대 지연 시간 (밀리초, 기본값: 2000ms = 2초)</summary>
        /// <remarks>지수 백오프로 증가해도 이 값을 초과하지 않습니다.</remarks>
        public int MaxRetryDelayMs
        {
            get;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "최대 지연 시간은 0ms 이상이어야 합니다.");
                field = value;
            }
        } = 2000;

        /// <summary>재시도 시 Jitter(무작위 요소) 사용 여부 (기본값: true)</summary>
        /// <remarks>Thunder Herd 문제 방지를 위해 권장됩니다.</remarks>
        public bool UseRetryJitter { get; set; } = true;

        /// <summary>재시도 백오프 타입 (기본값: Exponential)</summary>
        public RetryBackoffType RetryBackoffType { get; set; } = RetryBackoffType.Exponential;

        // ---------------------------------------------------------------------------------
        // Circuit Breaker Configuration
        // ---------------------------------------------------------------------------------

        /// <summary>회로 차단기 최소 처리량 임계값 (기본값: 5)</summary>
        /// <remarks>
        /// 샘플링 기간 동안 이 값 이상의 요청이 있어야 실패율 기준으로 차단 여부를 평가합니다.
        /// 낮은 트래픽 상황에서 오탐을 방지합니다.
        /// </remarks>
        public int CircuitBreakerThreshold
        {
            get;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "임계값은 0보다 커야 합니다.");
                field = value;
            }
        } = 5;

        /// <summary>회로 차단기 샘플링 기간 (밀리초, 기본값: 30000ms = 30초)</summary>
        /// <remarks>이 기간 동안의 요청 성공/실패 비율을 집계하여 차단 여부를 결정합니다.</remarks>
        public int CircuitBreakerSamplingDurationMs
        {
            get;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "샘플링 기간은 0ms보다 커야 합니다.");
                field = value;
            }
        } = 30000;

        /// <summary>회로 차단기가 열린 후 유지되는 시간 (밀리초, 기본값: 30000ms = 30초)</summary>
        /// <remarks>이 시간이 지나면 Half-Open 상태로 전환되어 제한적으로 재연결을 시도합니다.</remarks>
        public int CircuitBreakerBreakDurationMs
        {
            get;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "차단 기간은 0ms보다 커야 합니다.");
                field = value;
            }
        } = 30000;

        /// <summary>회로 차단기가 열리는 실패 비율 임계값 (0.0 ~ 1.0, 기본값: 0.5)</summary>
        /// <remarks>샘플링 기간 동안 이 비율 이상의 요청이 실패하면 회로가 열립니다.</remarks>
        public double CircuitBreakerFailureRatio
        {
            get;
            set
            {
                if (value is < 0.0 or > 1.0) throw new ArgumentOutOfRangeException(nameof(value), value, "실패 비율은 0.0 ~ 1.0 사이여야 합니다.");
                field = value;
            }
        } = 0.5;
    }

    #endregion

    #region [9] 회복 탄력성 (Resilience)

    /// <summary>
    /// 매퍼(Mapper) 및 팩토리 캐시의 최대 항목 수 (기본값: 10,000)
    /// <para>동적 쿼리나 익명 타입 사용 시 발생할 수 있는 메모리 누수를 방지하기 위해, 이 값을 초과하면 캐시를 정리합니다.</para>
    /// </summary>
    [Range(1000, 1_000_000, ErrorMessage = "캐시 크기는 최소 1,000 이상이어야 합니다.")]
    public int MaxCacheSize
    {
        get;
        set
        {
            if (value < 1000)
                throw new ArgumentOutOfRangeException(nameof(value), value, "캐시 크기는 최소 1,000 항목 이상이어야 합니다.");
            field = value;
        }
    } = 10_000;

    /// <summary>
    /// L1 스키마 스냅샷 경고 임계값 (기본값: 5,000)
    /// <para>메모리에 로드된 스키마 객체 수가 이 값을 초과하면 경고 로그를 남겨 과도한 메타데이터 로딩을 알립니다.</para>
    /// </summary>
    public int SchemaSnapshotWarningThreshold
    {
        get;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "스키마 스냅샷 임계값은 0보다 커야 합니다.");
            field = value;
        }
    } = 5_000;

    #endregion

    #region [10] L2 캐시 및 공유 메모리


    /// <summary>
    /// 공유 메모리 기반 L2 캐시 설정
    /// <para>
    /// <b>권장 사용처:</b>
    /// <list type="bullet">
    /// <item>다중 프로세스 환경에서 스키마 캐시 공유</item>
    /// <item>IIS/Kestrel 여러 인스턴스 간 동기화</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>[하위 호환성]</b><br/>
    /// 기존 SharedMemoryMappedCacheOptions DI 등록은 계속 지원됩니다.
    /// </para>
    /// <para>
    /// <b>[AOT 호환성]</b><br/>
    /// JsonIgnore가 적용되어 IConfiguration 바인딩에서 제외됩니다.<br/>
    /// 이 속성을 설정하려면 코드에서 직접 할당하거나, PostConfigure를 사용하세요.
    /// </para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SharedMemoryCacheOptions SharedMemoryCache
    {
        get;
        set
        {
            field = value ?? throw new ArgumentNullException(nameof(value), "SharedMemoryCache 옵션은 null일 수 없습니다.");
        }
    } = new();

    /// <summary>
    /// 공유 메모리 기반 L2 캐시 사용 활성화 여부 (기본값: null = 자동 감지)
    /// <para>null: Windows에서는 true, 그 외 OS에서는 false로 자동 설정됩니다.</para>
    /// <para>true/false: OS 상관없이 강제 활성화/비활성화합니다.</para>
    /// </summary>
    public bool? EnableSharedMemoryCache { get; set; } = null;


    /// <summary>
    /// 프로세스 간 에포크 조정(Epoch Coordination) 활성화 여부
    /// <para>L2 캐시의 데이터 일관성을 위해 프로세스 간 동기화를 수행할지 결정합니다.</para>
    /// </summary>
    public bool? EnableEpochCoordination { get; set; } = null;

    /// <summary>
    /// 에포크(Epoch) 확인 주기 (초 단위, 기본값: 5초)
    /// <para>L2 캐시의 유효성을 검사하는 주기입니다.</para>
    /// </summary>
    public int EpochCheckIntervalSeconds { get; set; } = 5;

    #endregion

    #region [11] 카오스 엔지니어링

    /// <summary>
    /// 카오스 엔지니어링 설정 (기본값: 비활성)
    /// <para>개발/테스트 환경에서 시스템의 회복력을 검증하기 위한 설정입니다.</para>
    /// </summary>
    public ChaosOptions Chaos
    {
        get;
        set
        {
            field = value ?? throw new ArgumentNullException(nameof(value), "Chaos 옵션은 null일 수 없습니다.");
        }
    } = new();

    #endregion

    #region [12] 관측 가능성 및 헬스 체크


    /// <summary>
    /// DB 헬스 체크(Health Check) 최소 실행 간격 (초 단위, 기본값: 1초)
    /// <para>잦은 헬스 체크로 인한 DB 부하를 방지하기 위해 이 시간 이내의 중복 요청은 캐시된 결과를 반환합니다.</para>
    /// </summary>
    public int HealthCheckThrottleSeconds
    {
        get;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "헬스 체크 스로틀링 시간은 0초보다 커야 합니다.");
            field = value;
        }
    } = 1;

    /// <summary>
    /// 헬스 체크 쿼리(SELECT 1)의 타임아웃 시간 (초 단위, 기본값: 2초)
    /// </summary>
    public int HealthCheckTimeoutSeconds
    {
        get;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "헬스 체크 타임아웃 시간은 0초보다 커야 합니다.");
            field = value;
        }
    } = 2;

    /// <summary>
    /// OpenTelemetry 추적 및 메트릭 활성화 여부 (기본값: false)
    /// <para>true 설정 시 ActivitySource("Lib.Db")와 Meter("Lib.Db")를 통해 텔레메트리 데이터를 생성합니다.</para>
    /// <para>비활성 시 오버헤드는 0에 가깝습니다 (분기 1회).</para>
    /// </summary>
    public bool EnableOpenTelemetry { get; set; } = false;

    /// <summary>
    /// 관측 가능성(Logging, Metrics, Tracing) 기능 활성화 여부 (기본값: false)
    /// <para>이 값은 EnableOpenTelemetry 등 세부 설정의 마스터 스위치 역할을 합니다.</para>
    /// </summary>
    public bool EnableObservability { get; set; } = false;

    /// <summary>
    /// SQL 쿼리 파라미터를 추적(Trace)에 포함할지 여부 (기본값: false)
    /// <para>보안상 중요하므로, 개발 환경에서만 true로 설정하는 것이 좋습니다.</para>
    /// </summary>
    public bool IncludeParametersInTrace { get; set; } = false;

    #endregion

    #region [13] 내부 튜닝 및 고급 옵션


    /// <summary>
    /// 스키마 서비스 내부 락(Lock) 정리 임계값 (기본값: 1,000개)
    /// <para>메모리 누수 방지를 위해, 생성된 락 객체 수가 이 값을 초과하면 미사용 락 정리를 시도합니다.</para>
    /// </summary>
    public int SchemaLockCleanupThreshold
    {
        get;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "락 정리 임계값은 0보다 커야 합니다.");
            field = value;
        }
    } = 1000;

    /// <summary>
    /// 스키마 서비스 내부 락(Lock) 정리 최소 주기 (ms 단위, 기본값: 60,000ms = 1분)
    /// </summary>
    public int SchemaLockCleanupIntervalMs
    {
        get;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "락 정리 주기는 0ms보다 커야 합니다.");
            field = value;
        }
    } = 60000;

    /// <summary>
    /// 스키마 워밍업 실행 시 동시에 진행할 최대 작업 수입니다.
    /// <para>
    /// 0 이면 <see cref="Environment.ProcessorCount"/> 와 워밍업 대상 개수 중 작은 값을 사용합니다.
    /// 음수는 허용되지 않으며, 너무 큰 값은 1024를 상한으로 사용합니다.
    /// </para>
    /// </summary>
    [Range(0, 1024, ErrorMessage = "스키마 워밍업 동시성은 0 이상 1024 이하 값이어야 합니다.")]
    public int PrewarmMaxConcurrency { get; set; } = 0;

    #endregion
}


#region 관련 네스티드 옵션 클래스 (Related Option Classes)

#region ChaosOptions

/// <summary>
/// 카오스 엔지니어링 정책 옵션입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 이 클래스는 개발 및 테스트 환경에서 시스템의 회복 탄력성(Resilience)을 검증하기 위해 인위적인 오류(Exception)와 지연(Latency)을 주입하는 정책을 정의합니다.
/// 프로덕션 환경에서는 실수로 활성화되지 않도록 기본값을 비활성화 상태로 유지하며, C# 14 <c>field</c> 키워드를 활용해 설정값의 안전성을 보장합니다.
/// </para>
/// </summary>
public class ChaosOptions
{
    /// <summary>
    /// 카오스 주입 활성화 여부 (기본값: false)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 예외 발생 확률 (0.0 ~ 1.0, 기본값: 1% = 0.01)
    /// <para>쿼리 실행 시 이 확률로 인위적인 예외(SqlException 등)가 발생합니다.</para>
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "예외 발생 확률은 0.0과 1.0 사이여야 합니다.")]
    public double ExceptionRate
    {
        get;
        set
        {
            if (value is < 0.0 or > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "예외 발생 확률은 0.0과 1.0 사이여야 합니다.");
            field = value;
        }
    } = 0.01;

    /// <summary>
    /// 지연(Latency) 발생 확률 (0.0 ~ 1.0, 기본값: 5% = 0.05)
    /// <para>쿼리 실행 시 이 확률로 인위적인 대기 시간이 추가됩니다.</para>
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "지연 발생 확률은 0.0과 1.0 사이여야 합니다.")]
    public double LatencyRate
    {
        get;
        set
        {
            if (value is < 0.0 or > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "지연 발생 확률은 0.0과 1.0 사이여야 합니다.");
            field = value;
        }
    } = 0.05;

    /// <summary>
    /// 최소 지연 시간 (밀리초 단위, 기본값: 100ms)
    /// <para>지연 주입 시 적용될 최소 대기 시간입니다. 음수일 수 없습니다.</para>
    /// </summary>
    [Range(0, 60000, ErrorMessage = "최소 지연 시간은 0ms 이상이어야 합니다.")]
    public int MinLatencyMs
    {
        get;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "최소 지연 시간은 0ms 이상이어야 합니다.");
            field = value;
        }
    } = 100;

    /// <summary>
    /// 최대 지연 시간 (밀리초 단위, 기본값: 500ms)
    /// <para>지연 주입 시 적용될 최대 대기 시간입니다.</para>
    /// </summary>
    [Range(0, 60000, ErrorMessage = "최대 지연 시간은 0ms 이상이어야 합니다.")]
    public int MaxLatencyMs
    {
        get;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "최대 지연 시간은 0ms 이상이어야 합니다.");
            field = value;
        }
    } = 500;
}

#endregion // ChaosOptions

#region SharedMemoryCacheOptions

/// <summary>
/// <see cref="Caching.SharedMemoryCache"/>의 동작을 구성하는 옵션입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 이 클래스는 L2 캐시인 SharedMemoryCache의 물리적 저장 경로, 크기 제한, 공유 범위 등을 정의합니다.
/// <see cref="LibDbOptions"/>와 분리하여 캐시 관련 설정의 독립성을 보장하고, 필요한 경우 별도로 주입할 수 있도록 설계되었습니다.
/// </para>
/// </summary>
public sealed class SharedMemoryCacheOptions : Microsoft.Extensions.Options.IOptions<SharedMemoryCacheOptions>
{
    /// <summary>
    /// 캐시 파일이 저장될 기본 디렉토리 경로입니다.
    /// <para>기본값: <c>%TEMP%\Lib.Db.Cache</c></para>
    /// </summary>
    public string BasePath
    {
        get;
        set
        {
            field = string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("BasePath는 null이거나 공백일 수 없습니다.", nameof(value))
                : value;
        }
    } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lib.Db.Cache");

    /// <summary>
    /// 캐시 공유 범위입니다. (User vs Machine)
    /// </summary>
    public Caching.CacheScope Scope { get; set; } = Caching.CacheScope.User;

    /// <summary>
    /// 전체 캐시 저장소의 최대 허용 크기(바이트)입니다. (기본값: 1GB)
    /// </summary>
    [Range(1024 * 1024, long.MaxValue, ErrorMessage = "최대 캐시 크기는 최소 1MB 이상이어야 합니다.")]
    public long MaxCacheSizeBytes
    {
        get;
        set
        {
            if (value < 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(value), value, "최대 캐시 크기는 최소 1MB 이상이어야 합니다.");
            field = value;
        }
    } = 1024L * 1024L * 1024L;

    /// <summary>
    /// MMF 초기화 실패 시 폴백으로 사용할 인메모리 캐시 인스턴스입니다. (선택 사항)
    /// </summary>
    [JsonIgnore] // AOT: IDistributedCache는 Configuration 바인딩 불가
    public Microsoft.Extensions.Caching.Distributed.IDistributedCache? FallbackCache { get; set; }

    /// <summary>
    /// (내부용) 격리 키. ConnectionString 해시 등이 설정됩니다.
    /// </summary>
    public string? IsolationKey { get; set; }

    SharedMemoryCacheOptions Microsoft.Extensions.Options.IOptions<SharedMemoryCacheOptions>.Value => this;
}

#endregion // SharedMemoryCacheOptions

#endregion // 관련 네스티드 옵션 클래스