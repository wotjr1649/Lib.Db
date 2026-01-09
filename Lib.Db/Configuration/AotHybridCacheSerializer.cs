using System;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Lib.Db.Configuration;

/// <summary>
/// AOT(Ahead-of-Time) 컴파일 환경을 지원하는 리플렉션 없는 HybridCache용 직렬화기입니다.
/// </summary>
/// <typeparam name="T">직렬화 및 역직렬화할 데이터의 타입입니다.</typeparam>
/// <remarks>
/// <para>
/// 이 클래스는 <see cref="IHybridCacheSerializer{T}"/>를 구현하며, 
/// <see cref="JsonTypeInfo{T}"/>를 사용하여 런타임 리플렉션 없이 고성능 직렬화를 수행합니다.
/// </para>
/// <para>
/// <strong>주요 특징:</strong>
/// <list type="bullet">
///     <item>
///         <description><strong>Native AOT 호환:</strong> 리플렉션을 사용하지 않으므로 Trimming 및 Native AOT 빌드에서 안전합니다.</description>
///     </item>
///     <item>
///         <description><strong>Zero-Copy 지향:</strong> <see cref="ReadOnlySequence{Byte}"/> 및 <see cref="IBufferWriter{Byte}"/>를 직접 활용하여 메모리 할당을 최소화합니다.</description>
///     </item>
/// </list>
/// </para>
/// <para>
/// <strong>주의:</strong> 사용하기 전에 해당 타입 <typeparamref name="T"/>가 소스 생성기(Source Generator) 컨텍스트에 등록되어 있어야 합니다.
/// </para>
/// </remarks>
internal sealed class AotHybridCacheSerializer<T> : IHybridCacheSerializer<T>
{
    /// <summary>
    /// 컴파일 타임에 생성된 타입 메타데이터입니다.
    /// </summary>
    private readonly JsonTypeInfo<T> _typeInfo;

    /// <summary>
    /// <see cref="AotHybridCacheSerializer{T}"/> 클래스의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="typeInfo">
    /// System.Text.Json 소스 생성기를 통해 제공된 <typeparamref name="T"/>의 메타데이터입니다.
    /// <br/>
    /// 예: <c>MyJsonContext.Default.MyType</c>
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/>가 null인 경우 발생합니다.</exception>
    public AotHybridCacheSerializer(JsonTypeInfo<T> typeInfo)
    {
        _typeInfo = typeInfo ?? throw new ArgumentNullException(nameof(typeInfo));
    }

    /// <summary>
    /// 바이트 시퀀스에서 객체를 역직렬화합니다.
    /// </summary>
    /// <param name="source">읽을 데이터가 담긴 읽기 전용 바이트 시퀀스입니다.</param>
    /// <returns>역직렬화된 <typeparamref name="T"/> 타입의 객체입니다.</returns>
    /// <remarks>
    /// <see cref="Utf8JsonReader"/>를 사용하여 <see cref="ReadOnlySequence{Byte}"/>를 효율적으로 처리합니다.
    /// 데이터가 불연속적인 메모리 세그먼트에 나뉘어 있어도 별도의 병합(Coalescing) 과정 없이 처리됩니다.
    /// </remarks>
    /// <exception cref="JsonException">JSON 형식이 유효하지 않거나 타입과 일치하지 않을 때 발생합니다.</exception>
    public T Deserialize(ReadOnlySequence<byte> source)
    {
        // Utf8JsonReader는 ReadOnlySequence를 생성자에서 직접 지원하므로,
        // 별도의 Span 변환이나 배열 복사 없이 고성능 파싱이 가능합니다.
        var reader = new Utf8JsonReader(source);

        // ! 연산자 사용: JSON이 "null" 토큰일 경우 null이 반환될 수 있으나,
        // 일반적인 캐시 히트 시나리오에서는 유효한 객체를 가정합니다.
        // T가 값 타입이거나 nullable이 아닌 경우에도 JsonSerializer는 null을 리턴할 수 있으므로 주의가 필요합니다.
        return JsonSerializer.Deserialize(ref reader, _typeInfo)!;
    }

    /// <summary>
    /// 객체를 지정된 버퍼 작성기에 직렬화합니다.
    /// </summary>
    /// <param name="value">직렬화할 객체입니다.</param>
    /// <param name="target">직렬화된 데이터가 쓰일 대상 버퍼 작성기입니다.</param>
    /// <remarks>
    /// <see cref="Utf8JsonWriter"/>를 사용하여 <paramref name="target"/>에 직접 UTF-8 바이트를 씁니다.
    /// 중간 버퍼나 문자열 할당을 발생시키지 않습니다.
    /// </remarks>
    public void Serialize(T value, IBufferWriter<byte> target)
    {
        // IBufferWriter<byte>를 직접 사용하는 Utf8JsonWriter를 생성하여
        // 파이프라인(PipeWriter) 등에 직접 쓰기를 수행합니다.
        using var writer = new Utf8JsonWriter(target);

        JsonSerializer.Serialize(writer, value, _typeInfo);

        // Utf8JsonWriter는 Dispose 시점에 자동으로 Flush를 수행하지만,
        // 명시적인 제어를 위해 필요한 경우 writer.Flush()를 호출할 수 있습니다.
        // 여기서는 using 블록에 의해 자동으로 처리됩니다.
    }
}