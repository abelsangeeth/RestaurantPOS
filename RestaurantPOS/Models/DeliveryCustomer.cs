namespace RestaurantPOS.Models
{
    // Remove this duplicate class definition if another DeliveryCustomer class exists in the same namespace.
    // If this is the intended definition, ensure there is no other DeliveryCustomer class in RestaurantPOS.Models.
    public class DeliveryCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}