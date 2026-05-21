// ============================================================================
// 파일: Lib.Db/Configuration/LibDbJsonContext.cs
// 설명: AOT용 System.Text.Json 소스 생성 컨텍스트 — Lib.Db 전용 JSON 직렬화 메타데이터
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Models;

// [아키텍처 면제] System.Text.Json Source Generator는 block-scoped namespace + partial class를 요구합니다.
// file-scoped namespace(namespace X;) 및 sealed 클래스 적용이 불가합니다.
// 이는 .NET Source Generator의 구조적 제약이며, 코드 품질 위반이 아닙니다.
namespace Lib.Db.Configuration
{
    [JsonSerializable(typeof(LibDbOptions))]
    [JsonSerializable(typeof(SpSchema))]
    [JsonSerializable(typeof(TvpSchema))]
    [JsonSourceGenerationOptions(WriteIndented = true,
        IgnoreReadOnlyProperties = false,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [ExcludeFromCodeCoverage(Justification = "System.Text.Json source generation metadata is exercised through serialization tests, not line coverage.")]
    internal partial class LibDbJsonContext : JsonSerializerContext
    {
    }
}
