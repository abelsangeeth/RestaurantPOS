namespace RestaurantPOS.Models
{
    public class RestaurantTable
    {
        public int Id { get; set; }
        public string TableName { get; set; }
        public string Status { get; set; }
        public int Seats { get; set; }
    }
}