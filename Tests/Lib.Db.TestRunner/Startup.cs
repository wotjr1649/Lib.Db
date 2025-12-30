using Lib.Db.TestSuite.Live;
using Microsoft.Extensions.DependencyInjection;
using Lib.Db.Configuration;
using Xunit;

namespace Lib.Db.TestRunner;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHighPerformanceDb(options =>
        {
            options.ConnectionStrings = new Dictionary<string, string> 
            { 
                { "Default", "Server=(local);Database=LIBDB_VERIFICATION_TEST;Trusted_Connection=True;TrustServerCertificate=True;" } 
            };
            options.EnableResilience = true;
            // options.AddLibDbHybridCache(); // Static Baking Verify
        });
        
        // Register Test Fixtures/Classes
        services.AddTransient<LiveDbTests>();
    }
}
