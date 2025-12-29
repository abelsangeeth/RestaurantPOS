using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantPOS.Models;
using System.Text.Json;

namespace RestaurantPOS.Controllers
{
    public class DeliveryController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _connectionString;

        public DeliveryController(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IActionResult> Index()
        {
            var customerId = HttpContext.Session.GetInt32("DeliveryCustomerId");
            var viewModel = new DeliveryViewModel();

            if (customerId != null)
            {
                var customer = await GetCustomerById(customerId.Value);
                if (customer != null)
                {
                    viewModel.Customer = customer;
                    viewModel.IsLoggedIn = true;
                    viewModel.MenuItems = await GetMenuItems();
                    viewModel.CurrentOrder = GetCurrentDeliveryOrder();
                }
            }

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Login(string phone, string name, string address, string email)
        {
            if (string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(address))
            {
                TempData["Error"] = "Please fill in all required fields";
                return RedirectToAction("Index");
            }

            var customer = await GetOrCreateCustomer(phone, name, address, email);
            
            if (customer != null)
            {
                HttpContext.Session.SetInt32("DeliveryCustomerId", customer.Id);
                HttpContext.Session.SetString("DeliveryCustomerName", customer.Name);
                HttpContext.Session.SetString("DeliveryCustomerPhone", customer.Phone);
                HttpContext.Session.SetString("DeliveryCustomerAddress", customer.Address);
                
                TempData["Success"] = $"Welcome, {customer.Name}! Start adding items to your order.";
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Remove("DeliveryCustomerId");
            HttpContext.Session.Remove("DeliveryCustomerName");
            HttpContext.Session.Remove("DeliveryCustomerPhone");
            HttpContext.Session.Remove("DeliveryCustomerAddress");
            HttpContext.Session.Remove("DeliveryOrder");

            TempData["Success"] = "Logged out successfully";
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> AddToCart(int menuItemId, int quantity = 1)
        {
            var customerId = HttpContext.Session.GetInt32("DeliveryCustomerId");
            if (customerId == null)
            {
                return Json(new { success = false, message = "Please login first" });
            }

            var menuItem = await GetMenuItemById(menuItemId);
            if (menuItem == null)
            {
                return Json(new { success = false, message = "Item not found" });
            }

            var order = GetCurrentDeliveryOrder();
            var existingItem = order.Items.FirstOrDefault(i => i.MenuItemId == menuItemId);

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
                existingItem.Total = existingItem.Price * existingItem.Quantity;
            }
            else
            {
                order.Items.Add(new OrderItem
                {
                    MenuItemId = menuItem.Id,
                    Name = menuItem.Name,
                    Price = menuItem.Price,
                    Quantity = quantity,
                    Total = menuItem.Price * quantity
                });
            }

            order.Total = order.Items.Sum(i => i.Total);
            SaveCurrentDeliveryOrder(order);

            return Json(new { success = true, cartCount = order.Items.Sum(i => i.Quantity), total = order.Total });
        }

        [HttpPost]
        public IActionResult RemoveFromCart(int menuItemId)
        {
            var order = GetCurrentDeliveryOrder();
            var item = order.Items.FirstOrDefault(i => i.MenuItemId == menuItemId);

            if (item != null)
            {
                order.Items.Remove(item);
                order.Total = order.Items.Sum(i => i.Total);
                SaveCurrentDeliveryOrder(order);
            }

            return Json(new { success = true, cartCount = order.Items.Sum(i => i.Quantity), total = order.Total });
        }

        [HttpPost]
        public async Task<IActionResult> PlaceOrder(string notes)
        {
            var customerId = HttpContext.Session.GetInt32("DeliveryCustomerId");
            if (customerId == null)
            {
                return Json(new { success = false, message = "Please login first" });
            }

            var order = GetCurrentDeliveryOrder();
            if (!order.Items.Any())
            {
                return Json(new { success = false, message = "Your cart is empty" });
            }

            var customerName = HttpContext.Session.GetString("DeliveryCustomerName");
            var customerPhone = HttpContext.Session.GetString("DeliveryCustomerPhone");
            var customerAddress = HttpContext.Session.GetString("DeliveryCustomerAddress");

            var orderId = await CreateDeliveryOrder(order, customerName, customerPhone, customerAddress, notes);

            if (orderId > 0)
            {
                HttpContext.Session.Remove("DeliveryOrder");
                TempData["Success"] = $"Order placed successfully! Your order number is #{orderId}";
                return Json(new { success = true, orderId });
            }

            return Json(new { success = false, message = "Failed to place order. Please try again." });
        }

        public async Task<IActionResult> OrderHistory()
        {
            var customerId = HttpContext.Session.GetInt32("DeliveryCustomerId");
            if (customerId == null)
            {
                return RedirectToAction("Index");
            }

            var customerPhone = HttpContext.Session.GetString("DeliveryCustomerPhone");
            var orders = await GetCustomerOrders(customerPhone);

            return View(orders);
        }

        private async Task<DeliveryCustomer> GetOrCreateCustomer(string phone, string name, string address, string email)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Check if customer exists
            var selectSql = "SELECT * FROM DeliveryCustomers WHERE Phone = @Phone";
            using var selectCommand = new SqlCommand(selectSql, connection);
            selectCommand.Parameters.AddWithValue("@Phone", phone);

            using var reader = await selectCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var customer = new DeliveryCustomer
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Phone = reader.GetString(reader.GetOrdinal("Phone")),
                    Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
                    Address = reader.GetString(reader.GetOrdinal("Address")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                };
                reader.Close();

                // Update address if changed
                var updateSql = "UPDATE DeliveryCustomers SET Name = @Name, Address = @Address, Email = @Email WHERE Id = @Id";
                using var updateCommand = new SqlCommand(updateSql, connection);
                updateCommand.Parameters.AddWithValue("@Id", customer.Id);
                updateCommand.Parameters.AddWithValue("@Name", name);
                updateCommand.Parameters.AddWithValue("@Address", address);
                updateCommand.Parameters.AddWithValue("@Email", string.IsNullOrEmpty(email) ? DBNull.Value : email);
                await updateCommand.ExecuteNonQueryAsync();

                customer.Name = name;
                customer.Address = address;
                customer.Email = email;

                return customer;
            }
            reader.Close();

            // Create new customer
            var insertSql = @"
                INSERT INTO DeliveryCustomers (Name, Phone, Email, Address, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Name, @Phone, @Email, @Address, @CreatedAt)";

            using var insertCommand = new SqlCommand(insertSql, connection);
            insertCommand.Parameters.AddWithValue("@Name", name);
            insertCommand.Parameters.AddWithValue("@Phone", phone);
            insertCommand.Parameters.AddWithValue("@Email", string.IsNullOrEmpty(email) ? DBNull.Value : email);
            insertCommand.Parameters.AddWithValue("@Address", address);
            insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

            var newId = (int)await insertCommand.ExecuteScalarAsync();

            return new DeliveryCustomer
            {
                Id = newId,
                Name = name,
                Phone = phone,
                Email = email,
                Address = address,
                CreatedAt = DateTime.Now
            };
        }

        private async Task<DeliveryCustomer> GetCustomerById(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand("SELECT * FROM DeliveryCustomers WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@Id", id);

            await connection.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new DeliveryCustomer
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Phone = reader.GetString(reader.GetOrdinal("Phone")),
                    Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
                    Address = reader.GetString(reader.GetOrdinal("Address")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                };
            }

            return null;
        }

