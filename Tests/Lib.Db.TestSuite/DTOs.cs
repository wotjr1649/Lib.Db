// ============================================================================
// DTOs.cs
// 목적: Lib.Db 검증(Verification) 테스트용 DTO 클래스/레코드 모음
//       테스트 프로젝트 전반에서 공유하는 DTO (Single Source of Truth)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.ComponentModel.DataAnnotations;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;

namespace Lib.Db.Verification.Tests;

// ============================================================================
// [core] 스키마 DTOs
// ============================================================================

/// <summary>
/// [core].[Users] 테이블용 DTO
/// </summary>
// public record CoreUser(
//     int UserId,
//     string UserName,
//     string Email,
//     int? Age,
//     DateTime CreatedAt
// );
public class CoreUser
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public int? Age { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// [core].[Tvp_Core_User] TVP용 DTO
/// </summary>
[TvpRow(TypeName = "core.Tvp_Core_User")]
public record CoreUserTvp
{
    [TvpLength(100)]
    public required string UserName { get; init; }

    [TvpLength(255)]
    public required string Email { get; init; }

    public int? Age { get; init; }
}

/// <summary>
/// [core].[Products] 테이블용 DTO
/// </summary>
// public record CoreProduct(
//     int ProductId,
//     string ProductName,
//     decimal Price,
//     int Stock,
//     DateTime CreatedAt
// );
public class CoreProduct
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// [core].[Orders] 테이블용 DTO
/// </summary>
// public record CoreOrder(
//     int OrderId,
//     int UserId,
//     int ProductId,
//     int Quantity,
//     decimal TotalPrice,
//     DateTime OrderDate
// );
public class CoreOrder
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime OrderDate { get; set; }
}

/// <summary>
/// Dashboard 집계/조회 결과용 DTO
/// </summary>
public record DashboardUserInfo(
    int UserId,
    string UserName,
    string Email
);

public record DashboardOrder(
    int OrderId,
    int ProductId,
    int Quantity,
    decimal TotalPrice,
    DateTime OrderDate
);

public record DashboardStats(
    int TotalOrders,
    decimal? TotalSpent
);

// ============================================================================
// [tvp] 스키마 DTOs (.NET 10 타입 테스트)
// ============================================================================

/// <summary>
/// [tvp].[TypeTest] 테이블용 DTO (.NET 10 타입 전반 테스트)
/// </summary>
public record TvpTypeTest(
    int Id,
    DateOnly DateOnlyValue,
    TimeOnly TimeOnlyValue,
    // Int128은 Microsoft.Data.SqlClient TVP에서 미지원/제약 가능성이 있어 제외
    Half HalfValue,
    Guid GuidValue,
    decimal DecimalValue,
    DateOnly? NullableDateOnly,
    TimeOnly? NullableTimeOnly,
    // Int128은 Microsoft.Data.SqlClient TVP에서 미지원/제약 가능성이 있어 제외
    Half? NullableHalf,
    DateTime CreatedAt
);

/// <summary>
/// [tvp].[Tvp_Tvp_AllTypes] TVP용 DTO
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_AllTypes")]
public record TvpAllTypes
{
    public required DateOnly DateOnlyValue { get; init; }
    public required TimeOnly TimeOnlyValue { get; init; }

    // Int128은 Microsoft.Data.SqlClient TVP에서 미지원/제약 가능성이 있어 제외
    public required Half HalfValue { get; init; }

    public required Guid GuidValue { get; init; }

    // Note: Decimal(18,4) 등 스키마 정의에 맞춰 매핑
    public required decimal DecimalValue { get; init; }
}

/// <summary>
/// [tvp].[Tvp_Tvp_Nullable] TVP용 DTO
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_Nullable")]
public record TvpNullable
{
    public DateOnly? NullableDateOnly { get; init; }
    public TimeOnly? NullableTimeOnly { get; init; }

