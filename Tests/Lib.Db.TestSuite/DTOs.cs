// ============================================================================
// DTOs.cs
// ëª©ì : Lib.Db ê²€ì¦??ŒìŠ¤?¸ìš© DTO ?´ë˜?¤ë“¤
//       ?ŒìŠ¤???„ë¡œ?íŠ¸ ?„ë°˜?ì„œ ê³µìœ ?˜ëŠ” DTO (Single Source of Truth)            
// ?€?? .NET 10 / C# 14
// ============================================================================

using System.ComponentModel.DataAnnotations;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Mapping;

namespace Lib.Db.Verification.Tests;

// ============================================================================
// [core] ?¤í‚¤ë§?DTOs
// ============================================================================

/// <summary>
/// [core].[Users] ?Œì´ë¸”ìš© DTO
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
/// [core].[Tvp_Core_User] TVP??DTO
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
/// [core].[Products] ?Œì´ë¸”ìš© DTO
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
/// [core].[Orders] ?Œì´ë¸”ìš© DTO
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
/// Dashboard ?¤ì¤‘ ê²°ê³¼?‹ìš© DTO
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
// [tvp] ?¤í‚¤ë§?DTOs (.NET 10 ?€??
// ============================================================================

/// <summary>
/// [tvp].[TypeTest] ?Œì´ë¸”ìš© DTO (.NET 10 ?„ì²´ ?€??
/// </summary>
public record TvpTypeTest(
    int Id,
    DateOnly DateOnlyValue,
    TimeOnly TimeOnlyValue,
    // Int128?€ Microsoft.Data.SqlClient TVP?ì„œ ë¯¸ì??????œê±°
    Half HalfValue,
    Guid GuidValue,
    decimal DecimalValue,
    DateOnly? NullableDateOnly,
    TimeOnly? NullableTimeOnly,
    // Int128?€ Microsoft.Data.SqlClient TVP?ì„œ ë¯¸ì??????œê±°
    Half? NullableHalf,
    DateTime CreatedAt
);

/// <summary>
/// [tvp].[Tvp_Tvp_AllTypes] TVP??DTO
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_AllTypes")]
public record TvpAllTypes
{
    public required DateOnly DateOnlyValue { get; init; }
    public required TimeOnly TimeOnlyValue { get; init; }
    // Int128?€ Microsoft.Data.SqlClient TVP?ì„œ ë¯¸ì??????œê±°
    public required Half HalfValue { get; init; }
    public required Guid GuidValue { get; init; }
    
    // Note: Decimal(18,4)???ë™ ë§¤í•‘??
    public required decimal DecimalValue { get; init; }
}

/// <summary>
/// [tvp].[Tvp_Tvp_Nullable] TVP??DTO
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_Nullable")]
public record TvpNullable
{
    public DateOnly? NullableDateOnly { get; init; }
    public TimeOnly? NullableTimeOnly { get; init; }
    // Int128?€ Microsoft.Data.SqlClient TVP?ì„œ ë¯¸ì??????œê±°
    public Half? NullableHalf { get; init; }
}

/// <summary>
/// [tvp].[Tvp_Tvp_SchemaMismatch] TVP??DTO (?˜ë„??ë¶ˆì¼ì¹?
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
// [perf] ?¤í‚¤ë§?DTOs
// ============================================================================

/// <summary>
/// [perf].[BulkTest] ?Œì´ë¸”ìš© DTO
/// </summary>
public record PerfBulkTest(
    long Id,
    int BatchNumber,
    string? Data,
    DateTime CreatedAt
);

/// <summary>
/// [perf].[Tvp_Perf_BulkInsert] TVP??DTO
/// </summary>
[TvpRow(TypeName = "perf.Tvp_Perf_BulkInsert")]
public record PerfBulkInsertTvp
{
    // [?˜ì • ?? public required int BatchNumber { get; init; }
    // [?˜ì • ?? init??set?¼ë¡œ ë³€ê²½í•˜???˜ì • ê°€?¥í•˜ê²?ë§Œë“¦
    public required int BatchNumber { get; set; }

    [TvpLength(500)]
    public string? Data { get; init; }
}

// ============================================================================
// [exception] ?¤í‚¤ë§?DTOs
// ============================================================================

/// <summary>
/// [exception].[ParentTable] ?Œì´ë¸”ìš© DTO
/// </summary>
/// <param name="ParentId">Parent ID (PK)</param>
/// <param name="ParentName">Parent ?´ë¦„</param>
public record ExceptionParent(
    int ParentId,
    string ParentName
);

/// <summary>
/// [exception].[ChildTable] ?Œì´ë¸”ìš© DTO
/// </summary>
/// <param name="ChildId">Child ID (PK)</param>
/// <param name="ParentId">Parent ID (FK)</param>
/// <param name="ChildName">Child ?´ë¦„</param>
public record ExceptionChild(
    int ChildId,
    int ParentId,
    string ChildName
);

/// <summary>
/// [exception].[UniqueTable] ?Œì´ë¸”ìš© DTO
/// </summary>
/// <param name="Id">ID (IDENTITY PK)</param>
/// <param name="UniqueValue">Unique ?œì•½??ê±¸ë¦° ê°?/param>
/// <param name="CreatedAt">?ì„± ?¼ì‹œ</param>
public record ExceptionUnique(
    int Id,
    string UniqueValue,
    DateTime CreatedAt
);

// ============================================================================
// [resilience] ?¤í‚¤ë§?DTOs
// ============================================================================

/// <summary>
/// [resilience].[RetryTest] ?Œì´ë¸”ìš© DTO
/// </summary>
public record ResilienceRetryTest(
    int Id,
    int AttemptNumber,
    bool SuccessFlag,
    DateTime AttemptedAt
);

/// <summary>
/// [resilience].[TimeoutTest] ?Œì´ë¸”ìš© DTO
/// </summary>
public record ResilienceTimeoutTest(
    int Id,
    int DelaySeconds,
    DateTime CompletedAt
);

// ============================================================================
// DataTable ?¸í™˜???ŒìŠ¤?¸ìš© DTOs
// ============================================================================

/// <summary>
/// DataTable ë³€???ŒìŠ¤?¸ìš© User DTO
/// </summary>
public record User(
    int UserId,
    string UserName,
    string Email,
    int? Age
);

// ============================================================================
// [adv] ?¤í‚¤ë§?DTOs ë°?[DbResult] ?ŒìŠ¤??
// ============================================================================

/// <summary>
/// [adv].[ResumableLogs] ?Œì´ë¸”ìš© DTO
/// </summary>
public record AdvLog(
    int LogId,
    string Message,
    DateTime CreatedAt
);

/// <summary>
/// Source Generator [DbResult] ?ŒìŠ¤?¸ìš© DTO (Partial ?„ìˆ˜)
/// </summary>
[DbResult]
public partial class DbResultUser
{
    public int UserId { get; init; }
    public string UserName { get; init; } = "";
    public string Email { get; init; } = "";
    public int? Age { get; init; }
    // Source Generator ?¸í™˜??(CS0117 ?´ê²°)
    public string? Name { get; init; }
    public int? Val { get; init; }
}

// AdvancedQueryTests?ì„œ ?¬ìš©
public class ResumableLogDto
{
    public DateTime CreatedAt { get; set; }
    public string Message { get; set; } = "";
}

