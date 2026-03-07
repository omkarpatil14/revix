using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Reflection;

namespace Revix.Infrastructure;

public class RevixDbContextFactory : IDesignTimeDbContextFactory<RevixDbContext>
{
    public RevixDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(basePath, "../revix.API"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .AddUserSecrets(Assembly.Load("revix.API"))
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<RevixDbContext>();
        optionsBuilder.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));

        return new RevixDbContext(optionsBuilder.Options);
    }
}