        private async Task<int> CreateDeliveryOrder(Order order, string customerName, string customerPhone, string customerAddress, string notes)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var orderNumber = $"DEL-{DateTime.Now:yyyyMMddHHmmss}";

            // TODO: Add a "Notes" column to the "Orders" table. The application will function without it, but notes will not be saved.
            // Example SQL script to add the column: ALTER TABLE Orders ADD Notes NVARCHAR(MAX) NULL;
            var orderSql = @"
                INSERT INTO Orders (OrderNumber, Total, Status, OrderType, CustomerName, CustomerPhone, Notes, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@OrderNumber, @Total, 'pending', 'delivery', @CustomerName, @CustomerPhone, @Notes, @CreatedAt)";

            try
            {
                using var orderCommand = new SqlCommand(orderSql, connection);
                orderCommand.Parameters.AddWithValue("@OrderNumber", orderNumber);
                orderCommand.Parameters.AddWithValue("@Total", order.Total);
                orderCommand.Parameters.AddWithValue("@CustomerName", customerName);
                orderCommand.Parameters.AddWithValue("@CustomerPhone", customerPhone);
                orderCommand.Parameters.AddWithValue("@Notes", $"Delivery Address: {customerAddress}" + (string.IsNullOrEmpty(notes) ? "" : $"\n{notes}"));
                orderCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                var orderId = (int)await orderCommand.ExecuteScalarAsync();
                await CreateOrderItems(orderId, order, connection);
                return orderId;
            }
            catch (SqlException ex) when (ex.Message.Contains("Invalid column name 'Notes'"))
            {
                // Fallback for when the 'Notes' column doesn't exist.
                orderSql = @"
                    INSERT INTO Orders (OrderNumber, Total, Status, OrderType, CustomerName, CustomerPhone, CreatedAt)
                    OUTPUT INSERTED.Id
                    VALUES (@OrderNumber, @Total, 'pending', 'delivery', @CustomerName, @CustomerPhone, @CreatedAt)";

                using var orderCommand = new SqlCommand(orderSql, connection);
                orderCommand.Parameters.AddWithValue("@OrderNumber", orderNumber);
                orderCommand.Parameters.AddWithValue("@Total", order.Total);
                orderCommand.Parameters.AddWithValue("@CustomerName", customerName);
                orderCommand.Parameters.AddWithValue("@CustomerPhone", customerPhone);
                orderCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                var orderId = (int)await orderCommand.ExecuteScalarAsync();
                await CreateOrderItems(orderId, order, connection);
                return orderId;
            }
        }

        private async Task CreateOrderItems(int orderId, Order order, SqlConnection connection)
        {
            foreach (var item in order.Items)
            {
                var itemSql = @"
                    INSERT INTO OrderItems (OrderId, MenuItemId, Name, Price, Quantity, Total)
                    VALUES (@OrderId, @MenuItemId, @Name, @Price, @Quantity, @Total)";

                using var itemCommand = new SqlCommand(itemSql, connection);
                itemCommand.Parameters.AddWithValue("@OrderId", orderId);
                itemCommand.Parameters.AddWithValue("@MenuItemId", item.MenuItemId);
                itemCommand.Parameters.AddWithValue("@Name", item.Name);
                itemCommand.Parameters.AddWithValue("@Price", item.Price);
                itemCommand.Parameters.AddWithValue("@Quantity", item.Quantity);
                itemCommand.Parameters.AddWithValue("@Total", item.Total);

                await itemCommand.ExecuteNonQueryAsync();
            }
        }

        private async Task<List<Order>> GetCustomerOrders(string phone)
        {
            var orders = new List<Order>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Orders WHERE CustomerPhone = @Phone AND OrderType = 'delivery' ORDER BY CreatedAt DESC";
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Phone", phone);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                orders.Add(new Order
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    OrderNumber = reader.GetString(reader.GetOrdinal("OrderNumber")),
                    Total = reader.GetDecimal(reader.GetOrdinal("Total")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                });
            }

            return orders;
        }

        private async Task<List<MenuItem>> GetMenuItems()
        {
            var menuItems = new List<MenuItem>();

            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand("SELECT * FROM MenuItems WHERE IsAvailable = 1 ORDER BY Category, Name", connection);

            await connection.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                menuItems.Add(new MenuItem
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Price = reader.GetDecimal(reader.GetOrdinal("Price")),
                    Category = reader.GetString(reader.GetOrdinal("Category")),
                    Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                    IsAvailable = reader.GetBoolean(reader.GetOrdinal("IsAvailable"))
                });
            }

            return menuItems;
        }

        private async Task<MenuItem> GetMenuItemById(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand("SELECT * FROM MenuItems WHERE Id = @Id AND IsAvailable = 1", connection);
            command.Parameters.AddWithValue("@Id", id);

            await connection.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new MenuItem
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Price = reader.GetDecimal(reader.GetOrdinal("Price")),
                    Category = reader.GetString(reader.GetOrdinal("Category")),
                    Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description"))
                };
            }

            return null;
        }

        private Order GetCurrentDeliveryOrder()
        {
            var orderJson = HttpContext.Session.GetString("DeliveryOrder");
            if (!string.IsNullOrEmpty(orderJson))
            {
                return JsonSerializer.Deserialize<Order>(orderJson);
            }
            return new Order { Items = new List<OrderItem>() };
        }

        private void SaveCurrentDeliveryOrder(Order order)
        {
            var orderJson = JsonSerializer.Serialize(order);
            HttpContext.Session.SetString("DeliveryOrder", orderJson);
        }
    }
}