#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Lib.Db.Execution.Output;

/// <summary>
/// 호출자가 제공한 <see cref="SqlParameter"/>를 명령 소유 복사본으로 변환하고,
/// 실행 후 출력값만 호출자 파라미터로 되돌립니다.
/// </summary>
internal static class SqlParameterCloneFactory
{
    private static readonly ConditionalWeakTable<SqlParameter, SqlParameter> s_cloneSources = new();

    public static SqlParameter CloneForCommand(SqlParameter source, string parameterName)
    {
        var clone = new SqlParameter
        {
            ParameterName = parameterName.StartsWith('@') ? parameterName : "@" + parameterName,
            Direction = source.Direction,
            IsNullable = source.IsNullable,
            SourceColumn = source.SourceColumn,
            SourceColumnNullMapping = source.SourceColumnNullMapping,
            SourceVersion = source.SourceVersion,
            Value = source.Value ?? DBNull.Value
        };

        CopyTypeMetadata(source, clone);
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
        if (parameter.Direction == ParameterDirection.Input)
            return;

        OutputParameterName name = OutputParameterName.From(parameter.ParameterName);

        if (parameter.Direction == ParameterDirection.ReturnValue && parameter.SqlDbType != SqlDbType.Int)
        {
            throw new InvalidOperationException(
                $"ReturnValue parameter '{name.SafeDisplay()}' must use SqlDbType.Int.");
        }

        if (parameter.SqlDbType is SqlDbType.Structured or SqlDbType.Text or SqlDbType.NText or SqlDbType.Image)
        {
            throw new InvalidOperationException(
                $"Output parameter '{name.SafeDisplay()}' uses unsupported SqlDbType '{parameter.SqlDbType}'.");
        }
    }

    public static void CopyOutputValue(SqlParameter target, SqlParameter source)
    {
        if (source.Direction is ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue)
            target.Value = source.Value;
    }

    public static bool TryCopyOutputValueToRegisteredSource(SqlParameter commandParameter)
    {
        if (!s_cloneSources.TryGetValue(commandParameter, out SqlParameter? source))
            return false;

        CopyOutputValue(source, commandParameter);
        return true;
    }

    public static bool TryGetRegisteredSource(
        SqlParameter commandParameter,
        [NotNullWhen(true)] out SqlParameter? source)
        => s_cloneSources.TryGetValue(commandParameter, out source);

    public static bool IsRegisteredSource(SqlParameter commandParameter, SqlParameter source)
        => s_cloneSources.TryGetValue(commandParameter, out SqlParameter? registeredSource) &&
           ReferenceEquals(registeredSource, source);

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
    }
}
