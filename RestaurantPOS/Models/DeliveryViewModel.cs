using System.Collections.Generic;
using RestaurantPOS.Models; // Add this if CartItemViewModel is in this namespace

namespace RestaurantPOS.Models
{
    public class DeliveryViewModel
    {
        public bool IsLoggedIn { get; set; }
        public DeliveryCustomer Customer { get; set; }
        public List<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
        public Order CurrentOrder { get; set; } = new Order();
        public string RestaurantName { get; set; }
    }

    public class OrderHistoryViewModel
    {
        public string CustomerName { get; set; }
        public List<Order> Orders { get; set; } = new List<Order>();
    }
}