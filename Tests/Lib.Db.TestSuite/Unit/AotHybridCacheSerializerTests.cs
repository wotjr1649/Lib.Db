using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lib.Db.Configuration;
using Xunit;

namespace Lib.Db.Verification.Tests.Unit;

[Trait("Category", "Unit")]
public class AotHybridCacheSerializerTests
{
    // Simple POCO for testing
    public record TestDto(int Id, string Name);

    private readonly JsonTypeInfo<TestDto> _typeInfo;
    private readonly AotHybridCacheSerializer<TestDto> _serializer;

    public AotHybridCacheSerializerTests()
    {
        // Use standard reflection-based resolver for test (compatible with Aot serializer contract)
        // In real AOT, this would come from Source Generator context.
        var options = new JsonSerializerOptions 
        { 
            TypeInfoResolver = new DefaultJsonTypeInfoResolver() 
        };
        _typeInfo = (JsonTypeInfo<TestDto>)options.GetTypeInfo(typeof(TestDto));
        _serializer = new AotHybridCacheSerializer<TestDto>(_typeInfo);
    }

    [Fact]
    public void Serialize_ShouldWriteCorrectJson()
    {
        // Arrange
        var dto = new TestDto(1, "Test");
        var bufferWriter = new ArrayBufferWriter<byte>();

        // Act
        _serializer.Serialize(dto, bufferWriter);

        // Assert
        var json = Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
        Assert.Contains("\"Id\":1", json);
        Assert.Contains("\"Name\":\"Test\"", json);
    }

    [Fact]
    public void Deserialize_ShouldReadCorrectJson()
    {
        // Arrange
        var json = "{\"Id\":2,\"Name\":\"Restore\"}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var sequence = new ReadOnlySequence<byte>(bytes);

        // Act
        var result = _serializer.Deserialize(sequence);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Id);
        Assert.Equal("Restore", result.Name);
    }

    [Fact]
    public void RoundTrip_ShouldPreserveData()
    {
        // Arrange
        var original = new TestDto(99, "RoundTrip");
        var bufferWriter = new ArrayBufferWriter<byte>();

        // Act (Serialize)
        _serializer.Serialize(original, bufferWriter);

        // Act (Deserialize)
        var sequence = new ReadOnlySequence<byte>(bufferWriter.WrittenMemory);
        var restored = _serializer.Deserialize(sequence);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Deserialize_MultiSegmentSequence_ShouldWork()
    {
        // Arrange (Simulate fragmented sequence, typical in Pipelines)
        var part1 = Encoding.UTF8.GetBytes("{\"Id\":3");
        var part2 = Encoding.UTF8.GetBytes(",\"Name\":\"Split\"}");
        
        var firstSegment = new BufferSegment(part1);
        var secondSegment = firstSegment.Append(part2);
        
        var sequence = new ReadOnlySequence<byte>(firstSegment, 0, secondSegment, part2.Length);

        // Act
        var result = _serializer.Deserialize(sequence);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Id);
        Assert.Equal("Split", result.Name);
    }

    // Helper class for creating ReadOnlySequence segments
    private class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = next;
            return next;
        }
    }
}
