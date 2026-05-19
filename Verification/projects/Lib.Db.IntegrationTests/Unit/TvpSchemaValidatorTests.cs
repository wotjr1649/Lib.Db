// ============================================================================
// 파일: Unit/TvpSchemaValidatorTests.cs
// 설명: TVP 스키마 검증기 회귀 테스트
// ============================================================================

using System.Collections.Frozen;
using System.Data;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.Execution.Tvp;
using Lib.Db.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class TvpSchemaValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldUseStaticValidator_WhenGeneratedValidatorIsAvailable()
    {
        TvpSchema schema = new()
        {
            Name = "dbo.Sample",
            VersionToken = 1,
            Columns =
            [
                new TvpColumnMetadata(
                    Name: nameof(StaticValidatorRow.Id),
                    NameHash: TvpNameHash.Compute(nameof(StaticValidatorRow.Id)),
                    MaxLength: 4,
                    Ordinal: 0,
                    SqlDbType: SqlDbType.Int,
                    Precision: 0,
                    Scale: 0,
                    IsIdentity: false,
                    IsComputed: false,
                    IsNullable: false)
            ]
        };
        RecordingStaticValidator<StaticValidatorRow> staticValidator = new();
        var idProperty = typeof(StaticValidatorRow).GetProperty(nameof(StaticValidatorRow.Id))!;
        TvpAccessors<StaticValidatorRow> accessors = new()
        {
            Properties = [idProperty],
            Accessors = [static row => ((StaticValidatorRow)row).Id],
            TypedAccessors = [static row => row.Id],
            OrdinalMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(StaticValidatorRow.Id)] = 0
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            SchemaTable = TvpAccessors.BuildSchemaTable([idProperty]),
            StaticValidator = staticValidator
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        await validator.ValidateAsync("dbo.Sample", accessors, "TestInst", TestContext.Current.CancellationToken);

        staticValidator.CallCount.Should().Be(1);
        accessors.IsValidated.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldValidateRuntimeSupportedSqlTypeGroups()
    {
        TvpAccessors<RuntimeSupportedTypeRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeSupportedTypeRow>();
        TvpSchema schema = new()
        {
            Name = "dbo.RuntimeSupportedTypes",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(property.Name, ordinal, ExpectedSqlType(property.Name)))
                .ToArray()
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        await validator.ValidateAsync(
            "dbo.RuntimeSupportedTypes",
            accessors,
            "TestInst",
            TestContext.Current.CancellationToken);

        accessors.IsValidated.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldValidateRuntimeEnumUnderlyingSqlType()
    {
        TvpAccessors<RuntimeEnumRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeEnumRow>();
        TvpSchema schema = new()
        {
            Name = "dbo.RuntimeEnum",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(
                    property.Name,
                    ordinal,
                    SqlDbType.TinyInt,
                    isNullable: Nullable.GetUnderlyingType(property.PropertyType) is not null))
                .ToArray()
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        await validator.ValidateAsync(
            "dbo.RuntimeEnum",
            accessors,
            "TestInst",
            TestContext.Current.CancellationToken);

        accessors.IsValidated.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectRuntimeEnumSqlTypeMismatch()
    {
        TvpAccessors<RuntimeEnumRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeEnumRow>();
        TvpSchema schema = new()
        {
            Name = "dbo.RuntimeEnum",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(
                    property.Name,
                    ordinal,
                    property.Name == nameof(RuntimeEnumRow.Status)
                        ? SqlDbType.Int
                        : SqlDbType.TinyInt,
                    isNullable: Nullable.GetUnderlyingType(property.PropertyType) is not null))
                .ToArray()
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        TvpSchemaValidationException ex = await Assert.ThrowsAsync<TvpSchemaValidationException>(
            () => validator.ValidateAsync(
                "dbo.RuntimeEnum",
                accessors,
                "TestInst",
                TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(SchemaConstants.ErrTypeMismatch);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRevalidateCachedAccessorsForDifferentTvpSchema()
    {
        TvpAccessors<RuntimeEnumRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeEnumRow>();
        accessors.IsValidated = false;

        TvpSchema validSchema = new()
        {
            Name = "dbo.RuntimeEnum",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(
                    property.Name,
                    ordinal,
                    SqlDbType.TinyInt,
                    isNullable: Nullable.GetUnderlyingType(property.PropertyType) is not null))
                .ToArray()
        };
        TvpSchema invalidSchema = new()
        {
            Name = "dbo.RuntimeEnumMismatch",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(
                    property.Name,
                    ordinal,
                    property.Name == nameof(RuntimeEnumRow.Status)
                        ? SqlDbType.Int
                        : SqlDbType.TinyInt,
                    isNullable: Nullable.GetUnderlyingType(property.PropertyType) is not null))
                .ToArray()
        };

        TvpSchemaValidator validValidator = new(
            new FakeSchemaService(validSchema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);
        TvpSchemaValidator invalidValidator = new(
            new FakeSchemaService(invalidSchema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        await validValidator.ValidateAsync(
            "dbo.RuntimeEnum",
            accessors,
            "TestInst",
            TestContext.Current.CancellationToken);

        TvpSchemaValidationException ex = await Assert.ThrowsAsync<TvpSchemaValidationException>(
            () => invalidValidator.ValidateAsync(
                "dbo.RuntimeEnumMismatch",
                accessors,
                "TestInst",
                TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(SchemaConstants.ErrTypeMismatch);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectRuntimeNameMismatchWhenNameHashCollides()
    {
        TvpAccessors<RuntimeIdentityRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeIdentityRow>();
        accessors.IsValidated = false;

        TvpSchema schema = new()
        {
            Name = "dbo.RuntimeIdentity",
            VersionToken = 1,
            Columns =
            [
                new TvpColumnMetadata(
                    Name: "DifferentId",
                    NameHash: TvpNameHash.Compute(nameof(RuntimeIdentityRow.Id)),
                    MaxLength: 4,
                    Ordinal: 0,
                    SqlDbType: SqlDbType.Int,
                    Precision: 0,
                    Scale: 0,
                    IsIdentity: false,
                    IsComputed: false,
                    IsNullable: false)
            ]
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        TvpSchemaValidationException ex = await Assert.ThrowsAsync<TvpSchemaValidationException>(
            () => validator.ValidateAsync(
                "dbo.RuntimeIdentity",
                accessors,
                "TestInst",
                TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(SchemaConstants.ErrNameMismatch);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRevalidateCachedAccessorsWhenSchemaFingerprintChangesForSameTvp()
    {
        TvpAccessors<RuntimeEnumRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeEnumRow>();
        accessors.IsValidated = false;

        TvpSchema validSchema = new()
        {
            Name = "dbo.RuntimeEnum",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(
                    property.Name,
                    ordinal,
                    SqlDbType.TinyInt,
                    isNullable: Nullable.GetUnderlyingType(property.PropertyType) is not null))
                .ToArray()
        };
        TvpSchema invalidSchema = new()
        {
            Name = "dbo.RuntimeEnum",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(
                    property.Name,
                    ordinal,
                    property.Name == nameof(RuntimeEnumRow.Status)
                        ? SqlDbType.Int
                        : SqlDbType.TinyInt,
                    isNullable: Nullable.GetUnderlyingType(property.PropertyType) is not null))
                .ToArray()
        };
        TvpSchemaValidator validator = new(
            new SequenceSchemaService(validSchema, invalidSchema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        await validator.ValidateAsync(
            "dbo.RuntimeEnum",
            accessors,
            "TestInst",
            TestContext.Current.CancellationToken);

        TvpSchemaValidationException ex = await Assert.ThrowsAsync<TvpSchemaValidationException>(
            () => validator.ValidateAsync(
                "dbo.RuntimeEnum",
                accessors,
                "TestInst",
                TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(SchemaConstants.ErrTypeMismatch);
    }

    [Fact]
    public async Task ValidateAsync_ShouldSkipStaticValidatorForIdenticalSchemaIdentity()
    {
        TvpSchema schema = new()
        {
            Name = "dbo.Sample",
            VersionToken = 1,
            Columns =
            [
                new TvpColumnMetadata(
                    Name: nameof(StaticValidatorRow.Id),
                    NameHash: TvpNameHash.Compute(nameof(StaticValidatorRow.Id)),
                    MaxLength: 4,
                    Ordinal: 0,
                    SqlDbType: SqlDbType.Int,
                    Precision: 0,
                    Scale: 0,
                    IsIdentity: false,
                    IsComputed: false,
                    IsNullable: false)
            ]
        };
        RecordingStaticValidator<StaticValidatorRow> staticValidator = new();
        var idProperty = typeof(StaticValidatorRow).GetProperty(nameof(StaticValidatorRow.Id))!;
        TvpAccessors<StaticValidatorRow> accessors = new()
        {
            Properties = [idProperty],
            Accessors = [static row => ((StaticValidatorRow)row).Id],
            TypedAccessors = [static row => row.Id],
            OrdinalMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(StaticValidatorRow.Id)] = 0
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            SchemaTable = TvpAccessors.BuildSchemaTable([idProperty]),
            StaticValidator = staticValidator
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        await validator.ValidateAsync("dbo.Sample", accessors, "TestInst", TestContext.Current.CancellationToken);
        await validator.ValidateAsync("dbo.Sample", accessors, "TestInst", TestContext.Current.CancellationToken);

        staticValidator.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ValidateAsync_ShouldRejectRuntimeIdentityOrComputedColumns(
        bool isIdentity,
        bool isComputed)
    {
        TvpAccessors<RuntimeIdentityRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeIdentityRow>();
        TvpSchema schema = new()
        {
            Name = "dbo.RuntimeIdentity",
            VersionToken = 1,
            Columns =
            [
                Column(
                    nameof(RuntimeIdentityRow.Id),
                    0,
                    SqlDbType.Int,
                    isIdentity: isIdentity,
                    isComputed: isComputed)
            ]
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        TvpSchemaValidationException ex = await Assert.ThrowsAsync<TvpSchemaValidationException>(
            () => validator.ValidateAsync(
                "dbo.RuntimeIdentity",
                accessors,
                "TestInst",
                TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(SchemaConstants.ErrIdentityComputed);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectRuntimeTypeMismatchInVectorizedBatch()
    {
        if (!System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            return;
        }

        TvpAccessors<RuntimeVectorizedRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeVectorizedRow>();
        TvpSchema schema = new()
        {
            Name = "dbo.RuntimeVectorized",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(
                    property.Name,
                    ordinal,
                    ordinal == 0 ? SqlDbType.BigInt : SqlDbType.Int))
                .ToArray()
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        TvpSchemaValidationException ex = await Assert.ThrowsAsync<TvpSchemaValidationException>(
            () => validator.ValidateAsync(
                "dbo.RuntimeVectorized",
                accessors,
                "TestInst",
                TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(SchemaConstants.ErrTypeMismatch);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ValidateAsync_ShouldRejectRuntimeWriteBlockedColumnInVectorizedBatch(
        bool isIdentity,
        bool isComputed)
    {
        if (!System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            return;
        }

        TvpAccessors<RuntimeVectorizedRow> accessors =
            TvpAccessorCache.GetTypedAccessors<RuntimeVectorizedRow>();
        TvpSchema schema = new()
        {
            Name = "dbo.RuntimeVectorized",
            VersionToken = 1,
            Columns = accessors.Properties
                .Select((property, ordinal) => Column(
                    property.Name,
                    ordinal,
                    SqlDbType.Int,
                    isIdentity: ordinal == 0 && isIdentity,
                    isComputed: ordinal == 0 && isComputed))
                .ToArray()
        };
        TvpSchemaValidator validator = new(
            new FakeSchemaService(schema),
            new LibDbOptions { TvpValidationMode = TvpValidationMode.Strict },
            NullLogger<TvpSchemaValidator>.Instance);

        TvpSchemaValidationException ex = await Assert.ThrowsAsync<TvpSchemaValidationException>(
            () => validator.ValidateAsync(
                "dbo.RuntimeVectorized",
                accessors,
                "TestInst",
                TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(SchemaConstants.ErrIdentityComputed);
    }

    private sealed class StaticValidatorRow
    {
        public int Id { get; set; }
    }

    private sealed class RuntimeSupportedTypeRow
    {
        public byte Flag { get; set; }

        public short SmallCode { get; set; }

        public DateOnly EffectiveDate { get; set; }

        public TimeOnly EffectiveTime { get; set; }

        public Half Ratio { get; set; }
    }

    private enum ByteStatus : byte
    {
        None = 0,
        Active = 1
    }

    private sealed class RuntimeEnumRow
    {
        public ByteStatus Status { get; set; }

        public ByteStatus? OptionalStatus { get; set; }
    }

    private sealed class RuntimeIdentityRow
    {
        public int Id { get; set; }
    }

    private sealed class RuntimeVectorizedRow
    {
        public int C1 { get; set; }

        public int C2 { get; set; }

        public int C3 { get; set; }

        public int C4 { get; set; }

        public int C5 { get; set; }

        public int C6 { get; set; }

        public int C7 { get; set; }

        public int C8 { get; set; }
    }

    private static TvpColumnMetadata Column(
        string name,
        int ordinal,
        SqlDbType sqlDbType,
        bool isIdentity = false,
        bool isComputed = false,
        bool isNullable = false)
        => new(
            Name: name,
            NameHash: TvpNameHash.Compute(name),
            MaxLength: 0,
            Ordinal: ordinal,
            SqlDbType: sqlDbType,
            Precision: 0,
            Scale: 0,
            IsIdentity: isIdentity,
            IsComputed: isComputed,
            IsNullable: isNullable);

    private static SqlDbType ExpectedSqlType(string propertyName)
        => propertyName switch
        {
            nameof(RuntimeSupportedTypeRow.Flag) => SqlDbType.TinyInt,
            nameof(RuntimeSupportedTypeRow.SmallCode) => SqlDbType.SmallInt,
            nameof(RuntimeSupportedTypeRow.EffectiveDate) => SqlDbType.Date,
            nameof(RuntimeSupportedTypeRow.EffectiveTime) => SqlDbType.Time,
            nameof(RuntimeSupportedTypeRow.Ratio) => SqlDbType.Real,
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
        };

    private sealed class RecordingStaticValidator<T> : ITvpStaticValidator<T>
    {
        public int CallCount { get; private set; }

        public void ValidateStatic(TvpSchema schema)
        {
            CallCount++;
            schema.Name.Should().Be("dbo.Sample");
        }
    }

    private sealed class FakeSchemaService(TvpSchema schema) : ISchemaService
    {
        public Task<PreloadResult> PreloadSchemaAsync(
            IEnumerable<string> schemaNames,
            string instanceHash,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SpSchema> GetSpSchemaAsync(
            string spName,
            string instanceHash,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<TvpSchema> GetTvpSchemaAsync(
            string tvpName,
            string instanceHash,
            CancellationToken ct) =>
            Task.FromResult(schema);

        public Task FlushSchemaAsync(string instanceHash, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task FlushTvpAsync(string tvpName, string instanceHash, CancellationToken ct) =>
            throw new NotSupportedException();

        public void InvalidateSpSchema(string spName, string instanceHash) =>
            throw new NotSupportedException();

        public void InvalidateTvpSchema(string tvpName, string instanceHash) =>
            throw new NotSupportedException();
    }

    private sealed class SequenceSchemaService(params TvpSchema[] schemas) : ISchemaService
    {
        private int _index;

        public Task<PreloadResult> PreloadSchemaAsync(
            IEnumerable<string> schemaNames,
            string instanceHash,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SpSchema> GetSpSchemaAsync(
            string spName,
            string instanceHash,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<TvpSchema> GetTvpSchemaAsync(
            string tvpName,
            string instanceHash,
            CancellationToken ct)
        {
            int index = Math.Min(_index++, schemas.Length - 1);
            return Task.FromResult(schemas[index]);
        }

        public Task FlushSchemaAsync(string instanceHash, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task FlushTvpAsync(string tvpName, string instanceHash, CancellationToken ct) =>
            throw new NotSupportedException();

        public void InvalidateSpSchema(string spName, string instanceHash) =>
            throw new NotSupportedException();

        public void InvalidateTvpSchema(string tvpName, string instanceHash) =>
            throw new NotSupportedException();
    }
}
