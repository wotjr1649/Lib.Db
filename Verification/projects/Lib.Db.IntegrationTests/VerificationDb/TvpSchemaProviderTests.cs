using System.Data;
using Lib.Db.Execution.Tvp;
using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

public sealed class TvpSchemaProviderTests
{
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "TvpSchemaProvider")]
    public async Task GetSchemaAsync_ShouldReturnDescriptorWithFingerprint()
    {
        const string typeName = "dbo.LibDb_TvpSchemaProviderRows";
        MultiDbFixture fixture = new();
        bool initialized = false;

        try
        {
            await fixture.InitializeAsync();
            initialized = true;
            await fixture.Verification.Sql($@"
                DROP TYPE IF EXISTS {typeName};
                CREATE TYPE {typeName} AS TABLE
                (
                    Id INT NOT NULL,
                    Sku NVARCHAR(64) NULL,
                    Amount DECIMAL(18, 2) NOT NULL
                );
            ").ExecuteAsync();

            using IServiceScope scope = fixture.Services.CreateScope();
            ITvpSchemaProvider provider = scope.ServiceProvider.GetRequiredService<ITvpSchemaProvider>();

            TvpSchemaDescriptor descriptor = await provider.GetSchemaAsync(
                TvpTypeName.Parse(typeName),
                TestConnectionStrings.Verification,
                TestContext.Current.CancellationToken);

            descriptor.TypeName.FullName.Should().Be(typeName);
            descriptor.Columns.Should().HaveCount(3);
            descriptor.Columns.Select(column => column.Name).Should().Equal("Id", "Sku", "Amount");
            descriptor.Columns[1].SqlDbType.Should().Be(SqlDbType.NVarChar);
            descriptor.Columns[1].IsNullable.Should().BeTrue();
            descriptor.Fingerprint.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            if (initialized)
            {
                await fixture.Verification.Sql($"DROP TYPE IF EXISTS {typeName}").ExecuteAsync();
                await fixture.DisposeAsync();
            }
        }
    }
}
