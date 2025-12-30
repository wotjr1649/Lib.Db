// ============================================================================
// 파일: Lib.Db/Extensions/AdvancedSnapshotExtensions.cs
// 설명: 고급/특수 목적용 Snapshot 제어 확장 메서드
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Lib.Db.Fluent;

namespace Lib.Db.Extensions;

/// <summary>
/// 고급/특수 목적용 Snapshot 제어 기능입니다.
/// <para>
/// 일반 업무 코드에서는 사용하지 말고,
/// 도메인 전용 Helper/Repository 내부에서만 사용하십시오.
/// </para>
/// </summary>
public static class AdvancedSnapshotExtensions
{
    /// <summary>
    /// 이 명령에 대해 L1 SnapshotOnly 모드를 강제로 사용합니다.
    /// <para>Prewarm/Snapshot 미구성 환경에서는 실행 시 예외가 발생합니다.</para>
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IParameterStage UseSnapshotOnlyUnsafe(this IParameterStage stage)
    {
        if (stage is not DbRequestBuilder builder)
        {
            throw new InvalidOperationException(
                            "내부 빌더 타입 불일치: 이 기능은 DbRequestBuilder 에서만 사용할 수 있습니다.");
        }

        builder.OverrideSchemaMode(SchemaResolutionMode.SnapshotOnly);
        return stage;
    }

    /// <summary>
    /// 이 명령에 대해 ServiceOnly 모드를 강제로 사용합니다.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IParameterStage UseServiceOnlyUnsafe(this IParameterStage stage)
    {
        if (stage is not DbRequestBuilder builder)
        {
            throw new InvalidOperationException(
                            "내부 빌더 타입 불일치: 이 기능은 DbRequestBuilder 에서만 사용할 수 있습니다.");
        }

        builder.OverrideSchemaMode(SchemaResolutionMode.ServiceOnly);
        return stage;
    }

    /// <summary>
    /// 이 명령에 대해 Snapshot 우선 + Service 폴백 모드를 강제로 사용합니다.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IParameterStage UseSnapshotPreferredUnsafe(this IParameterStage stage)
    {
        if (stage is not DbRequestBuilder builder)
        {
            throw new InvalidOperationException(
                            "내부 빌더 타입 불일치: 이 기능은 DbRequestBuilder 에서만 사용할 수 있습니다.");
        }

        builder.OverrideSchemaMode(SchemaResolutionMode.SnapshotThenServiceFallback);
        return stage;
    }
}
