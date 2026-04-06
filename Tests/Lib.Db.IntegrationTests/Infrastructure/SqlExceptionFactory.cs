// ============================================================================
// 파일: Infrastructure/SqlExceptionFactory.cs
// 설명: 테스트 전용 SqlException UNSAFE Factory (Reflection 기반)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// [Warning] 테스트 전용 UNSAFE Factory
/// <para>
/// SqlException(Sealed)을 Reflection/FormatterServices로 강제 생성합니다.<br/>
/// 목적: Deadlock(1205) 등 특정 SQL 에러 상황을 결정론적으로 시뮬레이션.<br/>
/// 주의: Microsoft.Data.SqlClient 내부 구현 변경 시 깨질 수 있음. (Preflight Test 필수)
/// </para>
/// </summary>
internal static class SqlExceptionFactory
{
    public static SqlException Create(int number, string message = "Comparison Failure Injection")
    {
        try
        {
            var errorCollection = CreateErrorCollection();
            var error = CreateSqlError(number, message);
            AddErrorToCollection(errorCollection, error);
            return CreateSqlException_ViaReflection(errorCollection);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[SqlExceptionFactory] SqlException 생성 실패 ({ex.Message}). " +
                "Microsoft.Data.SqlClient 버전 호환성 문제일 수 있습니다.", ex);
        }
    }

    private static object CreateErrorCollection()
    {
        Type type = typeof(SqlErrorCollection);
        ConstructorInfo? ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        if (ctor != null)
        {
            return ctor.Invoke(null);
        }

        return FormatterServices.GetUninitializedObject(type);
    }

    private static object CreateSqlError(int number, string message)
    {
        Type type = typeof(SqlError);
        ConstructorInfo[] ctors = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic);

        foreach (ConstructorInfo ctor in ctors)
        {
            ParameterInfo[] p = ctor.GetParameters();
            if (p.Length >= 7 && p[0].ParameterType == typeof(int))
            {
                object[] args = new object[p.Length];
                args[0] = number;
                args[1] = (byte)0;
                args[2] = (byte)10;
                args[3] = "TestServer";
                args[4] = message;
                args[5] = "TestProc";
                args[6] = 1;

                if (p.Length > 7) args[7] = null!;

                return ctor.Invoke(args);
            }
        }

        throw new NotSupportedException("호환 가능한 SqlError 생성자를 찾을 수 없습니다.");
    }

    private static void AddErrorToCollection(object collection, object error)
    {
        Type type = collection.GetType();
        MethodInfo? addMethod = type.GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (addMethod != null)
        {
            addMethod.Invoke(collection, [error]);
            return;
        }

        if (collection is IList list)
        {
            list.Add(error);
            return;
        }

        throw new NotSupportedException("SqlErrorCollection.Add 메서드를 찾을 수 없습니다.");
    }

    private static SqlException CreateSqlException_ViaReflection(object errorCollection)
    {
        Type type = typeof(SqlException);

        MethodInfo? factoryMethod = type.GetMethod(
            "CreateException",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(SqlErrorCollection), typeof(string)],
            null);

        if (factoryMethod != null)
        {
            return (SqlException)factoryMethod.Invoke(null, [errorCollection, "10.0.0"])!;
        }

        SqlException ex = (SqlException)FormatterServices.GetUninitializedObject(type);

        FieldInfo? field = type.GetField("_errors", BindingFlags.Instance | BindingFlags.NonPublic)
                 ?? type.GetField("errors", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field != null)
        {
            field.SetValue(ex, errorCollection);
            return ex;
        }

        throw new NotSupportedException("SqlException._errors 필드를 주입할 수 없습니다.");
    }
}
