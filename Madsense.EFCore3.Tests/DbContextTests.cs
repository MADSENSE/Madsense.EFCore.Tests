using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Madsense.EFCore.Tests
{
    public class DbContextTests
    {
        [Fact]
        public async Task AppContextTest()
        {
            // Prepare
            var options = new DbContextOptionsBuilder<AppContext>()
                .UseSqlite("Data Source=myTestDatabase.db")
                .Options;

            using (var initContext = new AppContext(options))
            {
                await initContext.Database.EnsureDeletedAsync();
                await initContext.Database.EnsureCreatedAsync();

                await initContext.BasicModels.AddRangeAsync(
                    new BasicModel
                    {
                        Childs =
                        {
                            new ChildModel(),
                            new ChildModel()
                        }
                    });
                await initContext.SaveChangesAsync();
            }

            // Act
            using var testContext = new AppContext(options);

            // Assert
            Assert.Single(await testContext.BasicModels.ToListAsync());
        }
    }

    public class AppContext : DbContext
    {
        public DbSet<BasicModel> BasicModels { get; protected set; }
        public DbSet<ChildModel> ChildModels { get; protected set; }

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

                entity.HasMany(e => e.Childs)
                    .WithOne(e => e.Basic)
                    .HasForeignKey(e => e.BasicModelId);
            });

            builder.Entity<ChildModel>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }

    public class BasicModel
    {
        public int Id { get; set; }

        public ICollection<ChildModel> Childs { get; } = new HashSet<ChildModel>();
    }

    public class ChildModel
    {
        public int Id { get; set; }
        public int BasicModelId { get; set; }

        public BasicModel Basic { get; set; }
    }
}
