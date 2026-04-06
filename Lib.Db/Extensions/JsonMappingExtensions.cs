// ============================================================================
// 파일: Lib.Db/Extensions/JsonMappingExtensions.cs
// 설명: JSON 컬럼 자동 매핑을 위한 확장 메서드
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Lib.Db.Contracts.Core;

namespace Lib.Db.Extensions;

#region JSON 매핑 확장 메서드

/// <summary>
/// DB 조회 결과에서 JSON 컬럼을 C# 타입으로 역직렬화하는 확장 메서드 모음입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>Source Generator 비침범</b>: 기존 매퍼 파이프라인을 수정하지 않고, 결과 후처리로 JSON 매핑을 지원합니다.<br/>
/// - <b>Dictionary 결과 호환</b>: <c>QueryAsync&lt;Dictionary&lt;string, object?&gt;&gt;</c> 결과와 자연스럽게 연동됩니다.<br/>
/// - <b>DTO 결과 호환</b>: string 속성으로 조회한 JSON 문자열을 타입 안전하게 변환합니다.<br/>
/// - <b>LibDbOptions.JsonOptions 연동</b>: 전역 JSON 직렬화 옵션을 지원합니다.
/// </para>
/// </summary>
public static class JsonMappingExtensions
{
    #region Dictionary 기반 JSON 매핑

    /// <summary>
    /// Dictionary 결과에서 JSON 컬럼을 지정 타입으로 역직렬화합니다.
    /// </summary>
    /// <typeparam name="T">역직렬화 대상 타입</typeparam>
    /// <param name="row">DB 조회 결과 행 (Dictionary)</param>
    /// <param name="columnName">JSON 데이터가 저장된 컬럼 이름</param>
    /// <param name="options">JSON 직렬화 옵션 (null 시 기본 Web 옵션 사용). DI 환경에서는 LibDbOptions.JsonOptions를 전달하세요.</param>
    /// <returns>역직렬화된 객체, JSON이 없거나 null이면 default(T)</returns>
    [RequiresUnreferencedCode("JSON 직렬화는 AOT 환경에서 지원되지 않습니다.")]
    public static T? MapJsonColumn<T>(
        this Dictionary<string, object?> row,
        string columnName,
        JsonSerializerOptions? options = null)
    {
        if (!row.TryGetValue(columnName, out object? value) || value is not string json)
            return default;

        if (string.IsNullOrWhiteSpace(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, options ?? JsonDefaults.WebOptions);
    }

    /// <summary>
    /// Dictionary 결과에서 JSON 컬럼을 지정 타입으로 역직렬화합니다 (비동기 스트림 확장).
    /// </summary>
    /// <typeparam name="T">역직렬화 대상 타입</typeparam>
    /// <param name="rows">DB 조회 결과 행 스트림</param>
    /// <param name="columnName">JSON 데이터가 저장된 컬럼 이름</param>
    /// <param name="options">JSON 직렬화 옵션. DI 환경에서는 LibDbOptions.JsonOptions를 전달하세요.</param>
    /// <returns>역직렬화된 (행, JSON 객체) 튜플의 비동기 스트림</returns>
    [RequiresUnreferencedCode("JSON 직렬화는 AOT 환경에서 지원되지 않습니다.")]
    public static async IAsyncEnumerable<(Dictionary<string, object?> Row, T? Json)>
        WithJsonColumnAsync<T>(
            this IAsyncEnumerable<Dictionary<string, object?>> rows,
            string columnName,
            JsonSerializerOptions? options = null)
    {
        await foreach (Dictionary<string, object?> row in rows)
        {
            T? jsonValue = row.MapJsonColumn<T>(columnName, options);
            yield return (row, jsonValue);
        }
    }

    #endregion

    #region 문자열 기반 JSON 역직렬화

    /// <summary>
    /// JSON 문자열을 지정 타입으로 역직렬화합니다.
    /// </summary>
    /// <typeparam name="T">역직렬화 대상 타입</typeparam>
    /// <param name="json">JSON 문자열</param>
    /// <param name="options">JSON 직렬화 옵션 (null 시 기본 Web 옵션 사용). DI 환경에서는 LibDbOptions.JsonOptions를 전달하세요.</param>
    /// <returns>역직렬화된 객체, null 또는 빈 문자열이면 default(T)</returns>
    [RequiresUnreferencedCode("JSON 직렬화는 AOT 환경에서 지원되지 않습니다.")]
    public static T? FromJson<T>(this string? json, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, options ?? JsonDefaults.WebOptions);
    }

    /// <summary>
    /// 객체를 JSON 문자열로 직렬화합니다.
    /// </summary>
    /// <typeparam name="T">직렬화 대상 타입</typeparam>
    /// <param name="value">직렬화할 객체</param>
    /// <param name="options">JSON 직렬화 옵션 (null 시 기본 Web 옵션 사용). DI 환경에서는 LibDbOptions.JsonOptions를 전달하세요.</param>
    /// <returns>JSON 문자열</returns>
    [RequiresUnreferencedCode("JSON 직렬화는 AOT 환경에서 지원되지 않습니다.")]
    public static string ToJson<T>(this T value, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(value, options ?? JsonDefaults.WebOptions);
    }

    #endregion

    #region 기본 JsonSerializerOptions

    /// <summary>
    /// JSON 기본 옵션을 제공하는 정적 헬퍼입니다.
    /// </summary>
    private static class JsonDefaults
    {
        /// <summary>
        /// Web 호환 기본 옵션 (camelCase, 대소문자 무관 역직렬화).
        /// </summary>
        public static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    }

    #endregion
}

#endregion