    // Int128은 Microsoft.Data.SqlClient TVP에서 미지원/제약 가능성이 있어 제외
    public Half? NullableHalf { get; init; }
}

/// <summary>
/// [tvp].[Tvp_Tvp_SchemaMismatch] TVP용 DTO (의도적 스키마 불일치 테스트)
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_SchemaMismatch")]
public record TvpSchemaMismatch
{
    [TvpLength(50)]
    public string? ColumnA { get; init; }

    public int? ColumnB { get; init; }
    public DateTime? ColumnC { get; init; }
}

// ============================================================================
// [perf] 스키마 DTOs
// ============================================================================

/// <summary>
/// [perf].[BulkTest] 테이블용 DTO
/// </summary>
public record PerfBulkTest(
    long Id,
    int BatchNumber,
    string? Data,
    DateTime CreatedAt
);

/// <summary>
/// [perf].[Tvp_Perf_BulkInsert] TVP용 DTO
/// </summary>
[TvpRow(TypeName = "perf.Tvp_Perf_BulkInsert")]
public record PerfBulkInsertTvp
{
    // [수정] TVP 바인딩/직렬화 과정에서 필요 시 set 허용(테스트 편의)
    // init-only로 두면 테스트 데이터 빌더/변형 시 제약이 커질 수 있음
    public required int BatchNumber { get; set; }

    [TvpLength(500)]
    public string? Data { get; init; }
}

// ============================================================================
// [exception] 스키마 DTOs
// ============================================================================

/// <summary>
/// [exception].[ParentTable] 테이블용 DTO
/// </summary>
/// <param name="ParentId">Parent ID (PK)</param>
/// <param name="ParentName">Parent 이름</param>
public record ExceptionParent(
    int ParentId,
    string ParentName
);

/// <summary>
/// [exception].[ChildTable] 테이블용 DTO
/// </summary>
/// <param name="ChildId">Child ID (PK)</param>
/// <param name="ParentId">Parent ID (FK)</param>
/// <param name="ChildName">Child 이름</param>
public record ExceptionChild(
    int ChildId,
    int ParentId,
    string ChildName
);

/// <summary>
/// [exception].[UniqueTable] 테이블용 DTO
/// </summary>
/// <param name="Id">ID (IDENTITY PK)</param>
/// <param name="UniqueValue">Unique 제약 조건 컬럼 값</param>
/// <param name="CreatedAt">생성 일시</param>
public record ExceptionUnique(
    int Id,
    string UniqueValue,
    DateTime CreatedAt
);

// ============================================================================
// [resilience] 스키마 DTOs
// ============================================================================

/// <summary>
/// [resilience].[RetryTest] 테이블용 DTO
/// </summary>
public record ResilienceRetryTest(
    int Id,
    int AttemptNumber,
    bool SuccessFlag,
    DateTime AttemptedAt
);

/// <summary>
/// [resilience].[TimeoutTest] 테이블용 DTO
/// </summary>
public record ResilienceTimeoutTest(
    int Id,
    int DelaySeconds,
    DateTime CompletedAt
);

// ============================================================================
// DataTable 변환 테스트용 DTOs
// ============================================================================

/// <summary>
/// DataTable 변환 테스트용 User DTO
/// </summary>
public record User(
    int UserId,
    string UserName,
    string Email,
    int? Age
);

// ============================================================================
// [adv] 스키마 DTOs 및 [DbResult] 테스트
// ============================================================================

/// <summary>
/// [adv].[ResumableLogs] 테이블용 DTO
/// </summary>
public record AdvLog(
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

    // Source Generator 확장/호환 테스트용(예: CS0117 대응 시나리오)
    public string? Name { get; init; }
    public int? Val { get; init; }
}

// AdvancedQueryTests에서 사용
public class ResumableLogDto
{
    public DateTime CreatedAt { get; set; }
    public string Message { get; set; } = "";
}
