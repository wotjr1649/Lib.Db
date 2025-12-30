// File: Lib.Db.Verification.Tests/Infrastructure/SqlExceptionFactory.cs
#nullable enable

using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Verification.Tests.Infrastructure;

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
            // 1. SqlErrorCollection 생성 (sealed class)
            // Ctor: internal SqlErrorCollection()
            var errorCollection = CreateErrorCollection();

            // 2. SqlError 생성 (sealed class)
            // Ctor: internal SqlError(int infoNumber, byte errorState, byte errorClass, string server, string errorMessage, string procedure, int lineNumber, Exception exception = null)
            var error = CreateSqlError(number, message);

            // 3. Collection에 Error 추가
            // Method: internal void Add(SqlError error)
            AddErrorToCollection(errorCollection, error);

            // 4. SqlException 생성 (sealed class)
            // Method: public static SqlException CreateException(SqlErrorCollection errorCollection, string serverVersion)
            // 또는 Reflection으로 _errors 필드 주입
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
        // SqlErrorCollection은 sealed이므로 GetUninitializedObject 사용 권장
        // 또는 internal 생성자 호출
        var type = typeof(SqlErrorCollection);
        var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        
        if (ctor != null)
        {
            return ctor.Invoke(null);
        }
        
        // Fallback: FormatterServices
        return FormatterServices.GetUninitializedObject(type);
    }

    private static object CreateSqlError(int number, string message)
    {
        var type = typeof(SqlError);
        // 생성자 시그니처 찾기 (버전마다 다를 수 있어 유연하게 검색)
        var ctors = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic);
        
        // 가장 파라미터가 많은 생성자 찾기 (정보가 풍부한 쪽 선호)
        // 보통: (int infoNumber, byte errorState, byte errorClass, string server, string errorMessage, string procedure, int lineNumber, Exception exception = null)
        foreach (var ctor in ctors)
        {
            var p = ctor.GetParameters();
            if (p.Length >= 7 && p[0].ParameterType == typeof(int)) // 첫 파라미터가 infoNumber
            {
                // 기본값 채우기
                var args = new object[p.Length];
                args[0] = number;                // infoNumber
                args[1] = (byte)0;               // errorState
                args[2] = (byte)10;              // errorClass (Severity)
                args[3] = "TestServer";          // server
                args[4] = message;               // errorMessage
                args[5] = "TestProc";            // procedure
                args[6] = 1;                     // lineNumber
                
                if (p.Length > 7) args[7] = null!; // exception (optional)

                return ctor.Invoke(args);
            }
        }

        throw new NotSupportedException("호환 가능한 SqlError 생성자를 찾을 수 없습니다.");
    }

    private static void AddErrorToCollection(object collection, object error)
    {
        var type = collection.GetType();
        var addMethod = type.GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        
        if (addMethod != null)
        {
            addMethod.Invoke(collection, new[] { error });
            return;
        }

        // List/ArrayList 기반일 경우
        if (collection is IList list)
        {
            list.Add(error);
            return;
        }
        
        throw new NotSupportedException("SqlErrorCollection.Add 메서드를 찾을 수 없습니다.");
    }

    private static SqlException CreateSqlException_ViaReflection(object errorCollection)
    {
        var type = typeof(SqlException);
        
        // 1. 정적 CreateException 메서드 시도 (가장 깔끔함)
        // internal static SqlException CreateException(SqlErrorCollection errorCollection, string serverVersion)
        var factoryMethod = type.GetMethod(
            "CreateException", 
            BindingFlags.Static | BindingFlags.NonPublic, 
            null, 
            new[] { typeof(SqlErrorCollection), typeof(string) }, 
            null);

        if (factoryMethod != null)
        {
            return (SqlException)factoryMethod.Invoke(null, new[] { errorCollection, "10.0.0" })!;
        }

        // 2. Fallback: FormatterServices로 깡통 객체 생성 후 Field 주입
        var ex = (SqlException)FormatterServices.GetUninitializedObject(type);
        
        // _errors 또는 Errors 필드/프로퍼티 찾기
        var field = type.GetField("_errors", BindingFlags.Instance | BindingFlags.NonPublic) 
                 ?? type.GetField("errors", BindingFlags.Instance | BindingFlags.NonPublic); // 이름 매핑 시도

        if (field != null)
        {
            field.SetValue(ex, errorCollection);
            return ex;
        }

        throw new NotSupportedException("SqlException._errors 필드를 주입할 수 없습니다.");
    }
}
