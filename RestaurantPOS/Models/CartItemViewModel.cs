namespace RestaurantPOS.Models
{
    public class CartItemViewModel
    {
        // Define properties as needed
        public int Id { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }

        public string itempicture { get; set; }
    }
}