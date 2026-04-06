// ============================================================================
// 파일: Infrastructure/TestDtos.cs
// 설명: IntegrationTests 전용 DTO 클래스/레코드 모음 (TestSuite DTOs.cs에서 이관)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;

namespace Lib.Db.IntegrationTests.Infrastructure;

#region [core] 스키마 DTOs

/// <summary>
/// [core].[Users] 테이블용 DTO
/// </summary>
public sealed class CoreUser
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public int? Age { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// [core].[Orders] 테이블용 DTO
/// </summary>
public sealed class CoreOrder
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime OrderDate { get; set; }
}

/// <summary>
/// [core].[Products] 테이블용 DTO
/// </summary>
public sealed class CoreProduct
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// [core].[Tvp_Core_User] TVP용 DTO
/// </summary>
[TvpRow(TypeName = "core.Tvp_Core_User")]
public sealed record CoreUserTvp
{
    [TvpLength(100)]
    public required string UserName { get; init; }

    [TvpLength(255)]
    public required string Email { get; init; }

    public int? Age { get; init; }
}

#endregion

#region [tvp] 스키마 DTOs

/// <summary>
/// [tvp].[TypeTest] 테이블용 DTO (.NET 10 타입 전반 테스트)
/// </summary>
public sealed record TvpTypeTest(
    int Id,
    DateOnly DateOnlyValue,
    TimeOnly TimeOnlyValue,
    Half HalfValue,
    Guid GuidValue,
    decimal DecimalValue,
    DateOnly? NullableDateOnly,
    TimeOnly? NullableTimeOnly,
    Half? NullableHalf,
    DateTime CreatedAt
);

/// <summary>
/// [tvp].[Tvp_Tvp_AllTypes] TVP용 DTO
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_AllTypes")]
public sealed record TvpAllTypes
{
    public required DateOnly DateOnlyValue { get; init; }
    public required TimeOnly TimeOnlyValue { get; init; }
    public required Half HalfValue { get; init; }
    public required Guid GuidValue { get; init; }
    public required decimal DecimalValue { get; init; }
}

/// <summary>
/// [tvp].[Tvp_Tvp_Nullable] TVP용 DTO
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_Nullable")]
public sealed record TvpNullable
{
    public DateOnly? NullableDateOnly { get; init; }
    public TimeOnly? NullableTimeOnly { get; init; }
    public Half? NullableHalf { get; init; }
}

#endregion

#region [perf] 스키마 DTOs

/// <summary>
/// [perf].[BulkTest] 테이블용 DTO
/// </summary>
public sealed record PerfBulkTest(
    long Id,
    int BatchNumber,
    string? Data,
    DateTime CreatedAt
);

/// <summary>
/// [perf].[Tvp_Perf_BulkInsert] TVP용 DTO
/// </summary>
[TvpRow(TypeName = "perf.Tvp_Perf_BulkInsert")]
public sealed record PerfBulkInsertTvp
{
    public required int BatchNumber { get; set; }

    [TvpLength(500)]
    public string? Data { get; init; }
}

#endregion

#region [gap] 스키마 DTOs

/// <summary>
/// [gap].[Tvp_BulkTarget] TVP용 DTO (10K 벌크 삽입 테스트)
/// </summary>
[TvpRow(TypeName = "gap.Tvp_BulkTarget")]
public sealed record GapBulkTargetTvp
{
    [TvpLength(200)]
    public required string Data { get; init; }

    public required int BatchId { get; init; }
}

/// <summary>
/// [gap].[usp_Merge_Upsert] 결과 DTO
/// </summary>
public sealed record GapMergeResult
{
    public string MergeAction { get; set; } = "";
}

/// <summary>
/// [gap].[usp_Json_Query] 결과 DTO
/// </summary>
public sealed record GapJsonQueryResult
{
    public int Id { get; set; }
    public string? ExtractedValue { get; set; }
    public string? Payload { get; set; }
}

/// <summary>
/// [gap].[usp_Paginate] 페이지 데이터 DTO
/// </summary>
public sealed record GapPaginatedUser
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public int? Age { get; set; }
}

/// <summary>
/// [gap].[usp_Paginate] 총 건수 DTO
/// </summary>
public sealed record GapTotalCount
{
    public int TotalCount { get; set; }
}

#endregion

#region [tvp] 추가 DTOs

/// <summary>
/// [tvp].[Tvp_Tvp_SchemaMismatch] TVP용 DTO (의도적 스키마 불일치 테스트)
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_SchemaMismatch")]
public sealed record TvpSchemaMismatch
{
    [TvpLength(50)]
    public string? ColumnA { get; init; }

    public int? ColumnB { get; init; }
    public DateTime? ColumnC { get; init; }
}

#endregion

#region [exception] 스키마 DTOs

/// <summary>
/// [exception].[ParentTable] 테이블용 DTO
/// </summary>
public sealed record ExceptionParent(
    int ParentId,
    string ParentName
);

/// <summary>
/// [exception].[ChildTable] 테이블용 DTO
/// </summary>
public sealed record ExceptionChild(
    int ChildId,
    int ParentId,
    string ChildName
);

/// <summary>
/// [exception].[UniqueTable] 테이블용 DTO
/// </summary>
public sealed record ExceptionUnique(
    int Id,
    string UniqueValue,
    DateTime CreatedAt
);

#endregion

#region [resilience] 스키마 DTOs

/// <summary>
/// [resilience].[RetryTest] 테이블용 DTO
/// </summary>
public sealed record ResilienceRetryTest(
    int Id,
    int AttemptNumber,
    bool SuccessFlag,
    DateTime AttemptedAt
);

/// <summary>
/// [resilience].[TimeoutTest] 테이블용 DTO
/// </summary>
public sealed record ResilienceTimeoutTest(
    int Id,
    int DelaySeconds,
    DateTime CompletedAt
);

#endregion

#region DataTable 변환 테스트용 DTOs

/// <summary>
/// DataTable 변환 테스트용 User DTO
/// </summary>
public sealed record User(
    int UserId,
    string UserName,
    string Email,
    int? Age
);

#endregion

#region [adv] 스키마 DTOs

/// <summary>
/// [adv].[ResumableLogs] 테이블용 DTO
/// </summary>
public sealed record AdvLog(
    int LogId,
    string Message,
    DateTime CreatedAt
);

/// <summary>
/// Source Generator [DbResult] 테스트용 DTO (partial 필수)
/// </summary>
[DbResult]
public partial class DbResultUser
{
    public int UserId { get; init; }
    public string UserName { get; init; } = "";
    public string Email { get; init; } = "";
    public int? Age { get; init; }

    public string? Name { get; init; }
    public int? Val { get; init; }
}

/// <summary>
/// AdvancedQueryTests에서 사용
/// </summary>
public sealed class ResumableLogDto
{
    public DateTime CreatedAt { get; set; }
    public string Message { get; set; } = "";
}

#endregion

#region Dashboard DTOs (다중 결과셋)

/// <summary>
/// Dashboard 사용자 정보 DTO (ResultSet 1)
/// </summary>
public sealed record DashboardUserInfo(
    int UserId,
    string UserName,
    string Email
);

/// <summary>
/// Dashboard 주문 DTO (ResultSet 2)
/// </summary>
public sealed record DashboardOrder(
    int OrderId,
    int ProductId,
    int Quantity,
    decimal TotalPrice,
    DateTime OrderDate
);

/// <summary>
/// Dashboard 집계 DTO (ResultSet 3)
/// </summary>
public sealed record DashboardStats(
    int TotalOrders,
    decimal? TotalSpent
);

#endregion
