using Lib.Db.TestSuite.Live;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lib.Db.TestRunner;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // 수동으로 Configuration 빌드 (TestSuite의 appsettings.json 사용)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // AddLibDb를 사용하여 appsettings.json 설정 적용
        // Root Configuration을 넘겨야 "ConnectionStrings" 섹션 참조(Alias) 기능이 동작합니다.
        services.AddLibDb(configuration);

        // Register Test Fixtures/Classes
        services.AddSingleton<LiveDbFixture>();
    }
}
