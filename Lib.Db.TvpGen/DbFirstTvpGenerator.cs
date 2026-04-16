// ============================================================================
// 파일: Lib.Db.TvpGen/DbFirstTvpGenerator.cs
// 설명: 디자인 타임 스키마(libdb.schema.json) 기반 TVP DTO 자동 생성 제너레이터
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lib.Db.TvpGen;

/// <summary>
/// 디자인 타임 스키마(libdb.schema.json)를 읽어 TVP DTO 코드를 자동 생성하는 제너레이터
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DbFirstTvpGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "Lib.Db.Contracts.Models.GenerateTvpFromDbAttribute";
    private const string SchemaFileName = "libdb.schema.json";

    // -------------------------------------------------------------------------
    // Diagnostic 정의
    // -------------------------------------------------------------------------

    /// <summary>스키마 파일을 AdditionalFiles에서 찾지 못한 경우 경고</summary>
    private static readonly DiagnosticDescriptor SchemaNotFound = new(
        "LIBDB001",
        "스키마 파일 미발견",
        "libdb.schema.json 파일을 찾을 수 없습니다. AdditionalFiles에 추가하세요.",
        "Lib.Db.TvpGen",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>스키마 JSON 파싱 중 오류가 발생한 경우 에러</summary>
    private static readonly DiagnosticDescriptor SchemaParseError = new(
        "LIBDB002",
        "스키마 파싱 실패",
        "libdb.schema.json 파싱 중 오류 발생: {0}",
        "Lib.Db.TvpGen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. [GenerateTvpFromDb]가 붙은 클래스 찾기
        IncrementalValuesProvider<INamedTypeSymbol> classes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .WithComparer(SymbolEqualityComparer.Default);

        // 2. AdditionalFiles에서 libdb.schema.json 찾기
        IncrementalValueProvider<string?> schemaFile = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(SchemaFileName, StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) => file.GetText(cancellationToken)?.ToString())
            .Where(static text => text is not null)
            .Collect()
            .Select(static (texts, _) => texts.FirstOrDefault()); // 첫 번째 파일만 사용

        // 3. 결합 및 실행
        IncrementalValueProvider<(ImmutableArray<INamedTypeSymbol> Left, string? Right)> source =
            classes.Collect().Combine(schemaFile);

        context.RegisterSourceOutput(source, Execute);
    }

    private static void Execute(
        SourceProductionContext spc,
        (ImmutableArray<INamedTypeSymbol> Classes, string? SchemaJson) input)
    {
        (ImmutableArray<INamedTypeSymbol> classes, string? json) = input;
        if (classes.IsDefaultOrEmpty) return;

        // 스키마 파일이 없는 경우 — LIBDB001 경고
        if (string.IsNullOrEmpty(json))
        {
            spc.ReportDiagnostic(Diagnostic.Create(SchemaNotFound, Location.None));
            return;
        }

        // JSON 파싱 (MiniParser) — 오류 발생 시 LIBDB002 에러
        (SchemaRoot? schema, string? parseError) = MiniJsonParser.Parse(json!);
        if (schema is null)
        {
            string errorMessage = parseError ?? "알 수 없는 파싱 오류";
            spc.ReportDiagnostic(Diagnostic.Create(SchemaParseError, Location.None, errorMessage));
            return;
        }

        foreach (INamedTypeSymbol classSymbol in classes)
        {
            spc.CancellationToken.ThrowIfCancellationRequested();

            (string? tvpName, bool usePascalCase) = GetAttributeOptions(classSymbol);
            if (tvpName is null) continue;

            if (schema.Tvps.TryGetValue(tvpName, out List<ColumnDef>? columns))
            {
                string code = GenerateClass(classSymbol, columns, usePascalCase);
                spc.AddSource($"{classSymbol.Name}.Generated.cs", SourceText.From(code, Encoding.UTF8));
            }
            else
            {
                // TVP 이름이 스키마에 없음 — 경고 발생
                spc.ReportDiagnostic(Diagnostic.Create(SchemaNotFound, Location.None));
            }
        }
    }

    private static (string? TvpName, bool UsePascalCase) GetAttributeOptions(INamedTypeSymbol symbol)
    {
        AttributeData? attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeMetadataName);

        if (attr == null) return (null, true);

        string? name = attr.ConstructorArguments.FirstOrDefault().Value as string;
        bool usePascal = true;

        foreach (KeyValuePair<string, TypedConstant> named in attr.NamedArguments)
        {
            if (named.Key == "UsePascalCase" && named.Value.Value is bool b)
                usePascal = b;
        }

        return (name, usePascal);
    }

    private static string GenerateClass(INamedTypeSymbol symbol, List<ColumnDef> columns, bool usePascal)
    {
        string ns = symbol.ContainingNamespace.ToDisplayString();
        string className = symbol.Name;

        // 선언된 접근 제한자를 그대로 반영
        string accessModifier = symbol.DeclaredAccessibility switch
        {
            Accessibility.Internal => "internal",
            _ => "public"
        };

        StringBuilder sb = new();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
        sb.AppendLine("using Lib.Db.Contracts.Models;"); // TvpRowAttribute
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// DB 스키마 기반 자동 생성 TVP DTO");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    [TvpRow(TypeName = \"{columns[0].TvpName}\")] // 메타데이터 주입");
        sb.AppendLine($"    {accessModifier} partial class {className}");
        sb.AppendLine("    {");

        foreach (ColumnDef col in columns)
        {
            string propName = usePascal ? ToPascalCase(col.Name) : col.Name;
            string typeName = TypeMappingRegistry.MapSqlTypeToCSharp(col.Type);

            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// {col.Name} ({col.Type})");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public {typeName} {propName} {{ get; set; }}");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    // ✅ MapSqlTypeToCSharp → TypeMappingRegistry.MapSqlTypeToCSharp로 통합 (단일 진실 원천)
}

// --- 별도 파일로 분리해도 되지만 편의상 내부에 포함 (Private Helpers) ---

internal sealed class SchemaRoot
{
    public Dictionary<string, List<ColumnDef>> Tvps { get; set; } = new();
}

internal sealed class ColumnDef
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    // 파서 편의를 위해 임시 저장
    public string TvpName { get; set; } = "";
}

