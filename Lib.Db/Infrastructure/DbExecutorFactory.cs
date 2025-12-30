// ============================================================================
// 파일명 : Lib.Db/Contracts/Execution/DbExecutorFactory.cs
// 설명   : DB 실행기 팩터리 구현
// 대상   : .NET 10 / C# 14
// 역할   :
//   - [전략 패턴] Resilient(Polly) vs Transactional(Atomicity) 실행 전략의 구체화
//   - [조립] Schema Service, Mapper, Interceptor, Chaos 등 하위 컴포넌트의 결합
//   - [의존성 격리] Contracts 레이어와 실행 구현체(SqlDbExecutor) 간의 완충 지대
// ============================================================================
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Schema;
using Lib.Db.Contracts.Execution; // Added for IDbExecutorFactory, IDbExecutor, etc.

using Lib.Db.Execution;

using Lib.Db.Execution.Executors;
using Polly;
using Polly.Registry;

namespace Lib.Db.Infrastructure;

#region [실행기 팩터리 구현] Resilient / Transactional 전략 구성

/// <summary>
/// DB 실행기 팩터리의 기본 구현체입니다.
/// <para>
/// 실행기 생성 시 다음 구성 요소들을 조합합니다.
/// <list type="bullet">
/// <item>실행 전략(Resilient / Transactional)</item>
/// <item>스키마 서비스(Self-Healing 지원)</item>
/// <item>매핑 팩터리(Source Generator / Reflection)</item>
/// <item>인터셉터 체인(로깅/Mocking/계측)</item>
/// <item>회복 탄력성/메모리 배압/카오스 주입</item>
/// </list>
/// </para>
/// </summary>
/// <remarks>
/// <b>[아키텍처 통찰: Factory as a Assembler]</b>
/// <para>
/// 이 팩터리는 단순히 객체를 생성(new)하는 것을 넘어, 실행 컨텍스트(Context)에 맞는
/// <b>'최적의 부품 조합(Assembly)'</b>을 결정합니다.
/// </para>
/// <list type="table">
/// <listheader><term>전략(Strategy)</term><description>구성 및 철학</description></listheader>
/// <item>
/// <term>Resilient</term>
/// <description>
/// <c>ResilientStrategy</c> + <c>Polly Pipeline</c> + <c>Self-Healing Schema</c><br/>
/// "실패는 불가피하다"는 가정 하에, 재시도와 회로 차단으로 시스템을 보호합니다.
/// </description>
/// </item>
/// <item>
/// <term>Transactional</term>
/// <description>
/// <c>TransactionalStrategy</c> + <c>Snapshot Schema</c><br/>
/// "무결성이 우선이다"는 원칙 하에, 외부 트랜잭션의 생명주기에 종속되며 회복 탄력성보다는 원자성을 보장합니다.
/// </description>
/// </item>
/// </list>
/// </remarks>
internal sealed class DbExecutorFactory : IDbExecutorFactory
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IResiliencePipelineProvider _pipelineProvider;
    private readonly ISchemaService _schemaService;
    private readonly IMapperFactory _mapperFactory;
    private readonly IResumableStateStore _resumableStore;
    private readonly IMemoryPressureMonitor _memoryMonitor;
    private readonly IChaosInjector _chaosInjector;
    private readonly IEnumerable<IDbCommandInterceptor> _interceptors;
    private readonly LibDbOptions _options;
    private readonly ILogger<SqlDbExecutor> _logger;

    #region [생성자] 의존성 주입

    public DbExecutorFactory(
        IDbConnectionFactory connFactory,
        IResiliencePipelineProvider pipelineProvider,
        ISchemaService schemaService,
        IMapperFactory mapperFactory,
        IResumableStateStore resumableStore,
        IMemoryPressureMonitor memoryMonitor,
        IChaosInjector chaosInjector,
        IEnumerable<IDbCommandInterceptor> interceptors,
        LibDbOptions options,
        ILogger<SqlDbExecutor> logger)
    {
        _connFactory = connFactory;
        _pipelineProvider = pipelineProvider;
        _schemaService = schemaService;
        _mapperFactory = mapperFactory;
        _resumableStore = resumableStore;
        _memoryMonitor = memoryMonitor;
        _chaosInjector = chaosInjector;
        _interceptors = interceptors;
        _options = options;
        _logger = logger;
    }

    #endregion

    #region [Resilient 실행기 생성] 재시도/회복 탄력성 전략

    /// <summary>
    /// Polly <see cref="ResiliencePipeline"/>을 사용하는
    /// Resilient 실행기를 생성합니다.
    /// </summary>
    public IDbExecutor CreateResilient()
    {
        // 회복 탄력성 실행 전략 구성
        var strategy = new ResilientStrategy(
            _connFactory,
            _pipelineProvider,
            _schemaService,
            _logger);

        // 인터셉터 체인 구성 (로깅/Mock/계측 등)
        var chain = new InterceptorChain(_interceptors);

        // 최종 실행기 조립
        return new SqlDbExecutor(
            strategy,
            _schemaService,
            _mapperFactory,
            _resumableStore,
            _memoryMonitor,
            _chaosInjector,
            chain,
            _options,
            _logger);
    }

    #endregion

    #region [Transactional 실행기 생성] 외부 트랜잭션 공유

    /// <summary>
    /// 기존 <see cref="SqlConnection"/> / <see cref="SqlTransaction"/>을 공유하는
    /// Transactional 실행기를 생성합니다.
    /// </summary>
    /// <param name="conn">공유할 DB 연결</param>
    /// <param name="tx">공유할 트랜잭션</param>
    public IDbExecutor CreateTransactional(SqlConnection conn, SqlTransaction tx)
    {
        // 외부 트랜잭션을 사용하는 실행 전략 구성
        var strategy = new TransactionalStrategy(
            conn,
            tx,
            _schemaService,
            _logger);

        // 인터셉터 체인 구성
        var chain = new InterceptorChain(_interceptors);

        // 최종 실행기 조립
        return new SqlDbExecutor(
            strategy,
            _schemaService,
            _mapperFactory,
            _resumableStore,
            _memoryMonitor,
            _chaosInjector,
            chain,
            _options,
            _logger);
    }

    #endregion
}

#endregion
