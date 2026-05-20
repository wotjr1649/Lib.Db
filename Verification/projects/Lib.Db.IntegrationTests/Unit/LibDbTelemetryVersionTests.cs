// ============================================================================
// 파일: Unit/LibDbTelemetryVersionTests.cs
// 설명: Lib.Db 릴리스 버전 메타데이터 검증 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.IO;
using System.Xml.Linq;
using Lib.Db.Diagnostics;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class LibDbTelemetryVersionTests
{
    [Fact]
    public void Version_ShouldMatchLibDbProjectVersionProperty()
    {
        string projectVersion = LoadLibDbProjectVersion();

        LibDbTelemetry.Version.Should().Be(projectVersion);
        LibDbTelemetry.ActivitySource.Version.Should().Be(projectVersion);
        LibDbTelemetry.Meter.Version.Should().Be(projectVersion);
    }

    private static string LoadLibDbProjectVersion()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "Lib.Db", "Lib.Db.csproj");
            if (File.Exists(candidate))
            {
                XDocument document = XDocument.Load(candidate);
                string? version = document
                    .Descendants("Version")
                    .Select(static element => element.Value.Trim())
                    .SingleOrDefault(static value => value.Length > 0);

                return version ?? throw new InvalidOperationException("Lib.Db project Version property was not found.");
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Lib.Db.csproj was not found from the test output path.");
    }
}
