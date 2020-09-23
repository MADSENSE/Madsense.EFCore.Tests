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
        [InlineData(ServiceLifetime.Singleton, false, false, 1, 10)]
        [InlineData(ServiceLifetime.Singleton, false, false, 2, 10)] // SafeHandle cannot be null. (Parameter 'pHandle')
        [InlineData(ServiceLifetime.Singleton, false, true, 1, 10)]
        [InlineData(ServiceLifetime.Singleton, false, true, 2, 10)]
        [InlineData(ServiceLifetime.Singleton, true, false, 1, 10)]
        [InlineData(ServiceLifetime.Singleton, true, false, 2, 10)] // Invalid attempt to call GetInt32 when reader is closed.
        [InlineData(ServiceLifetime.Singleton, true, true, 1, 10)]
        [InlineData(ServiceLifetime.Singleton, true, true, 2, 10)]
        [InlineData(ServiceLifetime.Transient, false, false, 1, 10)]
        [InlineData(ServiceLifetime.Transient, false, false, 2, 10)]
        [InlineData(ServiceLifetime.Transient, false, true, 1, 10)]
        [InlineData(ServiceLifetime.Transient, false, true, 2, 10)]
        [InlineData(ServiceLifetime.Transient, true, false, 1, 10)]
        [InlineData(ServiceLifetime.Transient, true, false, 2, 10)]
        [InlineData(ServiceLifetime.Transient, true, true, 1, 10)]
        [InlineData(ServiceLifetime.Transient, true, true, 2, 10)]
        public async Task Sqlite_ConcurrentSave_Test(ServiceLifetime optionsLifeTime, bool encrypted, bool openConnection, int concurrentSaveCount, int insertOperationsCount)
        {
            // Prepare
            var serviceCollection = new ServiceCollection();

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"Filename={Guid.NewGuid()}.db",
                Password = encrypted ? "12345" : null
            }.ToString();

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

        [Theory]
        [InlineData(ServiceLifetime.Singleton, false, false, 1, 10)]
        [InlineData(ServiceLifetime.Singleton, false, false, 2, 10)] // Object reference not set to an instance of an object.
        [InlineData(ServiceLifetime.Singleton, false, true, 1, 10)]
        [InlineData(ServiceLifetime.Singleton, false, true, 2, 10)] // SQLite Error 1: 'cannot start a transaction within a transaction'.
        [InlineData(ServiceLifetime.Singleton, true, false, 1, 10)]
        [InlineData(ServiceLifetime.Singleton, true, false, 2, 10)] // BeginTransaction can only be called when the connection is open.
        [InlineData(ServiceLifetime.Singleton, true, true, 1, 10)]
        [InlineData(ServiceLifetime.Singleton, true, true, 2, 10)] // SQLite Error 1: 'cannot start a transaction within a transaction'.
        [InlineData(ServiceLifetime.Transient, false, false, 1, 10)]
        [InlineData(ServiceLifetime.Transient, false, false, 2, 10)]
        [InlineData(ServiceLifetime.Transient, false, true, 1, 10)]
        [InlineData(ServiceLifetime.Transient, false, true, 2, 10)]
        [InlineData(ServiceLifetime.Transient, true, false, 1, 10)]
        [InlineData(ServiceLifetime.Transient, true, false, 2, 10)]
        [InlineData(ServiceLifetime.Transient, true, true, 1, 10)]
        [InlineData(ServiceLifetime.Transient, true, true, 2, 10)]
        public async Task Sqlite_ConcurrentRead_Test(ServiceLifetime optionsLifeTime, bool encrypted, bool openConnection, int concurrentReadCount, int readOperationsCount)
        {
            // Prepare
            var serviceCollection = new ServiceCollection();

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"Filename={Guid.NewGuid()}.db",
                Password = encrypted ? "12345" : null
            }.ToString();

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
                await initContext.AddRangeAsync(Enumerable.Range(0, readOperationsCount).Select(i => new BasicModel{Name = Guid.NewGuid().ToString()}));
                await initContext.SaveChangesAsync();
            }
            
            // Act
            var addDataFunc = new Func<Task>(async () =>
            {
                for (var i = 0; i < readOperationsCount; i++)
                {
                    await using var context = serviceProvider.GetRequiredService<AppContext>();
                    {
                        await context.BasicModels.ToArrayAsync();
                    }
                }
            });

            var concurrentTasks = Enumerable.Range(0, concurrentReadCount).Select(i => Task.Run(() => addDataFunc()));
            await Task.WhenAll(concurrentTasks);
            
            // Assert
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
