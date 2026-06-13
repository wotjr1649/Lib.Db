// ============================================================================
// 파일: Unit/MapperCoverageTests.cs
// 설명: 특수/대체 SQL 매퍼(Dictionary, DataRow, Reflection, GeneratedResult) 커버리지 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class MapperCoverageTests
{
    [Fact]
    public void DictionarySqlMapper_ShouldMapRawSchemaOutputAndDuplicateResultNames()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = 7
        };

        mapper.MapParameters(command, null!, schema: null);
        mapper.MapParameters(command, [], schema: null);
        mapper.MapParameters(command, values, schema: null);
        mapper.MapParameters(command, values, CreateSchema(
            Param("@Id", SqlDbType.Int),
            Param("@OutValue", SqlDbType.Int, direction: ParameterDirection.Output),
            Param("@Optional", SqlDbType.NVarChar, nullable: true)));

        command.Parameters["@OutValue"].Value = DBNull.Value;
        mapper.MapOutputParameters(command, values);
        mapper.MapOutputParameters(command, null!);

        values.Should().ContainKey("OutValue").WhoseValue.Should().BeNull();

        Mock<DbDataReader> reader = new();
        reader.Setup(r => r.FieldCount).Returns(4);
        reader.Setup(r => r.GetName(0)).Returns(" ");
        reader.Setup(r => r.GetName(1)).Returns("Col_1");
        reader.Setup(r => r.GetName(2)).Returns("Col");
        reader.Setup(r => r.GetName(3)).Returns("Col");
        reader.Setup(r => r.GetValue(0)).Returns(DBNull.Value);
        reader.Setup(r => r.GetValue(1)).Returns("existing");
        reader.Setup(r => r.GetValue(2)).Returns(1);
        reader.Setup(r => r.GetValue(3)).Returns(2);

        Dictionary<string, object?> row = mapper.MapResult(reader.Object);

        row["Column0"].Should().BeNull();
        row["Col_1"].Should().Be("existing");
        row["Col"].Should().Be(1);
        row.Values.Should().Contain(2);
    }

    [Fact]
    public void DictionarySqlMapper_ShouldCloneExplicitSqlParameterOutputReferencesAndCopyBack()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var outputVal = new SqlParameter("@OutputVal", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.Input,
            Value = 999L
        };
        var inOutVal = new SqlParameter("@InOutVal", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.InputOutput,
            Value = 5L
        };
        var returnValue = new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue,
            Value = 999
        };
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["InputVal"] = 10,
            ["OutputVal"] = outputVal,
            ["InOutVal"] = inOutVal,
            ["ReturnValue"] = returnValue
        };

        mapper.MapParameters(command, values, CreateSchema(
            Param("@InputVal", SqlDbType.Int, nullable: false),
            Param("@OutputVal", SqlDbType.Int, direction: ParameterDirection.Output),
            Param("@InOutVal", SqlDbType.Int, direction: ParameterDirection.Output)));

        command.Parameters["@OutputVal"].Should().NotBeSameAs(outputVal);
        command.Parameters["@OutputVal"].Direction.Should().Be(ParameterDirection.Output);
        command.Parameters["@OutputVal"].SqlDbType.Should().Be(SqlDbType.Int);
        command.Parameters["@OutputVal"].Value.Should().Be(DBNull.Value);
        outputVal.Direction.Should().Be(ParameterDirection.Input);
        outputVal.SqlDbType.Should().Be(SqlDbType.BigInt);
        outputVal.Value.Should().Be(999L);

        command.Parameters["@InOutVal"].Should().NotBeSameAs(inOutVal);
        command.Parameters["@InOutVal"].Direction.Should().Be(ParameterDirection.InputOutput);
        command.Parameters["@InOutVal"].SqlDbType.Should().Be(SqlDbType.Int);
        command.Parameters["@InOutVal"].Value.Should().BeOfType<int>().Which.Should().Be(5);
        inOutVal.SqlDbType.Should().Be(SqlDbType.BigInt);
        inOutVal.Value.Should().Be(5L);

        command.Parameters["@ReturnValue"].Should().NotBeSameAs(returnValue);
        command.Parameters["@ReturnValue"].Direction.Should().Be(ParameterDirection.ReturnValue);
        command.Parameters["@ReturnValue"].Value.Should().Be(DBNull.Value);
        returnValue.Value.Should().Be(999);

        command.Parameters["@OutputVal"].Value = 20;
        command.Parameters["@InOutVal"].Value = 15;
        command.Parameters["@ReturnValue"].Value = 10;
        mapper.MapOutputParameters(command, values);

        values["OutputVal"].Should().Be(20);
        values["InOutVal"].Should().Be(15);
        values["ReturnValue"].Should().BeSameAs(returnValue);
        outputVal.Value.Should().Be(20);
        inOutVal.Value.Should().Be(15);
        returnValue.Value.Should().Be(10);
    }

    [Fact]
    public void DictionarySqlMapper_ShouldNormalizeExplicitInputOutputValueWithSchema()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var inOutValue = new SqlParameter("@TextValue", SqlDbType.NVarChar, 4000)
        {
            Direction = ParameterDirection.InputOutput,
            Value = "abcdef"
        };
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["TextValue"] = inOutValue
        };

        mapper.MapParameters(command, values, CreateSchema(
            Param("@TextValue", SqlDbType.NVarChar, direction: ParameterDirection.Output, size: 3)));

        command.Parameters["@TextValue"].Should().NotBeSameAs(inOutValue);
        command.Parameters["@TextValue"].Direction.Should().Be(ParameterDirection.InputOutput);
        command.Parameters["@TextValue"].Size.Should().Be(3);
        command.Parameters["@TextValue"].Value.Should().Be("abc");
        inOutValue.Size.Should().Be(4000);
        inOutValue.Value.Should().Be("abcdef");
    }

    [Fact]
    public void DictionarySqlMapper_ShouldRejectRequiredExplicitInputOutputNull()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Required"] = new SqlParameter("@Required", SqlDbType.Int)
            {
                Direction = ParameterDirection.InputOutput,
                Value = DBNull.Value
            }
        };

        Action act = () => mapper.MapParameters(
            command,
            values,
            CreateSchema(Param(
                "@Required",
                SqlDbType.Int,
                direction: ParameterDirection.InputOutput,
                nullable: false)));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Required*");
    }

    [Fact]
    public void DictionarySqlMapper_ShouldRejectAmbiguousOutputTargetNames()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["OutputVal"] = null,
            ["outputval"] = null
        };

        command.Parameters.Add(new SqlParameter("@OutputVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 42
        });

        Action act = () => mapper.MapOutputParameters(command, values);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OutputVal*ambiguous*");
    }

    [Fact]
    public void DictionarySqlMapper_ShouldRejectUnsupportedExplicitReturnValueType()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = 1,
            ["ReturnValue"] = new SqlParameter("@ReturnValue", SqlDbType.BigInt)
            {
                Direction = ParameterDirection.ReturnValue
            }
        };

        Action act = () => mapper.MapParameters(command, values, CreateSchema(Param("@Id", SqlDbType.Int)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ReturnValue*Int*");
    }

    [Fact]
    public void DictionarySqlMapper_ShouldNotDoubleBindSchemaReturnValue()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var returnValue = new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue,
            Value = 999
        };
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ReturnValue"] = returnValue
        };

        mapper.MapParameters(
            command,
            values,
            CreateSchema(Param("@ReturnValue", SqlDbType.Int, direction: ParameterDirection.ReturnValue)));

        command.Parameters
            .Cast<SqlParameter>()
            .Should()
            .ContainSingle(parameter => parameter.Direction == ParameterDirection.ReturnValue);
        command.Parameters["@ReturnValue"].Should().NotBeSameAs(returnValue);
        command.Parameters["@ReturnValue"].Value = 10;

        mapper.MapOutputParameters(command, values);

        values["ReturnValue"].Should().BeSameAs(returnValue);
        returnValue.Value.Should().Be(10);
    }

    [Fact]
    public void DictionarySqlMapper_ShouldRejectSchemaModeDuplicateExtraReturnValueParameters()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ReturnValue"] = new SqlParameter("@ReturnValue", SqlDbType.Int)
            {
                Direction = ParameterDirection.ReturnValue
            },
            ["OtherReturn"] = new SqlParameter("@OtherReturn", SqlDbType.Int)
            {
                Direction = ParameterDirection.ReturnValue
            }
        };

        Action act = () => mapper.MapParameters(
            command,
            values,
            CreateSchema(Param("@ReturnValue", SqlDbType.Int, direction: ParameterDirection.ReturnValue)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only one ReturnValue*OtherReturn*");
    }

    [Theory]
    [InlineData(SqlDbType.Structured)]
    [InlineData(SqlDbType.Text)]
    [InlineData(SqlDbType.NText)]
    [InlineData(SqlDbType.Image)]
    public void DictionarySqlMapper_ShouldRejectUnsupportedExplicitOutputTypes(SqlDbType sqlDbType)
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Blob"] = new SqlParameter("@Blob", sqlDbType)
            {
                Direction = ParameterDirection.Output
            }
        };

        Action act = () => mapper.MapParameters(
            command,
            values,
            CreateSchema(Param("@Blob", sqlDbType, direction: ParameterDirection.Output)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Blob*unsupported*");
    }

    [Theory]
    [InlineData(SqlDbType.Structured)]
    [InlineData(SqlDbType.Text)]
    [InlineData(SqlDbType.NText)]
    [InlineData(SqlDbType.Image)]
    public void DictionarySqlMapper_ShouldRejectUnsupportedSchemaOutputTypesWithoutExplicitSqlParameter(SqlDbType sqlDbType)
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();

        Action act = () => mapper.MapParameters(
            command,
            [],
            CreateSchema(Param("@Blob", sqlDbType, direction: ParameterDirection.Output)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Blob*unsupported*");
    }

    [Fact]
    public void DictionarySqlMapper_ShouldRejectDuplicateRawReturnValueParameters()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ReturnValue"] = new SqlParameter("@ReturnValue", SqlDbType.Int)
            {
                Direction = ParameterDirection.ReturnValue
            },
            ["OtherReturn"] = new SqlParameter("@OtherReturn", SqlDbType.Int)
            {
                Direction = ParameterDirection.ReturnValue
            }
        };

        Action act = () => mapper.MapParameters(command, values, schema: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only one ReturnValue*");
    }

    [Fact]
    public void DbBinder_ShouldAllowExplicitSqlParameterRebindAfterCommandParametersClear()
    {
        using var command = new SqlCommand();
        var returnValue = new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue
        };

        DbBinder.BindRawParameter(command, "ReturnValue", returnValue);
        command.Parameters.Clear();

        Action act = () => DbBinder.BindRawParameter(command, "ReturnValue", returnValue);

        act.Should().NotThrow();
        command.Parameters
            .Cast<SqlParameter>()
            .Should()
            .ContainSingle(parameter => parameter.Direction == ParameterDirection.ReturnValue);
    }

    [Fact]
    public void DbBinder_ShouldNotRetainExplicitSqlParameterAfterCommandParametersClear()
    {
        using var command = new SqlCommand();
        WeakReference weakReference = BindExplicitParameterAndClear(command);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        weakReference.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void DbBinder_ShouldEscapeControlCharactersInOutputParameterDisplayName()
    {
        using var command = new SqlCommand();
        const string parameterName = "Bad\t\u001B\u202EName";
        var parameter = new SqlParameter(parameterName, SqlDbType.Image)
        {
            Direction = ParameterDirection.Output
        };

        Action act = () => DbBinder.BindRawParameter(command, parameterName, parameter);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Bad\\u0009\\u001B\\u202EName*");
    }

    [Fact]
    public void DictionarySqlMapper_ShouldOmitExplicitSqlParameterWhenInputUsesDbDefault()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var defaultedValue = new SqlParameter("@Defaulted", SqlDbType.Int)
        {
            Direction = ParameterDirection.Input,
            Value = DBNull.Value
        };
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Defaulted"] = defaultedValue
        };

        mapper.MapParameters(command, values, CreateSchema(
            Param("@Defaulted", SqlDbType.Int, nullable: false, hasDefault: true)));

        command.Parameters.Cast<SqlParameter>()
            .Should()
            .NotContain(parameter => parameter.ParameterName == "@Defaulted");
    }

    [Fact]
    public void ExpressionTreeMapper_ShouldBindExplicitSqlParameterOutputReferences()
    {
        var mapper = new ExpressionTreeMapper<ExplicitSqlParameterDto>(jsonOptions: null, strict: true);

        AssertExplicitSqlParameterSchemaBinding(mapper);
    }

    [Fact]
    public void ExpressionTreeMapper_ShouldNotEvaluateNonSqlParameterPropertiesWhenScanningExtraReturnValue()
    {
        var mapper = new ExpressionTreeMapper<ThrowingGetterReturnValueDto>(jsonOptions: null, strict: true);

        AssertExtraReturnValueScanDoesNotEvaluateNonSqlParameterProperties(mapper);
    }

    [Fact]
    public void ExpressionTreeMapper_ShouldRejectSchemaModeDuplicateExtraReturnValueParameters()
    {
        var mapper = new ExpressionTreeMapper<DuplicateReturnValueDto>(jsonOptions: null, strict: true);

        AssertDuplicateExtraReturnValueRejected(mapper);
    }

    [Fact]
    public void DictionarySqlMapper_ShouldThrowWhenRequiredKeyIsMissing()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();

        Action act = () => mapper.MapParameters(
            command,
            [],
            CreateSchema(Param("@Required", SqlDbType.Int, nullable: false)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Required*");
    }

    [Fact]
    public void DataRowSqlMapper_ShouldMapRawSchemaAndRejectUnsupportedResultMapping()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("OutValue", typeof(int));
        table.Rows.Add(9, DBNull.Value);
        DataRow row = table.Rows[0];

        mapper.MapParameters(command, null!, schema: null);
        mapper.MapParameters(command, row, schema: null);
        mapper.MapParameters(command, row, CreateSchema(
            Param("@Id", SqlDbType.Int),
            Param("@OutValue", SqlDbType.Int, direction: ParameterDirection.Output),
            Param("@Optional", SqlDbType.NVarChar, nullable: true)));
        mapper.MapOutputParameters(command, row);

        command.Parameters.Count.Should().BeGreaterThan(0);
        mapper.Invoking(m => m.MapResult(Mock.Of<DbDataReader>()))
            .Should()
            .Throw<NotSupportedException>();
    }

    [Fact]
    public void DataRowSqlMapper_ShouldMapOutputParametersTransactionally()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("OutputVal", typeof(int));
        table.Columns.Add("InOutVal", typeof(int));
        table.Columns.Add("NullableText", typeof(string));
        table.Rows.Add(1, 5, "old");
        DataRow row = table.Rows[0];

        command.Parameters.Add(new SqlParameter("@OutputVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 20
        });
        command.Parameters.Add(new SqlParameter("@InOutVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.InputOutput,
            Value = 15
        });
        command.Parameters.Add(new SqlParameter("@NullableText", SqlDbType.NVarChar, 50)
        {
            Direction = ParameterDirection.Output,
            Value = DBNull.Value
        });

        mapper.MapOutputParameters(command, row);

        row["OutputVal"].Should().Be(20);
        row["InOutVal"].Should().Be(15);
        row["NullableText"].Should().Be(DBNull.Value);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldPreserveExplicitInputOutputValueWithSchemaOutput()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("TextValue", typeof(object));
        var source = new SqlParameter("@TextValue", SqlDbType.NVarChar, 4000)
        {
            Direction = ParameterDirection.InputOutput,
            Value = "abcdef"
        };
        table.Rows.Add(source);
        DataRow row = table.Rows[0];

        mapper.MapParameters(
            command,
            row,
            CreateSchema(Param("@TextValue", SqlDbType.NVarChar, direction: ParameterDirection.Output, size: 3)));

        command.Parameters["@TextValue"].Should().NotBeSameAs(source);
        command.Parameters["@TextValue"].Direction.Should().Be(ParameterDirection.InputOutput);
        command.Parameters["@TextValue"].Size.Should().Be(3);
        command.Parameters["@TextValue"].Value.Should().Be("abc");
        source.Value.Should().Be("abcdef");

        command.Parameters["@TextValue"].Value = "xyz";
        mapper.MapOutputParameters(command, row);

        row["TextValue"].Should().Be("xyz");
        source.Value.Should().Be("xyz");
    }

    [Fact]
    public void DataRowSqlMapper_ShouldPreserveExplicitInputOutputValueWithSchemaInputOutput()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("TextValue", typeof(object));
        var source = new SqlParameter("@TextValue", SqlDbType.NVarChar, 4000)
        {
            Direction = ParameterDirection.InputOutput,
            Value = "abcdef"
        };
        table.Rows.Add(source);
        DataRow row = table.Rows[0];

        mapper.MapParameters(
            command,
            row,
            CreateSchema(Param("@TextValue", SqlDbType.NVarChar, direction: ParameterDirection.InputOutput, size: 3)));

        command.Parameters["@TextValue"].Should().NotBeSameAs(source);
        command.Parameters["@TextValue"].Direction.Should().Be(ParameterDirection.InputOutput);
        command.Parameters["@TextValue"].Size.Should().Be(3);
        command.Parameters["@TextValue"].Value.Should().Be("abc");
        source.Value.Should().Be("abcdef");

        command.Parameters["@TextValue"].Value = "uvw";
        mapper.MapOutputParameters(command, row);

        row["TextValue"].Should().Be("uvw");
        source.Value.Should().Be("uvw");
    }

    [Fact]
    public void DataRowSqlMapper_ShouldIgnoreMissingOutputColumnWhenNotStrict()
    {
        var mapper = new DataRowSqlMapper(strict: false);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);
        DataRow row = table.Rows[0];

        command.Parameters.Add(new SqlParameter("@Missing", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 7
        });

        mapper.MapOutputParameters(command, row);

        row["Id"].Should().Be(1);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldThrowWhenStrictOutputColumnIsMissing()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);
        DataRow row = table.Rows[0];

        command.Parameters.Add(new SqlParameter("@Missing", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 7
        });

        Action act = () => mapper.MapOutputParameters(command, row);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing*missing*");
        row["Id"].Should().Be(1);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldRollbackWhenOutputColumnWriteFails()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("Good", typeof(int));
        DataColumn bad = table.Columns.Add("Bad", typeof(string));
        bad.MaxLength = 2;
        table.Rows.Add(1, "ok");
        DataRow row = table.Rows[0];

        var source = new SqlParameter("@Good", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 1
        };
        DbBinder.BindRawParameter(command, "Good", source);
        command.Parameters["@Good"].Value = 10;
        command.Parameters.Add(new SqlParameter("@Bad", SqlDbType.NVarChar, 8)
        {
            Direction = ParameterDirection.Output,
            Value = "toolong"
        });

        Action act = () => mapper.MapOutputParameters(command, row);

        InvalidOperationException exception = act.Should().Throw<InvalidOperationException>()
            .Which;
        exception.Message.Should().Contain("transactionally");
        exception.InnerException.Should().BeNull();
        exception.Message.Should().NotContain("toolong");
        row["Good"].Should().Be(1);
        row["Bad"].Should().Be("ok");
        source.Value.Should().Be(1);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldCopyRegisteredExplicitSqlParameterSourceOnSuccess()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("OutputVal", typeof(object));
        var source = new SqlParameter("@OutputVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 1
        };
        table.Rows.Add(source);
        DataRow row = table.Rows[0];

        DbBinder.BindRawParameter(command, "OutputVal", source);
        command.Parameters["@OutputVal"].Value = 20;

        mapper.MapOutputParameters(command, row);

        row["OutputVal"].Should().Be(20);
        source.Value.Should().Be(20);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldNotMapReturnValueToDataRowColumn()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("ReturnValue", typeof(int));
        table.Rows.Add(7);
        DataRow row = table.Rows[0];

        command.Parameters.Add(new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue,
            Value = 10
        });

        mapper.MapOutputParameters(command, row);

        row["ReturnValue"].Should().Be(7);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldIgnoreAmbiguousScalarReturnValueColumns()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("ReturnValue", typeof(int));
        table.Columns.Add("returnvalue", typeof(int));
        table.Rows.Add(7, 8);

        command.Parameters.Add(new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue,
            Value = 10
        });

        mapper.MapOutputParameters(command, table.Rows[0]);

        table.Rows[0]["ReturnValue"].Should().Be(7);
        table.Rows[0]["returnvalue"].Should().Be(8);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldExcludeReturnValueFromDataRowButCopyRegisteredSource()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("ReturnValue", typeof(object));
        var source = new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue,
            Value = 999
        };
        table.Rows.Add(source);
        DataRow row = table.Rows[0];

        DbBinder.BindRawParameter(command, "ReturnValue", source);
        command.Parameters["@ReturnValue"].Value = 10;

        mapper.MapOutputParameters(command, row);

        row["ReturnValue"].Should().BeSameAs(source);
        source.Value.Should().Be(10);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldBindSchemaReturnValueFromExplicitDataRowSource()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("ReturnValue", typeof(object));
        var source = new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue,
            Value = 999
        };
        table.Rows.Add(source);
        DataRow row = table.Rows[0];

        mapper.MapParameters(
            command,
            row,
            CreateSchema(Param("@ReturnValue", SqlDbType.Int, direction: ParameterDirection.ReturnValue)));
        command.Parameters["@ReturnValue"].Value = 10;
        mapper.MapOutputParameters(command, row);

        row["ReturnValue"].Should().BeSameAs(source);
        source.Value.Should().Be(10);
    }

    [Fact]
    public void DataRowSqlMapper_ShouldRollbackReturnValueSourceWhenOutputColumnWriteFails()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("ReturnValue", typeof(object));
        DataColumn bad = table.Columns.Add("Bad", typeof(string));
        bad.MaxLength = 2;
        var source = new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue,
            Value = null
        };
        table.Rows.Add(source, "ok");
        DataRow row = table.Rows[0];

        DbBinder.BindRawParameter(command, "ReturnValue", source);
        command.Parameters["@ReturnValue"].Value = 10;
        command.Parameters.Add(new SqlParameter("@Bad", SqlDbType.NVarChar, 8)
        {
            Direction = ParameterDirection.Output,
            Value = "toolong"
        });

        Action act = () => mapper.MapOutputParameters(command, row);

        InvalidOperationException exception = act.Should().Throw<InvalidOperationException>()
            .Which;
        exception.Message.Should().Contain("transactionally");
        exception.InnerException.Should().BeNull();
        exception.Message.Should().NotContain("toolong");
        row["ReturnValue"].Should().BeSameAs(source);
        row["Bad"].Should().Be("ok");
        source.Value.Should().BeNull();
    }

    [Fact]
    public void DataRowSqlMapper_ShouldRejectAmbiguousOutputColumn()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("OutputVal", typeof(int));
        table.Columns.Add("outputval", typeof(int));
        table.Rows.Add(1, 2);

        command.Parameters.Add(new SqlParameter("@OutputVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 3
        });

        Action act = () => mapper.MapOutputParameters(command, table.Rows[0]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OutputVal*ambiguous*");
    }

    [Fact]
    public void DataRowSqlMapper_ShouldRejectReadOnlyOutputColumn()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        DataColumn output = table.Columns.Add("OutputVal", typeof(int));
        table.Rows.Add(1);
        output.ReadOnly = true;

        command.Parameters.Add(new SqlParameter("@OutputVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 3
        });

        Action act = () => mapper.MapOutputParameters(command, table.Rows[0]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OutputVal*read-only*");
    }

    [Fact]
    public void DataRowSqlMapper_ShouldRejectExpressionOutputColumn()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("Base", typeof(int));
        table.Columns.Add("Computed", typeof(int), "Base + 1");
        table.Rows.Add(1);

        command.Parameters.Add(new SqlParameter("@Computed", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 3
        });

        Action act = () => mapper.MapOutputParameters(command, table.Rows[0]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Computed*expression*");
    }

    [Fact]
    public void DataRowSqlMapper_ShouldThrowWhenRequiredColumnIsMissing()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);

        Action act = () => mapper.MapParameters(
            command,
            table.Rows[0],
            CreateSchema(Param("@Required", SqlDbType.Int, nullable: false)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Required*");
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldMapRawSchemaOutputAndRejectResultMapping()
    {
        var mapper = new ReflectionParameterMapper<ReflectionCoverageDto>(strict: true);
        using var command = new SqlCommand();
        var dto = new ReflectionCoverageDto
        {
            Id = 11,
            Name = "neo"
        };

        mapper.MapParameters(command, null!, schema: null);
        mapper.MapParameters(command, dto, schema: null);
        mapper.MapParameters(command, dto, CreateSchema(
            Param("@Id", SqlDbType.Int),
            Param("@OutValue", SqlDbType.Int, direction: ParameterDirection.Output),
            Param("@Defaulted", SqlDbType.Int, nullable: false, hasDefault: true),
            Param("@Optional", SqlDbType.NVarChar, nullable: true)));

        using var outputCommand = new SqlCommand();
        outputCommand.Parameters.Add(new SqlParameter("@OutValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 42
        });
        outputCommand.Parameters.Add(new SqlParameter("@NullValue", SqlDbType.NVarChar)
        {
            Direction = ParameterDirection.InputOutput,
            Value = DBNull.Value
        });

        mapper.MapOutputParameters(outputCommand, dto);
        mapper.MapOutputParameters(outputCommand, null!);

        dto.OutValue.Should().Be(42);
        dto.NullValue.Should().BeNull();
        mapper.Invoking(m => m.MapResult(Mock.Of<DbDataReader>()))
            .Should()
            .Throw<NotSupportedException>();
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldBindExplicitSqlParameterOutputReferences()
    {
        var mapper = new ReflectionParameterMapper<ExplicitSqlParameterDto>(strict: true);

        AssertExplicitSqlParameterSchemaBinding(mapper);
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldNotEvaluateNonSqlParameterPropertiesWhenScanningExtraReturnValue()
    {
        var mapper = new ReflectionParameterMapper<ThrowingGetterReturnValueDto>(strict: true);

        AssertExtraReturnValueScanDoesNotEvaluateNonSqlParameterProperties(mapper);
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldRejectSchemaModeDuplicateExtraReturnValueParameters()
    {
        var mapper = new ReflectionParameterMapper<DuplicateReturnValueDto>(strict: true);

        AssertDuplicateExtraReturnValueRejected(mapper);
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldThrowWhenRequiredPropertyIsMissing()
    {
        var mapper = new ReflectionParameterMapper<ReflectionCoverageDto>(strict: true);
        using var command = new SqlCommand();

        Action act = () => mapper.MapParameters(
            command,
            new ReflectionCoverageDto(),
            CreateSchema(Param("@RequiredMissing", SqlDbType.Int, nullable: false)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RequiredMissing*");
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldCoverDefaultMissingInputAndOutputBranches()
    {
        var mapper = new ReflectionParameterMapper<ReflectionOutputCoverageDto>(strict: true);
        var dto = new ReflectionOutputCoverageDto();
        using var inputCommand = new SqlCommand();

        mapper.MapParameters(inputCommand, dto, CreateSchema(
            Param("@MissingDefaulted", SqlDbType.Int, nullable: false, hasDefault: true),
            Param("@MissingNullable", SqlDbType.NVarChar, nullable: true),
            Param("@MissingInputOutput", SqlDbType.NVarChar, direction: ParameterDirection.InputOutput)));

        inputCommand.Parameters
            .Cast<SqlParameter>()
            .Should()
            .ContainSingle(p => p.ParameterName == "@MissingNullable");
        inputCommand.Parameters["@MissingNullable"].Value.Should().Be(DBNull.Value);

        using var outputCommand = new SqlCommand();
        outputCommand.Parameters.Add(new SqlParameter("@WritableValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.InputOutput,
            Value = 5
        });
        outputCommand.Parameters.Add(new SqlParameter("@ReadOnlyValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 7
        });
        outputCommand.Parameters.Add(new SqlParameter("@MissingOutput", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 8
        });
        outputCommand.Parameters.Add(new SqlParameter("@IgnoredInput", SqlDbType.Int)
        {
            Direction = ParameterDirection.Input,
            Value = 9
        });

        mapper.MapOutputParameters(outputCommand, dto);

        dto.WritableValue.Should().Be(5);
        dto.ReadOnlyValue.Should().Be(0);
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldMapDynamicTypeWithMetadataTokenFallback()
    {
        Type dynamicType = CreateDynamicParameterType();
        object dto = Activator.CreateInstance(dynamicType)!;
        dynamicType.GetProperty("Value")!.SetValue(dto, 17);

        Type mapperType = typeof(ReflectionParameterMapper<>).MakeGenericType(dynamicType);
        object mapper = Activator.CreateInstance(mapperType, [true])!;
        using var command = new SqlCommand();

        mapperType.GetMethod(nameof(ReflectionParameterMapper<object>.MapParameters))!
            .Invoke(mapper, [command, dto, null]);

        command.Parameters["@Value"].Value.Should().Be(17);
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldUseNameOrderingWhenMetadataTokensAreUnavailable()
    {
        using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
        var mapper = new ReflectionParameterMapper<ReflectionMetadataTokenFallbackDto>(strict: true);
        var dto = new ReflectionMetadataTokenFallbackDto
        {
            Beta = 2,
            Alpha = 1
        };
        using var command = new SqlCommand();

        mapper.MapParameters(command, dto, schema: null);

        command.Parameters.Cast<SqlParameter>()
            .Select(static parameter => parameter.ParameterName)
            .Should()
            .Equal("@Alpha", "@Beta");
    }

    [Fact]
    public void MapperFactory_ShouldUseReflectionMapperWhenDynamicCodeIsDisabled()
    {
        using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var factory = new MapperFactory(services, new LibDbOptions());

        ISqlMapper<RuntimeFeatureFallbackDto> mapper = factory.GetMapper<RuntimeFeatureFallbackDto>();

        mapper.Should().BeOfType<ReflectionParameterMapper<RuntimeFeatureFallbackDto>>();
    }

    [Fact]
    public void MapperFactory_ShouldKeepRuntimeFeatureCacheEntriesIsolated()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var factory = new MapperFactory(services, new LibDbOptions());
        bool runtimeDynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported;

        using (RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false))
        {
            ISqlMapper<RuntimeFeatureCacheIsolationDto> fallbackMapper =
                factory.GetMapper<RuntimeFeatureCacheIsolationDto>();

            fallbackMapper.Should().BeOfType<ReflectionParameterMapper<RuntimeFeatureCacheIsolationDto>>();
        }

        RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().Be(runtimeDynamicCodeSupported);

        ISqlMapper<RuntimeFeatureCacheIsolationDto> defaultMapper =
            factory.GetMapper<RuntimeFeatureCacheIsolationDto>();

        if (!runtimeDynamicCodeSupported)
        {
            defaultMapper.Should().BeOfType<ReflectionParameterMapper<RuntimeFeatureCacheIsolationDto>>();
            return;
        }

        defaultMapper.Should().BeOfType<ExpressionTreeMapper<RuntimeFeatureCacheIsolationDto>>();
    }

    [Fact]
    public void RuntimeFeatureSwitch_ShouldRestoreNestedOverridesAndIgnoreDoubleDispose()
    {
        bool runtimeDynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported;
        bool original = RuntimeFeatureSwitch.IsDynamicCodeSupported;
        IDisposable outer = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
        RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().BeFalse();

        using (RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(true))
        {
            RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().Be(runtimeDynamicCodeSupported);
        }

        RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().BeFalse();

        outer.Dispose();
        outer.Dispose();

        RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().Be(original);
    }

    [Fact]
    public void GeneratedResultMapper_ShouldUseReflectionParameterMapperWhenDynamicCodeIsDisabled()
    {
        using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);

        var mapper = new GeneratedResultMapper<GeneratedDbDataReaderRow>(new LibDbOptions());

        GetPrivateField(mapper, "_parameterMapper")
            .Should()
            .BeOfType<ReflectionParameterMapper<GeneratedDbDataReaderRow>>();
    }

    [Fact]
    public void GeneratedResultMapper_ShouldUseDbDataReaderMapAndDelegateParameterMapping()
    {
        var mapper = new GeneratedResultMapper<GeneratedDbDataReaderRow>(new LibDbOptions());
        Mock<DbDataReader> reader = new();
        reader.Setup(r => r.GetInt32(0)).Returns(21);

        GeneratedDbDataReaderRow row = mapper.MapResult(reader.Object);
        using var command = new SqlCommand();
        mapper.MapParameters(command, row, schema: null);
        mapper.MapOutputParameters(command, row);

        row.Id.Should().Be(21);
        command.Parameters.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GeneratedResultMapper_ShouldRejectDbDataReaderWhenOnlySqlDataReaderMapExists()
    {
        var mapper = new GeneratedResultMapper<SqlOnlyGeneratedRow>(new LibDbOptions());

        Action act = () => mapper.MapResult(Mock.Of<DbDataReader>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*only exposes Map(SqlDataReader)*");
    }

    [Fact]
    public async Task GeneratedResultMapper_ShouldUseSqlDataReaderMapWhenSqlDataReaderIsAvailable()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Verification");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var mapper = new GeneratedResultMapper<SqlOnlyGeneratedRow>(new LibDbOptions());

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand("SELECT CAST(34 AS int)", connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            TestContext.Current.CancellationToken);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        SqlOnlyGeneratedRow row = mapper.MapResult(reader);

        row.Id.Should().Be(34);
    }

    [Fact]
    public void GeneratedResultMapper_ShouldThrowWhenStaticMapIsMissing()
    {
        Action act = () => _ = new GeneratedResultMapper<NoStaticMapRow>(new LibDbOptions());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Map(DbDataReader)*Map(SqlDataReader)*");
    }

    [Fact]
    public void ScalarSqlMapper_ShouldMapPrimitiveNullableStreamAndNoOpParameters()
    {
        var intMapper = new ScalarSqlMapper<int>();
        var nullableMapper = new ScalarSqlMapper<int?>();
        var streamMapper = new ScalarSqlMapper<Stream>();
        using var command = new SqlCommand();

        intMapper.MapParameters(command, 1, schema: null);
        intMapper.MapOutputParameters(command, 1);

        intMapper.MapResult(ValueReader(123)).Should().Be(123);
        intMapper.MapResult(ValueReader((byte)7)).Should().Be(7);
        nullableMapper.MapResult(ValueReader(DBNull.Value)).Should().BeNull();
        using Stream stream = streamMapper.MapResult(ValueReader(new byte[] { 1, 2, 3 }));
        stream.Length.Should().Be(3);
    }

    private static void AssertExplicitSqlParameterSchemaBinding(ISqlMapper<ExplicitSqlParameterDto> mapper)
    {
        using var command = new SqlCommand();
        var inputVal = new SqlParameter("@InputVal", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.Output,
            Value = 10L
        };
        var outputVal = new SqlParameter("@OutputVal", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.Input,
            Value = 999L
        };
        var inOutVal = new SqlParameter("@InOutVal", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.InputOutput,
            Value = 5L
        };
        var returnValue = new SqlParameter("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue,
            Value = 999
        };
        var dto = new ExplicitSqlParameterDto
        {
            InputVal = inputVal,
            OutputVal = outputVal,
            InOutVal = inOutVal,
            ReturnValue = returnValue
        };

        mapper.MapParameters(command, dto, CreateSchema(
            Param("@InputVal", SqlDbType.Int, nullable: false),
            Param("@OutputVal", SqlDbType.Int, direction: ParameterDirection.Output),
            Param("@InOutVal", SqlDbType.Int, direction: ParameterDirection.Output)));

        command.Parameters["@InputVal"].Should().NotBeSameAs(inputVal);
        command.Parameters["@InputVal"].Direction.Should().Be(ParameterDirection.Input);
        command.Parameters["@InputVal"].SqlDbType.Should().Be(SqlDbType.Int);
        command.Parameters["@InputVal"].Value.Should().BeOfType<int>().Which.Should().Be(10);
        inputVal.Direction.Should().Be(ParameterDirection.Output);
        inputVal.SqlDbType.Should().Be(SqlDbType.BigInt);
        inputVal.Value.Should().Be(10L);

        command.Parameters["@OutputVal"].Should().NotBeSameAs(outputVal);
        command.Parameters["@OutputVal"].Direction.Should().Be(ParameterDirection.Output);
        command.Parameters["@OutputVal"].SqlDbType.Should().Be(SqlDbType.Int);
        command.Parameters["@OutputVal"].Value.Should().Be(DBNull.Value);
        outputVal.Direction.Should().Be(ParameterDirection.Input);
        outputVal.SqlDbType.Should().Be(SqlDbType.BigInt);
        outputVal.Value.Should().Be(999L);

        command.Parameters["@InOutVal"].Should().NotBeSameAs(inOutVal);
        command.Parameters["@InOutVal"].Direction.Should().Be(ParameterDirection.InputOutput);
        command.Parameters["@InOutVal"].SqlDbType.Should().Be(SqlDbType.Int);
        command.Parameters["@InOutVal"].Value.Should().BeOfType<int>().Which.Should().Be(5);
        inOutVal.SqlDbType.Should().Be(SqlDbType.BigInt);
        inOutVal.Value.Should().Be(5L);

        command.Parameters["@ReturnValue"].Should().NotBeSameAs(returnValue);
        command.Parameters["@ReturnValue"].Direction.Should().Be(ParameterDirection.ReturnValue);
        command.Parameters["@ReturnValue"].Value.Should().Be(DBNull.Value);
        returnValue.Value.Should().Be(999);

        command.Parameters["@OutputVal"].Value = 20;
        command.Parameters["@InOutVal"].Value = 15;
        command.Parameters["@ReturnValue"].Value = 10;
        mapper.Invoking(m => m.MapOutputParameters(command, dto))
            .Should()
            .NotThrow();

        dto.OutputVal.Should().BeSameAs(outputVal);
        dto.InOutVal.Should().BeSameAs(inOutVal);
        dto.ReturnValue.Should().BeSameAs(returnValue);
        dto.OutputVal.Value.Should().Be(20);
        dto.InOutVal.Value.Should().Be(15);
        dto.ReturnValue.Value.Should().Be(10);
    }

    private static void AssertExtraReturnValueScanDoesNotEvaluateNonSqlParameterProperties(
        ISqlMapper<ThrowingGetterReturnValueDto> mapper)
    {
        using var command = new SqlCommand();
        var dto = new ThrowingGetterReturnValueDto();

        Action act = () => mapper.MapParameters(
            command,
            dto,
            CreateSchema(Param("@Id", SqlDbType.Int, nullable: false)));

        act.Should().NotThrow();
        command.Parameters
            .Cast<SqlParameter>()
            .Should()
            .ContainSingle(parameter => parameter.Direction == ParameterDirection.ReturnValue);
    }

    private static void AssertDuplicateExtraReturnValueRejected(ISqlMapper<DuplicateReturnValueDto> mapper)
    {
        using var command = new SqlCommand();
        var dto = new DuplicateReturnValueDto();

        Action act = () => mapper.MapParameters(
            command,
            dto,
            CreateSchema(Param("@ReturnValue", SqlDbType.Int, direction: ParameterDirection.ReturnValue)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only one ReturnValue*OtherReturn*");
    }

    private static WeakReference BindExplicitParameterAndClear(SqlCommand command)
    {
        var parameter = new SqlParameter("@OutputVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        WeakReference weakReference = new(parameter);

        DbBinder.BindRawParameter(command, "OutputVal", parameter);
        command.Parameters.Clear();

        return weakReference;
    }

    private static object GetPrivateField(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return field.GetValue(instance)!;
    }

    private static SpSchema CreateSchema(params SpParameterMetadata[] parameters)
        => new()
        {
            Name = "dbo.usp_Coverage",
            VersionToken = 1,
            LastCheckedAt = DateTime.UtcNow,
            Parameters = parameters
        };

    private static SpParameterMetadata Param(
        string name,
        SqlDbType dbType,
        ParameterDirection direction = ParameterDirection.Input,
        bool nullable = true,
        bool hasDefault = false,
        int size = 0,
        byte precision = 0,
        byte scale = 0)
        => new(
            name,
            UdtTypeName: null,
            Size: size,
            dbType,
            direction,
            Precision: precision,
            Scale: scale,
            IsNullable: nullable,
            HasDefaultValue: hasDefault);

    private sealed class ReflectionCoverageDto
    {
        public int Id { get; set; }

        [DbParameter(DbType = SqlDbType.NVarChar, Size = 32)]
        public string Name { get; set; } = "";

        public int? OutValue { get; set; }

        public string? NullValue { get; set; }
    }

    private sealed class ReflectionOutputCoverageDto
    {
        public int? WritableValue { get; set; }

        public int ReadOnlyValue => 0;
    }

    private sealed class ExplicitSqlParameterDto
    {
        public SqlParameter InputVal { get; init; } = new();

        public SqlParameter OutputVal { get; init; } = new();

        public SqlParameter InOutVal { get; init; } = new();

        public SqlParameter ReturnValue { get; init; } = new();
    }

    private sealed class ThrowingGetterReturnValueDto
    {
        public int Id { get; init; } = 1;

        public SqlParameter ReturnValue { get; init; } = new("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue
        };

        public int Explodes => throw new InvalidOperationException("This getter must not be evaluated.");
    }

    private sealed class DuplicateReturnValueDto
    {
        public SqlParameter ReturnValue { get; init; } = new("@ReturnValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue
        };

        public SqlParameter OtherReturn { get; init; } = new("@OtherReturn", SqlDbType.Int)
        {
            Direction = ParameterDirection.ReturnValue
        };
    }

    private sealed class ReflectionMetadataTokenFallbackDto
    {
        public int Beta { get; set; }

        public int Alpha { get; set; }
    }

    private sealed class RuntimeFeatureFallbackDto
    {
        public int Id { get; set; }
    }

    private sealed class RuntimeFeatureCacheIsolationDto
    {
        public int Id { get; set; }
    }

    public sealed class GeneratedDbDataReaderRow : IMapableResult<GeneratedDbDataReaderRow>
    {
        public int Id { get; set; }

        public static GeneratedDbDataReaderRow Map(DbDataReader reader)
            => new() { Id = reader.GetInt32(0) };

        public static GeneratedDbDataReaderRow Map(SqlDataReader reader)
            => throw new NotSupportedException();
    }

    public sealed class SqlOnlyGeneratedRow : IMapableResult<SqlOnlyGeneratedRow>
    {
        public int Id { get; set; }

        public static SqlOnlyGeneratedRow Map(SqlDataReader reader)
            => new() { Id = reader.GetInt32(0) };
    }

    public sealed class NoStaticMapRow
    {
    }

    private static DbDataReader ValueReader(object value)
    {
        Mock<DbDataReader> reader = new();
        reader.Setup(r => r.GetValue(0)).Returns(value);
        return reader.Object;
    }

    private static Type CreateDynamicParameterType()
    {
        var assemblyName = new AssemblyName("LibDbMapperCoverageDynamic" + Guid.NewGuid().ToString("N"));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule("Main");
        TypeBuilder typeBuilder = module.DefineType(
            "RuntimeParameter",
            TypeAttributes.Public | TypeAttributes.Class);
        FieldBuilder valueField = typeBuilder.DefineField(
            "_value",
            typeof(int),
            FieldAttributes.Private);
        PropertyBuilder valueProperty = typeBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            typeof(int),
            Type.EmptyTypes);
        const MethodAttributes AccessorAttributes =
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        MethodBuilder getter = typeBuilder.DefineMethod(
            "get_Value",
            AccessorAttributes,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, valueField);
        getterIl.Emit(OpCodes.Ret);

        MethodBuilder setter = typeBuilder.DefineMethod(
            "set_Value",
            AccessorAttributes,
            null,
            [typeof(int)]);
        ILGenerator setterIl = setter.GetILGenerator();
        setterIl.Emit(OpCodes.Ldarg_0);
        setterIl.Emit(OpCodes.Ldarg_1);
        setterIl.Emit(OpCodes.Stfld, valueField);
        setterIl.Emit(OpCodes.Ret);

        valueProperty.SetGetMethod(getter);
        valueProperty.SetSetMethod(setter);

        return typeBuilder.CreateTypeInfo()!.AsType();
    }
}
