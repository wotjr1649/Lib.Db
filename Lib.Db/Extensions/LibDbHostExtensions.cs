// ============================================================================
// 파일: Lib.Db/Extensions/LibDbHostExtensions.cs
// 설명: IHost 확장 메서드 - 정적 바인딩 엔진 초기화
// 타겟: .NET 10 / C# 14
// ============================================================================
#nullable enable

using System.Collections.Concurrent;
using System.Reflection;
using Lib.Db.Contracts.Schema;
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
    #region [필드] 정적 필드 - TVP Validator 캠시

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

    /// <summary>
    /// (DTO 타입, UDT 이름) 단위로 TVP 검증 Task를 캐싱합니다.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type DtoType, string UdtName), Task> s_tvpValidationTasks = new();

    #endregion

    /// <summary>
    /// 정적 바인딩 엔진과 TVP Validator를 연결하고 초기화합니다.
    /// <para>
    /// <b>[호출 시점]</b> IHost 빌드 직후, 앱 시작 전에 1회 호출
    /// </para>
    /// </summary>
    /// <param name="host">호스트 인스턴스</param>
    /// <returns>체이닝을 위한 IHost</returns>
    public static IHost UseHighPerformanceDb(this IHost host)
    {
        // DataBindingEngine(Static) -> Validator(DI Instance) 브리지 설정
        DbBinder.ValidatorCallback = (dtoType, udtName) =>
        {
            var validator = host.Services.GetRequiredService<ITvpSchemaValidator>();
            var options = host.Services.GetRequiredService<LibDbOptions>();
            
            // 첫 번째 연결 문자열 사용 (Smart Pointer 적용)
            // options.ConnectionStringName이 유효하면 그것을, 아니면 Fallback으로 첫 번째 키 사용
            string instanceKey = options.ConnectionStrings.ContainsKey(options.ConnectionStringName)
                ? options.ConnectionStringName
                : (options.ConnectionStrings.Keys.FirstOrDefault() ?? "Default");
            var key = (dtoType, udtName);

            // 중복 검증 방지를 위한 Task 캐싱
            var validationTask = s_tvpValidationTasks.GetOrAdd(key, (tuple, state) =>
            {
                var (dto, udt) = tuple;
                var (v, accessorInfo, validateInfo, connKey) = 
                    ((ITvpSchemaValidator, MethodInfo, MethodInfo, string))state;

                // 1. Accessor 생성
                var accessors = accessorInfo.MakeGenericMethod(dto).Invoke(null, null)
                    ?? throw new InvalidOperationException($"TVP Accessor 생성 실패: {dto.Name}");

                // 2. ValidateAsync 호출 (Task 반환)
                var method = validateInfo.MakeGenericMethod(dto);
                return (Task)method.Invoke(v, [udt, accessors, connKey, CancellationToken.None])!;
            }, (validator, s_getTypedAccessorsOpenMethod, s_validateAsyncOpenMethod, instanceKey));

            // 동기 컨텍스트에서 대기 (Sync-over-Async: 초기화 시점에만 발생)
            validationTask.GetAwaiter().GetResult();
            
            // 예외 없이 완료되었다면 검증 성공
            return true;
        };

        return host;
    }
}
