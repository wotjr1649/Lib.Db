#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Data.SqlTypes;
using System.Runtime.CompilerServices;

namespace Lib.Db.Execution.Output;

/// <summary>
/// 호출자가 제공한 <see cref="SqlParameter"/>를 명령 소유 복사본으로 변환하고,
/// 실행 후 출력값만 호출자 파라미터로 되돌립니다.
/// </summary>
internal static class SqlParameterCloneFactory
{
    private static readonly ConditionalWeakTable<SqlParameter, SqlParameter> s_cloneSources = new();
    private static readonly SqlDbType? s_cursorSqlDbType =
        Enum.TryParse("Cursor", ignoreCase: true, out SqlDbType cursor)
            ? cursor
            : null;

    public static SqlParameter CloneForCommand(SqlParameter source, string parameterName)
    {
        var clone = new SqlParameter
        {
            ParameterName = parameterName.StartsWith('@') ? parameterName : "@" + parameterName,
            Direction = source.Direction,
            IsNullable = source.IsNullable,
            SourceColumn = source.SourceColumn,
            SourceColumnNullMapping = source.SourceColumnNullMapping,
            SourceVersion = source.SourceVersion
        };

        CopyTypeMetadata(source, clone);
        CopyValueState(source, clone);
        return clone;
    }

    public static void RegisterClone(SqlCommand command, SqlParameter clone, SqlParameter source, string parameterName)
    {
        foreach (SqlParameter existing in command.Parameters)
        {
            if (s_cloneSources.TryGetValue(existing, out SqlParameter? existingSource) &&
                ReferenceEquals(existingSource, source))
            {
                string display = OutputParameterName.From(parameterName).SafeDisplay();
                throw new InvalidOperationException(
                    $"SqlParameter '{display}' cannot be bound more than once to the same SqlCommand.");
            }
        }

        s_cloneSources.Add(clone, source);
    }

    public static void ValidateNoDuplicateReturnValue(SqlCommand command, SqlParameter candidate)
    {
        if (candidate.Direction != ParameterDirection.ReturnValue)
            return;

        foreach (SqlParameter existing in command.Parameters)
        {
            if (existing.Direction == ParameterDirection.ReturnValue)
                throw new InvalidOperationException("Only one ReturnValue parameter can be bound to a SqlCommand.");
        }
    }

    public static void ValidateSupportedOutputMetadata(SqlParameter parameter)
    {
        bool isCursorRef = IsCursorReferenceTypeName(parameter.TypeName)
            || IsCursorReferenceTypeName(parameter.UdtTypeName);
        ValidateSupportedOutputMetadata(
            parameter.ParameterName,
            parameter.SqlDbType,
            parameter.Direction,
            isCursorRef);
    }

    public static void ValidateSupportedOutputMetadata(
        string parameterName,
        SqlDbType sqlDbType,
        ParameterDirection direction)
        => ValidateSupportedOutputMetadata(parameterName, sqlDbType, direction, isCursorRef: false);

    public static void ValidateSupportedOutputMetadata(
        string parameterName,
        SqlDbType sqlDbType,
        ParameterDirection direction,
        bool isCursorRef)
    {
        if (direction == ParameterDirection.Input)
            return;

        OutputParameterName name = OutputParameterName.From(parameterName);

        if (direction == ParameterDirection.ReturnValue && sqlDbType != SqlDbType.Int)
        {
            throw new InvalidOperationException(
                $"ReturnValue parameter '{name.SafeDisplay()}' must use SqlDbType.Int.");
        }

        if (isCursorRef)
        {
            throw new InvalidOperationException(
                $"Output parameter '{name.SafeDisplay()}' is a SQL Server cursor-reference parameter, which Lib.Db does not support.");
        }

        if (sqlDbType is SqlDbType.Structured
            or SqlDbType.Text
            or SqlDbType.NText
            or SqlDbType.Image
            || IsCursorOutputType(sqlDbType))
        {
            throw new InvalidOperationException(
                $"Output parameter '{name.SafeDisplay()}' uses unsupported SqlDbType '{sqlDbType}'.");
        }
    }

    public static void CopyOutputValue(SqlParameter target, SqlParameter source)
    {
        if (source.Direction is ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue)
            CopyValueState(source, target);
    }

    public static SqlParameterValueState CaptureValueState(SqlParameter parameter)
        => new(parameter.Value, IsProviderValue(parameter.Value));

    public static void RestoreValueState(SqlParameter parameter, SqlParameterValueState state)
    {
        if (state.IsProviderValue && state.Value is not null and not DBNull)
        {
            parameter.SqlValue = state.Value;
            return;
        }

        parameter.Value = state.Value;
    }

    public static bool TryGetRegisteredSource(
        SqlParameter commandParameter,
        [NotNullWhen(true)] out SqlParameter? source)
        => s_cloneSources.TryGetValue(commandParameter, out source);

    public static bool IsRegisteredSource(SqlParameter commandParameter, SqlParameter source)
        => s_cloneSources.TryGetValue(commandParameter, out SqlParameter? registeredSource) &&
           ReferenceEquals(registeredSource, source);

    private static bool IsCursorOutputType(SqlDbType sqlDbType)
        => s_cursorSqlDbType is SqlDbType cursor && sqlDbType == cursor;

    private static bool IsCursorReferenceTypeName(string? typeName)
        => typeName is not null &&
           typeName.Trim().Equals("cursor", StringComparison.OrdinalIgnoreCase);

    private static void CopyTypeMetadata(SqlParameter source, SqlParameter clone)
    {
        clone.SqlDbType = source.SqlDbType;

        if (source.Size != 0)
            clone.Size = source.Size;
        if (source.Precision != 0)
            clone.Precision = source.Precision;
        if (source.Scale != 0)
            clone.Scale = source.Scale;
        if (!string.IsNullOrWhiteSpace(source.TypeName))
            clone.TypeName = source.TypeName;
        if (!string.IsNullOrWhiteSpace(source.UdtTypeName))
            clone.UdtTypeName = source.UdtTypeName;
        if (!string.IsNullOrWhiteSpace(source.XmlSchemaCollectionDatabase))
            clone.XmlSchemaCollectionDatabase = source.XmlSchemaCollectionDatabase;
        if (!string.IsNullOrWhiteSpace(source.XmlSchemaCollectionOwningSchema))
            clone.XmlSchemaCollectionOwningSchema = source.XmlSchemaCollectionOwningSchema;
        if (!string.IsNullOrWhiteSpace(source.XmlSchemaCollectionName))
            clone.XmlSchemaCollectionName = source.XmlSchemaCollectionName;

        clone.LocaleId = source.LocaleId;
        clone.CompareInfo = source.CompareInfo;
        clone.Offset = source.Offset;
        clone.ForceColumnEncryption = source.ForceColumnEncryption;
    }

    private static void CopyValueState(SqlParameter source, SqlParameter target)
    {
        object? value = source.Value;
        if (value is null or DBNull)
        {
            target.Value = DBNull.Value;
            return;
        }

        if (IsProviderValue(value))
        {
            target.SqlValue = value;
            return;
        }

        target.Value = value;
    }

    private static bool IsProviderValue(object value)
        => value is INullable or SqlBytes or SqlChars or SqlXml;
}

internal readonly record struct SqlParameterValueState(object? Value, bool IsProviderValue);
