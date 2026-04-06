// ============================================================================
// 파일: Unit/AotHybridCacheSerializerTests.cs
// 설명: AotHybridCacheSerializer 직렬화/역직렬화 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lib.Db.Configuration;

namespace Lib.Db.IntegrationTests.Unit;

[Trait("Category", "Unit")]
public sealed class AotHybridCacheSerializerTests
{
    public record TestDto(int Id, string Name);

    private readonly JsonTypeInfo<TestDto> _typeInfo;
    private readonly AotHybridCacheSerializer<TestDto> _serializer;

    public AotHybridCacheSerializerTests()
    {
        JsonSerializerOptions options = new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        _typeInfo = (JsonTypeInfo<TestDto>)options.GetTypeInfo(typeof(TestDto));
        _serializer = new AotHybridCacheSerializer<TestDto>(_typeInfo);
    }

    [Fact]
    public void Serialize_ShouldWriteCorrectJson()
    {
        TestDto dto = new(1, "Test");
        ArrayBufferWriter<byte> bufferWriter = new();

        _serializer.Serialize(dto, bufferWriter);

        string json = Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
        Assert.Contains("\"Id\":1", json);
        Assert.Contains("\"Name\":\"Test\"", json);
    }

    [Fact]
    public void Deserialize_ShouldReadCorrectJson()
    {
        string json = "{\"Id\":2,\"Name\":\"Restore\"}";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        ReadOnlySequence<byte> sequence = new(bytes);

        TestDto? result = _serializer.Deserialize(sequence);

        Assert.NotNull(result);
        Assert.Equal(2, result.Id);
        Assert.Equal("Restore", result.Name);
    }

    [Fact]
    public void RoundTrip_ShouldPreserveData()
    {
        TestDto original = new(99, "RoundTrip");
        ArrayBufferWriter<byte> bufferWriter = new();

        _serializer.Serialize(original, bufferWriter);

        ReadOnlySequence<byte> sequence = new(bufferWriter.WrittenMemory);
        TestDto? restored = _serializer.Deserialize(sequence);

        Assert.NotNull(restored);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Deserialize_MultiSegmentSequence_ShouldWork()
    {
        byte[] part1 = Encoding.UTF8.GetBytes("{\"Id\":3");
        byte[] part2 = Encoding.UTF8.GetBytes(",\"Name\":\"Split\"}");

        BufferSegment firstSegment = new(part1);
        BufferSegment secondSegment = firstSegment.Append(part2);

        ReadOnlySequence<byte> sequence = new(firstSegment, 0, secondSegment, part2.Length);

        TestDto? result = _serializer.Deserialize(sequence);

        Assert.NotNull(result);
        Assert.Equal(3, result.Id);
        Assert.Equal("Split", result.Name);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            BufferSegment next = new(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = next;
            return next;
        }
    }
}
