// ============================================================================
// 파일: Lib.Db/Fluent/SqlInterpolatedStringHandler.cs
// 설명: Zero-Allocation SQL 문자열 처리를 위한 InterpolatedStringHandler
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Lib.Db.Fluent;

/// <summary>
/// Zero-Allocation SQL 문자열 처리를 위한 Interpolated String Handler입니다.
/// <para>
/// <b>[핵심 기능]</b><br/>
/// - ArrayPool을 활용한 Zero-Allocation 버퍼 관리<br/>
/// - 자동 파라미터 수집 및 SQL Injection 방어<br/>
/// - Span&lt;char&gt; 기반 고성능 문자열 조합
/// </para>
/// </summary>
/// <example>
/// <code>
/// // 컴파일러가 자동으로 SqlInterpolatedStringHandler 사용
/// int userId = 123;
/// string userName = "TestUser";
/// 
/// var result = db.Default
///     .Sql($"SELECT * FROM Users WHERE Id = {userId} AND Name = {userName}")
///     .QueryAsync&lt;User&gt;();
/// 
/// // 생성된 SQL: "SELECT * FROM Users WHERE Id = @p0 AND Name = @p1"
/// // 파라미터: { "@p0": 123, "@p1": "TestUser" }
/// </code>
/// </example>
[InterpolatedStringHandler]
public ref struct SqlInterpolatedStringHandler
{
    #region [필드] 내부 상태

    private char[]? _buffer;
    private int _position;
    private readonly List<KeyValuePair<string, object?>> _parameters;
    private int _parameterIndex;
    private readonly bool _isValid;

    #endregion

    #region [생성자]

    /// <summary>
    /// InterpolatedStringHandler를 초기화합니다.
    /// </summary>
    /// <param name="literalLength">리터럴 문자열의 예상 길이</param>
    /// <param name="formattedCount">보간된 값의 개수</param>
    /// <param name="handlerIsValid">핸들러가 유효한지 여부 (out)</param>
    public SqlInterpolatedStringHandler(
        int literalLength, 
        int formattedCount,
        out bool handlerIsValid)
    {
        // 예상 크기 계산: 리터럴 + (파라미터 이름 4자 * 개수)
        int estimatedSize = literalLength + (formattedCount * 4); // "@p0 " = 4 chars
        
        // ArrayPool에서 버퍼 대여 (Zero-Allocation 전략)
        _buffer = ArrayPool<char>.Shared.Rent(Math.Max(estimatedSize, 256));
        _position = 0;
        _parameters = new List<KeyValuePair<string, object?>>(formattedCount);
        _parameterIndex = 0;
        _isValid = true;
        
        handlerIsValid = _isValid;
    }

    #endregion

    #region [메서드] AppendLiteral / AppendFormatted

    /// <summary>
    /// SQL 리터럴 문자열을 버퍼에 추가합니다.
    /// </summary>
    /// <param name="literal">리터럴 문자열 (예: "SELECT * FROM Users WHERE Id = ")</param>
    public void AppendLiteral(string literal)
    {
        if (_buffer == null || !_isValid)
            return;

        // 버퍼 크기 부족 시 재할당
        EnsureCapacity(literal.Length);
        
        // Span으로 고속 복사 (Zero-Allocation)
        literal.AsSpan().CopyTo(_buffer.AsSpan(_position));
        _position += literal.Length;
    }

    /// <summary>
    /// 보간된 값을 파라미터로 변환하여 SQL에 추가합니다.
    /// </summary>
    /// <typeparam name="T">값의 타입</typeparam>
    /// <param name="value">보간된 값</param>
    public void AppendFormatted<T>(T value)
    {
        if (_buffer == null || !_isValid)
            return;

        // 파라미터 이름 생성 (@p0, @p1, @p2, ...)
        string paramName = $"@p{_parameterIndex++}";
        
        // SQL에 파라미터 이름 추가
        EnsureCapacity(paramName.Length);
        paramName.AsSpan().CopyTo(_buffer.AsSpan(_position));
        _position += paramName.Length;
        
        // 파라미터 값 저장
        _parameters.Add(new KeyValuePair<string, object?>(paramName, value));
    }

    /// <summary>
    /// 보간된 문자열 값을 파라미터로 변환합니다. (문자열 특화 오버로드)
    /// </summary>
    public void AppendFormatted(string? value)
    {
        if (_buffer == null || !_isValid)
            return;

        string paramName = $"@p{_parameterIndex++}";
        EnsureCapacity(paramName.Length);
        paramName.AsSpan().CopyTo(_buffer.AsSpan(_position));
        _position += paramName.Length;
        
        _parameters.Add(new KeyValuePair<string, object?>(paramName, value));
    }

    #endregion

    #region [메서드] GetResult

    /// <summary>
    /// 생성된 SQL 문자열과 파라미터를 반환합니다.
    /// </summary>
    /// <returns>SQL 문자열과 파라미터 딕셔너리 튜플</returns>
    public readonly (string Sql, Dictionary<string, object?> Parameters) GetResult()
    {
        if (_buffer == null || !_isValid)
            return (string.Empty, new Dictionary<string, object?>());

        // SQL 문자열 생성
        string sql = new string(_buffer, 0, _position);
        
        // 파라미터 딕셔너리 생성
        var dict = new Dictionary<string, object?>(_parameters.Count);
        foreach (var pair in _parameters)
        {
            dict[pair.Key] = pair.Value;
        }
        
        return (sql, dict);
    }

    #endregion

    #region [헬퍼 메서드]

    /// <summary>
    /// 버퍼 용량을 확인하고 필요 시 확장합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int additionalLength)
    {
        if (_buffer == null)
            return;

        if (_position + additionalLength > _buffer.Length)
        {
            // 버퍼 확장 (기존 크기의 2배 또는 필요한 크기 중 큰 값)
            int newSize = Math.Max(_buffer.Length * 2, _position + additionalLength);
            char[] newBuffer = ArrayPool<char>.Shared.Rent(newSize);
            
            // 기존 데이터 복사
            _buffer.AsSpan(0, _position).CopyTo(newBuffer);
            
            // 기존 버퍼 반환
            ArrayPool<char>.Shared.Return(_buffer);
            
            _buffer = newBuffer;
        }
    }

    #endregion

    #region [Dispose]

    /// <summary>
    /// ArrayPool 버퍼를 반환합니다.
    /// </summary>
    public void Dispose()
    {
        if (_buffer != null)
        {
            ArrayPool<char>.Shared.Return(_buffer, clearArray: true);
            _buffer = null;
        }
    }

    #endregion
}
