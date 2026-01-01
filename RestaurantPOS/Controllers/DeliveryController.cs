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
            _connectionString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        private Order GetCurrentDeliveryOrder()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            var orderJson = session?.GetString("DeliveryOrder");
            if (!string.IsNullOrEmpty(orderJson))
            {
                try
                {
                    return JsonSerializer.Deserialize<Order>(orderJson) ?? new Order { Items = new List<OrderItem>() };
                }
                catch { /* Fallback to new order */ }
            }
            return new Order { Items = new List<OrderItem>() };
        }

        private void SaveCurrentDeliveryOrder(Order order)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            session?.SetString("DeliveryOrder", JsonSerializer.Serialize(order));
        }

        private void ClearCurrentDeliveryOrder()
        {
            _httpContextAccessor.HttpContext?.Session.Remove("DeliveryOrder");
        }

        private async Task<User?> GetUserById(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            var command = new SqlCommand("SELECT * FROM Users WHERE Id = @Id AND Role = 'User'", connection);
            command.Parameters.AddWithValue("@Id", userId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Username = reader.GetString(reader.GetOrdinal("Username")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Role = reader.GetString(reader.GetOrdinal("Role"))
                };
            }
            return null;
        }

        [HttpGet]
        public IActionResult Login()
        {
            // If user is already logged in, redirect to the main delivery page
            if (_httpContextAccessor.HttpContext?.Session.GetInt32("CustomerId").HasValue ?? false)
            {
                return RedirectToAction("Index");
            }
            return View(new DeliveryViewModel());
        }

        public async Task<IActionResult> Index(int? id)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            var userId = session?.GetInt32("CustomerId");

            if (!userId.HasValue)
            {
                return RedirectToAction("Login");
            }

            var user = await GetUserById(userId.Value);
            if (user == null)
            {
                session.Remove("CustomerId");
                TempData["Error"] = "Your session has expired. Please log in again.";
                return RedirectToAction("Login");
            }

            var menuItems = await GetMenuItems();
            var currentOrder = GetCurrentDeliveryOrder(); 

            var viewModel = new DeliveryViewModel
            {
                IsLoggedIn = true,
                Customer = new DeliveryCustomer { Name = user.Name },
                MenuItems = menuItems,
                CurrentOrder = currentOrder,
                RestaurantName = _configuration["RestaurantName"] ?? "The Golden Spoon"
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string username, string password, string name, string phone)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone))
            {
                TempData["Error"] = "All fields are required for registration.";
                return RedirectToAction("Login");
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var checkUserCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Username = @Username OR Phone = @Phone", connection);
            checkUserCmd.Parameters.AddWithValue("@Username", username);
            checkUserCmd.Parameters.AddWithValue("@Phone", phone);
            if ((int)await checkUserCmd.ExecuteScalarAsync() > 0)
            {
                TempData["Error"] = "Username or phone number already exists. Please choose another or log in.";
                return RedirectToAction("Login");
            }

            var insertUserSql = @"
                INSERT INTO Users (Username, Password, Name, Phone, Role, IsActive, CreatedAt)
                VALUES (@Username, @Password, @Name, @Phone, 'User', 1, @CreatedAt);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            using var command = new SqlCommand(insertUserSql, connection);
            command.Parameters.AddWithValue("@Username", username);
            command.Parameters.AddWithValue("@Password", password); // Note: Passwords should be hashed in a real application
            command.Parameters.AddWithValue("@Name", name);
            command.Parameters.AddWithValue("@Phone", phone);
            command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

            var newUserId = (int)await command.ExecuteScalarAsync();
            _httpContextAccessor.HttpContext.Session.SetInt32("CustomerId", newUserId);

            TempData["Success"] = "Registration successful! You are now logged in.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string loginIdentifier, string password)
        {
            if (string.IsNullOrWhiteSpace(loginIdentifier) || string.IsNullOrWhiteSpace(password))
            {
                TempData["Error"] = "Username/phone and password are required.";
                return RedirectToAction("Login");
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            var command = new SqlCommand("SELECT Id, Name FROM Users WHERE (Username = @LoginIdentifier OR Phone = @LoginIdentifier) AND Password = @Password AND Role = 'User' AND IsActive = 1", connection);
            command.Parameters.AddWithValue("@LoginIdentifier", loginIdentifier);
            command.Parameters.AddWithValue("@Password", password);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var userId = reader.GetInt32(reader.GetOrdinal("Id"));
                _httpContextAccessor.HttpContext.Session.SetInt32("CustomerId", userId);
                TempData["Success"] = "Login successful!";
                return RedirectToAction("Index");
            }

            TempData["Error"] = "Invalid credentials.";
            return RedirectToAction("Login");
        }

        [HttpPost]
        public IActionResult Logout()
        {
            _httpContextAccessor.HttpContext?.Session.Remove("CustomerId");
            ClearCurrentDeliveryOrder();
            TempData["Success"] = "You have been logged out.";
            return RedirectToAction("Index");
        }

        private async Task<List<MenuItem>> GetMenuItems()
        {
            var menuItems = new List<MenuItem>();
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            var command = new SqlCommand("SELECT * FROM MenuItems WHERE IsAvailable = 1 ORDER BY Category, Name", connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                menuItems.Add(new MenuItem
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Price = reader.GetDecimal(reader.GetOrdinal("Price")),
                    Category = reader.GetString(reader.GetOrdinal("Category")),
                    Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description"))
                });
            }
            return menuItems;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(int itemId, int quantity = 1)
        {
            var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("CustomerId");
            if (!userId.HasValue)
            {
                return Json(new { success = false, message = "You must be logged in to add items." });
            }

            var menuItem = await GetMenuItemById(itemId);
            if (menuItem == null)
            {
                return Json(new { success = false, message = "Item not found." });
            }

            var order = GetCurrentDeliveryOrder();
            var existingItem = order.Items.FirstOrDefault(i => i.MenuItemId == itemId);

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
                existingItem.CalculateAndSetTotal();
            }
            else
            {
                var newItem = new OrderItem { MenuItemId = itemId, Name = menuItem.Name, Price = menuItem.Price, Quantity = quantity };
                newItem.CalculateAndSetTotal();
                order.Items.Add(newItem);
            }

            order.CalculateAndSetTotal();
            SaveCurrentDeliveryOrder(order);

            return Json(new
            {
                success = true,
                message = $"{menuItem.Name} added to your order.",
                itemCount = order.Items.Sum(i => i.Quantity),
                total = order.Total
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFromCart(int menuItemId)
        {
            var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("CustomerId");
            if (!userId.HasValue) return Json(new { success = false, message = "Not logged in." });

            var order = GetCurrentDeliveryOrder();
            order.Items.RemoveAll(i => i.MenuItemId == menuItemId);
            order.CalculateAndSetTotal();
            SaveCurrentDeliveryOrder(order);

            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(string notes, string deliveryPhone, string deliveryAddress)
        {
            var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("CustomerId");
            if (!userId.HasValue)
            {
                return Json(new { success = false, message = "You must be logged in to place an order." });
            }

            var order = GetCurrentDeliveryOrder();
            if (!order.Items.Any())
            {
                return Json(new { success = false, message = "Your cart is empty." });
            }

            if (string.IsNullOrWhiteSpace(deliveryPhone) || string.IsNullOrWhiteSpace(deliveryAddress))
            {
                return Json(new { success = false, message = "Phone number and address are required for delivery." });
            }

            var user = await GetUserById(userId.Value);
            if (user == null)
            {
                return Json(new { success = false, message = "Customer not found." });
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var orderNumber = $"DEL-{DateTime.Now:yyyyMMddHHmmss}";
            var insertOrderSql = @"
                INSERT INTO Orders (OrderNumber, OrderType, CustomerName, CustomerPhone, CustomerAddress, Total, Status, CreatedAt, Notes, UserId)
                OUTPUT INSERTED.Id
                VALUES (@OrderNumber, 'delivery', @CustomerName, @CustomerPhone, @CustomerAddress, @Total, 'pending', @CreatedAt, @Notes, @UserId);";

            using var command = new SqlCommand(insertOrderSql, connection);
            command.Parameters.AddWithValue("@OrderNumber", orderNumber);
            command.Parameters.AddWithValue("@CustomerName", user.Name);
            command.Parameters.AddWithValue("@CustomerPhone", deliveryPhone);
            command.Parameters.AddWithValue("@CustomerAddress", deliveryAddress);
            command.Parameters.AddWithValue("@UserId", user.Id);
            command.Parameters.AddWithValue("@Total", order.Total);
            command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
            command.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes);

            var orderId = (int)await command.ExecuteScalarAsync();

            foreach (var item in order.Items)
            {
                var insertItemSql = "INSERT INTO OrderItems (OrderId, MenuItemId, Name, Price, Quantity, Total) VALUES (@OrderId, @MenuItemId, @Name, @Price, @Quantity, @Total)";
                using var itemCommand = new SqlCommand(insertItemSql, connection);
                itemCommand.Parameters.AddWithValue("@OrderId", orderId);
                itemCommand.Parameters.AddWithValue("@MenuItemId", item.MenuItemId);
                itemCommand.Parameters.AddWithValue("@Name", item.Name);
                itemCommand.Parameters.AddWithValue("@Price", item.Price);
                itemCommand.Parameters.AddWithValue("@Quantity", item.Quantity);
                itemCommand.Parameters.AddWithValue("@Total", item.Total);
                await itemCommand.ExecuteNonQueryAsync();
            }

            ClearCurrentDeliveryOrder();
            TempData["Success"] = $"Your order #{orderNumber} has been placed successfully!";
            return Json(new { success = true, redirectUrl = Url.Action("Index") });
        }

        private async Task<MenuItem?> GetMenuItemById(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            var command = new SqlCommand("SELECT * FROM MenuItems WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@Id", id);
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

        [HttpGet]
        public IActionResult GetCartPartial()
        {
            var order = GetCurrentDeliveryOrder();
            return PartialView("_CartPartial", order);
        }

        [HttpGet]
        public IActionResult GetCartTotals()
        {
            var order = GetCurrentDeliveryOrder();
            return Json(new
            {
                success = true,
                subtotal = order.Total,
                itemCount = order.Items.Sum(i => i.Quantity)
            });
        }

        [HttpGet]
        public async Task<IActionResult> OrderHistory()
        {
            var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("CustomerId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login");
            }

            var user = await GetUserById(userId.Value);
            if (user == null)
            {
                return RedirectToAction("Login");
            }

            var orders = await GetOrdersByUserId(userId.Value);

            var viewModel = new OrderHistoryViewModel
            {
                CustomerName = user.Name,
                Orders = orders
            };

            return View(viewModel);
        }

        private async Task<List<Order>> GetOrdersByUserId(int userId)
        {
            var orders = new List<Order>();
            var orderItemsMap = new Dictionary<int, List<OrderItem>>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var ordersSql = "SELECT * FROM Orders WHERE UserId = @UserId ORDER BY CreatedAt DESC";
            using var ordersCommand = new SqlCommand(ordersSql, connection);
            ordersCommand.Parameters.AddWithValue("@UserId", userId);

            using (var reader = await ordersCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var order = new Order
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        OrderNumber = reader.GetString(reader.GetOrdinal("OrderNumber")),
                        Total = reader.GetDecimal(reader.GetOrdinal("Total")),
                        Status = reader.GetString(reader.GetOrdinal("Status")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        Items = new List<OrderItem>()
                    };
                    orders.Add(order);
                    orderItemsMap[order.Id] = order.Items;
                }
            }

            if (orders.Any())
            {
                var orderIds = orders.Select(o => o.Id).ToList();
                var itemsSql = $"SELECT * FROM OrderItems WHERE OrderId IN ({string.Join(",", orderIds)})";
                using var itemsCommand = new SqlCommand(itemsSql, connection);

                using (var reader = await itemsCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var orderId = reader.GetInt32(reader.GetOrdinal("OrderId"));
                        if (orderItemsMap.TryGetValue(orderId, out var items))
                        {
                            items.Add(new OrderItem
                            {
                                Name = reader.GetString(reader.GetOrdinal("Name")),
                                Quantity = reader.GetInt32(reader.GetOrdinal("Quantity")),
                                Price = reader.GetDecimal(reader.GetOrdinal("Price"))
                            });
                        }
                    }
                }
            }

            return orders;
        }
    }
}