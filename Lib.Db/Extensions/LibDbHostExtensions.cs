// ============================================================================
// 파일: Lib.Db/Extensions/LibDbHostExtensions.cs
// 설명: IHost 확장 메서드 - 정적 바인딩 엔진 초기화
// 타겟: .NET 10 / C# 14
// ============================================================================
#nullable enable

using System.Reflection;
using Lib.Db.Contracts.Schema;
using Lib.Db.Core;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Tvp;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Lib.Db IHost 확장 메서드입니다.
/// <para>
/// <b>[중요]</b> 앱 구동 시점(IHost 빌드 직후)에 1회 호출해야 합니다.
/// </para>
/// </summary>
public static class LibDbHostExtensions
{
    #region [필드] 정적 Reflection 메타데이터

    /// <summary>
    /// ITvpSchemaValidator.ValidateAsync 열린 제네릭 메서드 메타데이터입니다.
    /// </summary>
    private static readonly MethodInfo s_validateAsyncOpenMethod =
        typeof(ITvpSchemaValidator).GetMethod(nameof(ITvpSchemaValidator.ValidateAsync))
        ?? throw new InvalidOperationException(
            "ITvpSchemaValidator.ValidateAsync 메서드를 찾을 수 없습니다.");

    /// <summary>
    /// TvpAccessorCache.GetTypedAccessors 열린 제네릭 메서드 메타데이터입니다.
    /// </summary>
    private static readonly MethodInfo s_getTypedAccessorsOpenMethod =
        typeof(TvpAccessorCache).GetMethod(
            nameof(TvpAccessorCache.GetTypedAccessors),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "TvpAccessorCache.GetTypedAccessors 메서드를 찾을 수 없습니다.");

    #endregion

    /// <summary>
    /// 정적 바인딩 엔진과 TVP Validator를 연결하고 초기화합니다.
    /// <para>
    /// <b>[호출 시점]</b> IHost 빌드 직후, 앱 시작 전에 1회 호출
    /// </para>
    /// <para>
    /// 등록된 Validator 콜백은 실제 TVP 바인딩 시점마다 호출되며,
    /// 현재 실행 컨텍스트의 DB 인스턴스를 우선 사용합니다.
    /// </para>
    /// </summary>
    /// <param name="host">호스트 인스턴스</param>
    /// <returns>체이닝을 위한 IHost</returns>
    public static IHost UseHighPerformanceDb(this IHost host)
    {
        // DataBindingEngine(Static) -> Validator(DI Instance) 브리지 설정
        DbBinder.ValidatorCallback = (dtoType, udtName) =>
        {
            ITvpSchemaValidator validator = host.Services.GetRequiredService<ITvpSchemaValidator>();
            LibDbOptions options = host.Services.GetRequiredService<LibDbOptions>();

            DbExecutionContext? currentContext = DbExecutionContextScope.Current;
            string instanceKey = currentContext?.InstanceName
                ?? (options.ConnectionStringNames.Count == 1
                    ? options.ConnectionStringNames[0]
                    : throw new InvalidOperationException(
                        "TVP 검증에는 현재 실행 DB 인스턴스 컨텍스트가 필요합니다. " +
                        "멀티 인스턴스 구성에서는 Lib.Db 실행 파이프라인을 통해 바인딩하거나 명시적 컨텍스트를 설정하세요."));

            // 1. Accessor 생성
            object accessors = s_getTypedAccessorsOpenMethod.MakeGenericMethod(dtoType).Invoke(null, null)
                ?? throw new InvalidOperationException($"TVP Accessor 생성 실패: {dtoType.Name}");

            // 2. ValidateAsync 호출 (Task 반환)
            MethodInfo method = s_validateAsyncOpenMethod.MakeGenericMethod(dtoType);
            Task validationTask = (Task)method.Invoke(
                validator,
                [udtName, accessors, instanceKey, CancellationToken.None])!;

            // DbBinder의 동기 콜백 계약을 맞추기 위한 브리지입니다.
            // 실제 대기는 TVP 바인딩 시점에 발생하며, 내부 validator의 identity cache가 중복 검증을 방지합니다.
            validationTask.GetAwaiter().GetResult();

            // 예외 없이 완료되었다면 검증 성공
            return true;
        };

        return host;
    }
}
