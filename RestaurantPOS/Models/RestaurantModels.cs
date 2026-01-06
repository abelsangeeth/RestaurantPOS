using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;




// Models/User.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantPOS.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Username { get; set; }

        [Required]
        [StringLength(255)]
        public string Password { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [StringLength(50)]
        public string Role { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class MenuItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Range(0.01, 1000)]
        public decimal Price { get; set; }

        [Required]
        [StringLength(50)]
        public string Category { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        public bool IsAvailable { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public string itempicture { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
        public decimal Total { get; set; }
        public string Status { get; set; } = "pending";
        public string OrderType { get; set; } = "dine-in";
        public string CustomerName { get; set; }
        public string CustomerPhone { get; set; }
        public int? TableId { get; set; }
        public Table Table { get; set; }
        public string PaymentMethod { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string Notes { get; set; }

        public void CalculateAndSetTotal()
        {
            Total = Items?.Sum(item => item.Total) ?? 0;
        }

        [NotMapped]
        public decimal DisplayTotal => Items?.Sum(item => item.DisplayTotal) ?? Total;
    }

    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order Order { get; set; }
        public int MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public decimal Total { get; set; }
        public DateTime CreatedAt { get; set; }

        public void CalculateAndSetTotal()
        {
            Total = Price * Quantity;
        }

        [NotMapped]
        public decimal DisplayTotal => Price * Quantity;
    }


    public class Table
    {
        [Key]
        public int Id { get; set; }

        [StringLength(50)]
        public string TableName { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "available";

        public int? Capacity { get; set; }

        [StringLength(50)]
        public string Location { get; set; }

        [StringLength(50)]
        public string Type { get; set; }

        [StringLength(500)]
        public string Notes { get; set; }

        public int? CurrentOrderId { get; set; }
        public Order CurrentOrder { get; set; } // Navigation to Current Order

        // Navigation property for orders associated with this table
        public ICollection<Order> Orders { get; set; } = new List<Order>();

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
    }
    public class Reservation
    {
        [Key]
        public int Id { get; set; }

        public int TableId { get; set; }
        public Table Table { get; set; }

        [Required]
        [StringLength(100)]
        public string CustomerName { get; set; }

        [Required]
        [StringLength(20)]
        public string Phone { get; set; }

        [StringLength(10)]
        public string PartySize { get; set; }

        public DateTime Time { get; set; }

        [StringLength(500)]
        public string Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }
    public class ReservationRequest
    {
        public int TableId { get; set; }
        public string CustomerName { get; set; }
        public string CustomerPhone { get; set; }
        public string ReservationDate { get; set; }
        public string ReservationTime { get; set; }
        public int PartySize { get; set; }
        public string SpecialRequests { get; set; }
    }

    public class POSViewModel
    {
        public List<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
        public List<Table> Tables { get; set; } = new List<Table>();
        public List<Order> Orders { get; set; } = new List<Order>();
        public User CurrentUser { get; set; }
        public Order CurrentOrder { get; set; } = new Order();
        public string RestaurantName { get; set; }
        public string ContactInfo { get; set; } = "123-456-7890 | AL Farwaniyah Kuwait";

        // Dashboard stats
        public decimal TodaySales { get; set; }
        public int ActiveOrdersCount { get; set; }
        public int OccupiedTablesCount { get; set; }
        public int AvailableTablesCount { get; set; }
        public int PendingOrdersCount { get; set; }
        public int PreparingOrdersCount { get; set; }
        public int ReadyOrdersCount { get; set; }
        public int CompletedOrdersCount { get; set; }

        public decimal SalesChange { get; set; }
        public int AveragePrepTime { get; set; }
        public int ReservedTablesCount { get; set; }
        public string TopSellingItem { get; set; }

        public List<Order> RecentOrders { get; set; }

        public int PeakHour { get; set; }
        public decimal AverageOrderValue { get; set; }
        public int CustomerSatisfaction { get; set; }

        // Add this property to avoid division by zero
        public int TotalTables => Tables?.Count ?? 0;

    }
    public class Promotion
    {
        [Key]
        public int Id { get; set; }
        [StringLength(150)]
        public string Title { get; set; }
        [Column(TypeName = "decimal(5,2)")]
        public decimal DiscountPercentage { get; set; }
        [Column(TypeName = "datetime2(7)")]
        public DateTime StartDate { get; set; }
        [Column(TypeName = "datetime2(7)")]
        public DateTime? EndDate { get; set; }           // nullable datetime2(7)
        public bool IsActive { get; set; } = true;       // bit
    }
}