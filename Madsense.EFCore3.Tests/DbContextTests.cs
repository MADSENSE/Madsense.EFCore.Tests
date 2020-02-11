using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Madsense.EFCore.Tests
{
    public class DbContextTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RecursiveIncludeTest(bool trackingEnabled)
        {
            // Prepare
            var options = new DbContextOptionsBuilder<AppContext>()
                .UseSqlite("Data Source=myTestDatabase.db")
                .Options;

            using (var initContext = new AppContext(options))
            {
                await initContext.Database.EnsureDeletedAsync();
                await initContext.Database.EnsureCreatedAsync();

                await initContext.Products.AddRangeAsync(
                    new Product
                    {
                        Id = 2,
                        Items =
                        {
                            new ProductItem { ChildProductId = 2 }
                        }
                    });
                await initContext.SaveChangesAsync();
            }

            // Act
            using var testContext = new AppContext(options);
            testContext.ChangeTracker.QueryTrackingBehavior = trackingEnabled ? QueryTrackingBehavior.TrackAll : QueryTrackingBehavior.NoTracking;
            var products = await testContext.Products.Include(p => p.Items).ThenInclude(i => i.ChildProduct).ToArrayAsync();

            // Assert
            var product = Assert.Single(products);
            Assert.Equal(2, product.Id);
            var item = Assert.Single(product.Items);
            Assert.NotNull(item.Product);
            Assert.NotNull(item.ChildProduct);
            Assert.Equal(2, item.ChildProduct.Id);
            Assert.NotEmpty(item.ChildProduct.Items);       // (EF3 Failed-NoTracking) Check recursive object populated
            Assert.Contains(item.ChildProduct, products);   // (EF3 Failed-NoTracking) Check it's same instance
        }
    }

    public class AppContext : DbContext
    {
        public DbSet<Product> Products { get; protected set; }
        public DbSet<ProductItem> ProductItems { get; protected set; }

        public AppContext(DbContextOptions<AppContext> options)
            : base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Product>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            builder.Entity<ProductItem>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Product)
                    .WithMany(e => e.Items)
                    .HasForeignKey(e => e.ProductId);
                entity.HasOne(e => e.ChildProduct)
                    .WithMany()
                    .HasForeignKey(e => e.ChildProductId);
            });
        }
    }

    public class Product
    {
        public int Id { get; set; }

        public ICollection<ProductItem> Items { get; } = new ObservableHashSet<ProductItem>();
    }

    public class ProductItem
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public int? ChildProductId { get; set; }

        public Product Product { get; set; }
        public Product ChildProduct { get; set; }
    }
}
