// ============================================================================
// 파일: Execution/Tvp/RuntimeTvpRowShape.cs
// 설명: 런타임 TVP fast-path에서 재사용할 row shape 캐시 항목
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;

namespace Lib.Db.Execution.Tvp;

internal sealed record RuntimeTvpRowShape(
    Type RowType,
    TvpColumnShape[] Columns,
    Func<object, object?>[] Accessors,
    IReadOnlyDictionary<string, int> Ordinals,
    DataTable SchemaTable);