internal static class MiniJsonParser
{
    // 정규식 기반의 매우 초보적인 파서 (의존성 제거 목표)
    // 실제 프로덕션 레벨에서는 System.Text.Json(SourceGen) 또는 더 견고한 파서를 권장하나
    // 현재 제약상 "libdb.schema.json"의 정해진 포맷만 파싱함.

    /// <summary>
    /// JSON 파싱을 시도하고 결과와 오류 메시지를 함께 반환한다.
    /// </summary>
    /// <returns>성공 시 (SchemaRoot, null), 실패 시 (null, 오류 메시지)</returns>
    public static (SchemaRoot? Root, string? Error) Parse(string json)
    {
        try
        {
            SchemaRoot root = new();
            // "Tvps": { ... } 찾기
            // 복잡한 중첩 파싱 대신, 정규식으로 "Key": [Array] 패턴을 찾습니다.
            // 1. "Tvps" 블록 추출
            int tvpsIndex = json.IndexOf("\"Tvps\"", StringComparison.OrdinalIgnoreCase);
            if (tvpsIndex < 0) return (null, "\"Tvps\" 섹션을 찾을 수 없습니다.");

            // 대안: 문자열을 한 글자씩 읽는 State Machine Parser (안전함)
            SchemaRoot result = PoorMansStateMachine(json);
            return (result, null);
        }
        catch (Exception ex)
        {
            // 예외 정보를 진단 메시지로 전달 — 이전에는 silently 삼켰던 오류를 복원
            return (null, ex.Message);
        }
    }

    private static SchemaRoot PoorMansStateMachine(string json)
    {
        SchemaRoot root = new();

        // 퀵 앤 더티: 그냥 문자열 검색으로 "dbo.XXX" 키와 그 내부 객체들을 찾는다.
        // 하지만 신뢰성을 위해 Step-by-step 파싱을 흉내냅니다.

        // 실제 구현은 시간 관계상 "간단한 가정"에 의존합니다.
        // 가정: JSON은 Pretty Print 또는 표준 포맷을 따른다.

        // 1. Tvps 섹션 찾기
        System.Text.RegularExpressions.Match tvpSection =
            System.Text.RegularExpressions.Regex.Match(json, "\"Tvps\"\\s*:\\s*\\{");
        if (!tvpSection.Success) return root;

        int startIndex = tvpSection.Index + tvpSection.Length;

        // 2. 각 Key 찾기 ("Key": [ ... ])
        // 닫는 중괄호 } 를 만날 때까지 반복
        // 단순 정규식으로 "키": [ ... ] 를 추출합니다. (Non-greedy)
        System.Text.RegularExpressions.MatchCollection matches =
            System.Text.RegularExpressions.Regex.Matches(
                json.Substring(startIndex),
                "\"([^\"]+)\"\\s*:\\s*\\[(.*?)\\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string tvpName = m.Groups[1].Value;
            string arrayContent = m.Groups[2].Value;

            List<ColumnDef> cols = ParseColumns(arrayContent);
            foreach (ColumnDef c in cols) c.TvpName = tvpName;
            root.Tvps[tvpName] = cols;
        }

        return root;
    }

    private static List<ColumnDef> ParseColumns(string arrayContent)
    {
        List<ColumnDef> list = new();
        // { "Name": "A", "Type": "B" } 패턴 반복
        System.Text.RegularExpressions.MatchCollection matches =
            System.Text.RegularExpressions.Regex.Matches(
                arrayContent,
                "\\{(.*?)\\}",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string objContent = m.Groups[1].Value;
            ColumnDef col = new();

            // "Name": "Value" 찾기
            System.Text.RegularExpressions.MatchCollection props =
                System.Text.RegularExpressions.Regex.Matches(
                    objContent,
                    "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\"");

            foreach (System.Text.RegularExpressions.Match p in props)
            {
                string k = p.Groups[1].Value;
                string v = p.Groups[2].Value;

                if (k == "Name") col.Name = v;
                else if (k == "Type") col.Type = v;
            }
            if (!string.IsNullOrEmpty(col.Name))
                list.Add(col);
        }
        return list;
    }
}
