// Data/ApplicationDbContext.cs
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Models;

namespace RestaurantPOS.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Table> Tables { get; set; }
        public DbSet<Reservation> Reservations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure User entity
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Username).IsUnique();
                entity.Property(u => u.Username).IsRequired().HasMaxLength(50);
                entity.Property(u => u.Password).IsRequired().HasMaxLength(255);
                entity.Property(u => u.Name).IsRequired().HasMaxLength(100);
                entity.Property(u => u.Role).IsRequired().HasMaxLength(50);
            });

            // Configure MenuItem entity
            modelBuilder.Entity<MenuItem>(entity =>
            {
                entity.Property(m => m.Name).IsRequired().HasMaxLength(100);
                entity.Property(m => m.Category).IsRequired().HasMaxLength(50);
                entity.Property(m => m.Price).HasColumnType("decimal(18,2)");
                entity.Property(m => m.Description).HasMaxLength(500);
            });

            // Configure Order entity
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasIndex(o => o.OrderNumber).IsUnique();
                entity.Property(o => o.OrderNumber).IsRequired().HasMaxLength(50);
                entity.Property(o => o.Status).IsRequired().HasMaxLength(20);
                entity.Property(o => o.OrderType).IsRequired().HasMaxLength(20);
                entity.Property(o => o.CustomerName).IsRequired().HasMaxLength(100);
                entity.Property(o => o.CustomerPhone).HasMaxLength(20);
                entity.Property(o => o.PaymentMethod).HasMaxLength(50);
                entity.Property(o => o.Total).HasColumnType("decimal(18,2)");

                // Order -> OrderItems (One-to-Many)
                entity.HasMany(o => o.Items)
                      .WithOne(oi => oi.Order)
                      .HasForeignKey(oi => oi.OrderId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Order -> Table (Many-to-One) - Order has optional Table
                entity.HasOne(o => o.Table)
                      .WithMany(t => t.Orders) // Table has many Orders
                      .HasForeignKey(o => o.TableId)
                      .IsRequired(false) // Table is optional for orders (takeaway/delivery)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // Configure OrderItem entity
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.Property(oi => oi.Name).IsRequired().HasMaxLength(100);
                entity.Property(oi => oi.Price).HasColumnType("decimal(18,2)");
                entity.Property(oi => oi.Total).HasColumnType("decimal(18,2)");

                // OrderItem -> MenuItem (Many-to-One)
                entity.HasOne(oi => oi.MenuItem)
                      .WithMany()
                      .HasForeignKey(oi => oi.MenuItemId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Configure Table entity
            modelBuilder.Entity<Table>(entity =>
            {
                entity.Property(t => t.Status).IsRequired().HasMaxLength(20);
                entity.Property(t => t.Location).HasMaxLength(50);
                entity.Property(t => t.Type).HasMaxLength(50);
                entity.Property(t => t.Notes).HasMaxLength(500);

                // Table -> Orders (One-to-Many) - already configured in Order entity
                // Table -> CurrentOrder (One-to-One) - Table has one current order
                entity.HasOne(t => t.CurrentOrder)
                      .WithOne() // No navigation property back from Order to Table's CurrentOrder
                      .HasForeignKey<Table>(t => t.CurrentOrderId)
                      .IsRequired(false) // CurrentOrder is optional
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // Configure Reservation entity
            modelBuilder.Entity<Reservation>(entity =>
            {
                entity.Property(r => r.CustomerName).IsRequired().HasMaxLength(100);
                entity.Property(r => r.Phone).IsRequired().HasMaxLength(20);
                entity.Property(r => r.PartySize).HasMaxLength(10);
                entity.Property(r => r.Notes).HasMaxLength(500);

                // Reservation -> Table (Many-to-One)
                entity.HasOne(r => r.Table)
                      .WithMany() // Table can have many reservations over time
                      .HasForeignKey(r => r.TableId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Seed initial data
            SeedData(modelBuilder);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // Seed Users
            modelBuilder.Entity<User>().HasData(
                new User { Id = 1, Username = "manager", Password = "12345", Name = "Restaurant Manager", Role = "Manager", CreatedAt = DateTime.Now, IsActive = true },
                new User { Id = 2, Username = "staff", Password = "12345", Name = "Restaurant Staff", Role = "Staff", CreatedAt = DateTime.Now, IsActive = true },
                new User { Id = 3, Username = "user", Password = "12345", Name = "Customer User", Role = "User", CreatedAt = DateTime.Now, IsActive = true }
            );

            // Seed Menu Items
            modelBuilder.Entity<MenuItem>().HasData(
                new MenuItem { Id = 1, Name = "Margherita Pizza", Price = 18.99m, Category = "Pizza", Description = "Classic pizza with tomato sauce, mozzarella, and fresh basil", IsAvailable = true, CreatedAt = DateTime.Now },
                new MenuItem { Id = 2, Name = "Caesar Salad", Price = 12.99m, Category = "Salads", Description = "Crisp romaine lettuce with Caesar dressing and croutons", IsAvailable = true, CreatedAt = DateTime.Now },
                new MenuItem { Id = 3, Name = "Grilled Chicken", Price = 22.99m, Category = "Mains", Description = "Tender grilled chicken breast with herbs and spices", IsAvailable = true, CreatedAt = DateTime.Now },
                new MenuItem { Id = 4, Name = "Fish & Chips", Price = 19.99m, Category = "Mains", Description = "Beer-battered fish with golden fries", IsAvailable = true, CreatedAt = DateTime.Now },
                new MenuItem { Id = 5, Name = "Pasta Carbonara", Price = 16.99m, Category = "Pasta", Description = "Creamy pasta with bacon, eggs, and parmesan cheese", IsAvailable = true, CreatedAt = DateTime.Now },
                new MenuItem { Id = 6, Name = "Chocolate Cake", Price = 8.99m, Category = "Desserts", Description = "Rich chocolate cake with chocolate frosting", IsAvailable = true, CreatedAt = DateTime.Now },
                new MenuItem { Id = 7, Name = "Coffee", Price = 4.99m, Category = "Beverages", Description = "Freshly brewed coffee", IsAvailable = true, CreatedAt = DateTime.Now },
                new MenuItem { Id = 8, Name = "Wine Glass", Price = 9.99m, Category = "Beverages", Description = "House wine selection", IsAvailable = true, CreatedAt = DateTime.Now }
            );

            // Seed Tables
            for (int i = 1; i <= 12; i++)
            {
                modelBuilder.Entity<Table>().HasData(
                    new Table { Id = i, Capacity = 4, Location = "Main Dining", Type = "Regular", Status = "available", CreatedAt = DateTime.Now }
                );
            }
        }
    }
}