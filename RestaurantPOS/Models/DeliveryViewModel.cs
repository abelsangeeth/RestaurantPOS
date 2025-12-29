namespace RestaurantPOS.Models
{
    public class DeliveryViewModel
    {
        public string RestaurantName { get; set; } = "Restaurant POS";
        public DeliveryCustomer Customer { get; set; }
        public List<MenuItem> MenuItems { get; set; } = new();
        public Order CurrentOrder { get; set; } = new() { Items = new List<OrderItem>() };
        public bool IsLoggedIn { get; set; }
    }
}