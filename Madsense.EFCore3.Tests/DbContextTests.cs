using System;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Madsense.EFCore.Tests
{
    public class DbContextTests
    {
        [Theory]
        [InlineData(ServiceLifetime.Singleton, false, 1, 10)]
        [InlineData(ServiceLifetime.Transient, false, 3, 10)]
        [InlineData(ServiceLifetime.Singleton, false, 3, 10)] // SQLite Error 1: 'cannot rollback - no transaction is active'.
        [InlineData(ServiceLifetime.Transient, true, 3, 10)]
        [InlineData(ServiceLifetime.Singleton, true, 3, 10)] // SQLite Error 1: 'cannot start a transaction within a transaction'.
        public async Task Sqlite_ConcurrentSave_Test(ServiceLifetime optionsLifeTime, bool openConnection, int concurrentSaveCount, int insertOperationsCount)
        {
            // Prepare
            var serviceCollection = new ServiceCollection();
            var connectionString = $"Filename={Guid.NewGuid()}.db";

            serviceCollection.AddDbContext<AppContext>((s, builder) =>
            {
                var connection = new SqliteConnection(connectionString);

                if (openConnection)
                    connection.Open();

                builder.UseSqlite(connection);
            }, ServiceLifetime.Transient, optionsLifeTime);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            await using (var initContext = serviceProvider.GetRequiredService<AppContext>())
            {
                await initContext.Database.EnsureCreatedAsync();
            }

            // Act
            var addDataFunc = new Func<Task>(async () =>
            {
                for (var i = 0; i < insertOperationsCount; i++)
                {
                    await using var context = serviceProvider.GetRequiredService<AppContext>();
                    {
                        await context.AddAsync(new BasicModel{Name = Guid.NewGuid().ToString()});
                        await context.SaveChangesAsync();
                    }
                }
            });

            var concurrentTasks = Enumerable.Range(0, concurrentSaveCount).Select(i => Task.Run(() => addDataFunc()));
            await Task.WhenAll(concurrentTasks);
            
            // Assert
            await using var assertContext = serviceProvider.GetRequiredService<AppContext>();
            Assert.Equal(concurrentSaveCount*insertOperationsCount, assertContext.BasicModels.Count());
        }
    }

    public class AppContext : DbContext
    {
        public DbSet<BasicModel> BasicModels { get; protected set; }

        public AppContext(DbContextOptions<AppContext> options)
            : base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<BasicModel>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }

    public class BasicModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
