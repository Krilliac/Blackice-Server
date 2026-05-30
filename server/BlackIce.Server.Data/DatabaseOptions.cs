using Microsoft.EntityFrameworkCore;

namespace BlackIce.Server.Data;

public sealed class DatabaseOptions
{
    public string Provider { get; set; } = "Sqlite";              // "Sqlite" | "MySql"
    public string ConnectionString { get; set; } = "Data Source=blackice.db";

    /// <summary>Builds a context configured for the selected provider and ensures the schema exists.</summary>
    public BlackIceDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<BlackIceDbContext>();
        switch (Provider.ToLowerInvariant())
        {
            case "mysql":
                builder.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));
                break;
            case "sqlite":
            default:
                builder.UseSqlite(ConnectionString);
                break;
        }
        var ctx = new BlackIceDbContext(builder.Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }
}
