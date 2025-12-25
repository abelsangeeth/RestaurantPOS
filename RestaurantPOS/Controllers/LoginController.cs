using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantPOS.Models;
using System.Data;
using System.Text.Json;

namespace RestaurantPOS.Controllers
{
    public class LoginController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _connectionString;

        public LoginController(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        public IActionResult Login()
        {
            // Check if user has "Remember Me" cookie
            var rememberedUsername = Request.Cookies["RememberUsername"];

            var viewModel = new POSViewModel();

            if (!string.IsNullOrEmpty(rememberedUsername))
            {
                ViewBag.RememberedUsername = rememberedUsername;
                ViewBag.RememberMe = true;
            }

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe = false)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                TempData["Error"] = "Please enter both username and password";
                return View(new POSViewModel());
            }

            var user = await GetUserByCredentials(username, password);
            if (user != null)
            {
                _httpContextAccessor.HttpContext.Session.SetInt32("UserId", user.Id);

                // Handle Remember Me
                if (rememberMe)
                {
                    var cookieOptions = new CookieOptions
                    {
                        Expires = DateTime.Now.AddDays(30),
                        HttpOnly = true,
                        Secure = false, // Set to true in production with HTTPS
                        SameSite = SameSiteMode.Lax
                    };

                    Response.Cookies.Append("RememberUsername", username, cookieOptions);
                    Response.Cookies.Append("RememberToken", Convert.ToBase64String(Guid.NewGuid().ToByteArray()), cookieOptions);
                }
                else
                {
                    Response.Cookies.Delete("RememberUsername");
                    Response.Cookies.Delete("RememberToken");
                }

                TempData["Success"] = $"Welcome back, {user.Name}!";
                return RedirectToAction("Index");
            }

            TempData["Error"] = "Invalid username or password";
            return View(new POSViewModel());
        }

        [HttpPost]
        public IActionResult Logout()
        {
            _httpContextAccessor.HttpContext.Session.Remove("UserId");
            _httpContextAccessor.HttpContext.Session.Remove("CurrentOrder");
            
            TempData["Success"] = "Logged out successfully";
            return RedirectToAction("Index");
        }

        private async Task<User> GetUserByCredentials(string username, string password)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand("SELECT * FROM Users WHERE Username = @Username AND Password = @Password AND IsActive = 1", connection);
            command.Parameters.AddWithValue("@Username", username);
            command.Parameters.AddWithValue("@Password", password);

            await connection.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt32("Id"),
                    Username = reader.GetString("Username"),
                    Password = reader.GetString("Password"),
                    Name = reader.GetString("Name"),
                    Role = reader.GetString("Role"),
                    CreatedAt = reader.GetDateTime("CreatedAt"),
                    IsActive = reader.GetBoolean("IsActive")
                };
            }

            return null;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _httpContextAccessor.HttpContext.Session.GetInt32("UserId");
            
            if (userId == null)
            {
                // User not logged in - show login form
                // Check for remembered username
                var rememberedUsername = Request.Cookies["RememberUsername"];
                if (!string.IsNullOrEmpty(rememberedUsername))
                {
                    ViewBag.RememberedUsername = rememberedUsername;
                    ViewBag.RememberMe = true;
                }
                
                return View(new POSViewModel());
            }

            var user = await GetUserById(userId.Value);
            if (user == null)
            {
                // Check for remembered username
                var rememberedUsername = Request.Cookies["RememberUsername"];
                if (!string.IsNullOrEmpty(rememberedUsername))
                {
                    ViewBag.RememberedUsername = rememberedUsername;
                    ViewBag.RememberMe = true;
                }
                
                return View(new POSViewModel());
            }

            var viewModel = await CreatePOSViewModel(user);
            return View(viewModel);
        }

        private async Task<User> GetUserById(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand("SELECT * FROM Users WHERE Id = @Id AND IsActive = 1", connection);
            command.Parameters.AddWithValue("@Id", id);

            await connection.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt32("Id"),
                    Username = reader.GetString("Username"),
                    Password = reader.GetString("Password"),
                    Name = reader.GetString("Name"),
                    Role = reader.GetString("Role"),
                    CreatedAt = reader.GetDateTime("CreatedAt"),
                    IsActive = reader.GetBoolean("IsActive")
                };
            }

            return null;
        }

        private async Task<POSViewModel> CreatePOSViewModel(User user)
        {
            var today = DateTime.Today;

            var menuItems = await GetMenuItems();
            var tables = await GetTables();
            var orders = await GetOrders();

            var todayOrders = orders.Where(o => o.CreatedAt.Date == today).ToList();

            var viewModel = new POSViewModel
            {
                CurrentUser = user,
                MenuItems = menuItems,
                Tables = tables,
                Orders = orders,
                CurrentOrder = GetCurrentOrder(),
                TodaySales = todayOrders.Where(o => o.Status == "completed").Sum(o => o.Total),
                ActiveOrdersCount = orders.Count(o => o.Status == "pending" || o.Status == "preparing" || o.Status == "ready"),
                OccupiedTablesCount = tables.Count(t => t.Status == "occupied"),
                AvailableTablesCount = tables.Count(t => t.Status == "available"),
                PendingOrdersCount = orders.Count(o => o.Status == "pending"),
                PreparingOrdersCount = orders.Count(o => o.Status == "preparing"),
                ReadyOrdersCount = orders.Count(o => o.Status == "ready"),
                CompletedOrdersCount = orders.Count(o => o.Status == "completed")
            };

            return viewModel;
        }

        private async Task<List<Order>> GetOrders()
        {
            var orders = new List<Order>();
            var orderItems = new Dictionary<int, List<OrderItem>>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var ordersSql = @"
                SELECT o.*, t.Id as TableId, t.Status as TableStatus, t.Capacity, t.Location
                FROM Orders o 
                LEFT JOIN Tables t ON o.TableId = t.Id 
                ORDER BY o.CreatedAt DESC";

            using var ordersCommand = new SqlCommand(ordersSql, connection);
            using var ordersReader = await ordersCommand.ExecuteReaderAsync();

            while (await ordersReader.ReadAsync())
            {
                var order = new Order
                {
                    Id = ordersReader.GetInt32("Id"),
                    OrderNumber = ordersReader.GetString("OrderNumber"),
                    Total = ordersReader.GetDecimal("Total"),
                    Status = ordersReader.GetString("Status"),
                    OrderType = ordersReader.GetString("OrderType"),
                    CustomerName = ordersReader.GetString("CustomerName"),
                    CustomerPhone = ordersReader.IsDBNull("CustomerPhone") ? null : ordersReader.GetString("CustomerPhone"),
                    TableId = ordersReader.IsDBNull("TableId") ? null : ordersReader.GetInt32("TableId"),
                    PaymentMethod = ordersReader.IsDBNull("PaymentMethod") ? null : ordersReader.GetString("PaymentMethod"),
                    CreatedAt = ordersReader.GetDateTime("CreatedAt"),
                    UpdatedAt = ordersReader.IsDBNull("UpdatedAt") ? null : ordersReader.GetDateTime("UpdatedAt"),
                    CompletedAt = ordersReader.IsDBNull("CompletedAt") ? null : ordersReader.GetDateTime("CompletedAt")
                };

                if (!ordersReader.IsDBNull("TableId"))
                {
                    order.Table = new Table
                    {
                        Id = ordersReader.GetInt32("TableId"),
                        Status = ordersReader.GetString("TableStatus"),
                        Capacity = ordersReader.IsDBNull("Capacity") ? null : ordersReader.GetInt32("Capacity"),
                        Location = ordersReader.IsDBNull("Location") ? null : ordersReader.GetString("Location")
                    };
                }

                orders.Add(order);
                orderItems[order.Id] = new List<OrderItem>();
            }
            ordersReader.Close();

            var itemsSql = "SELECT * FROM OrderItems WHERE OrderId IN (SELECT Id FROM Orders)";
            using var itemsCommand = new SqlCommand(itemsSql, connection);
            using var itemsReader = await itemsCommand.ExecuteReaderAsync();

            while (await itemsReader.ReadAsync())
            {
                var orderItem = new OrderItem
                {
                    Id = itemsReader.GetInt32("Id"),
                    OrderId = itemsReader.GetInt32("OrderId"),
                    MenuItemId = itemsReader.GetInt32("MenuItemId"),
                    Name = itemsReader.GetString("Name"),
                    Price = itemsReader.GetDecimal("Price"),
                    Quantity = itemsReader.GetInt32("Quantity"),
                    Total = itemsReader.GetDecimal("Total")
                };

                if (orderItems.ContainsKey(orderItem.OrderId))
                {
                    orderItems[orderItem.OrderId].Add(orderItem);
                }
            }

            foreach (var order in orders)
            {
                if (orderItems.ContainsKey(order.Id))
                {
                    order.Items = orderItems[order.Id];
                }
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
                    Id = reader.GetInt32("Id"),
                    Name = reader.GetString("Name"),
                    Price = reader.GetDecimal("Price"),
                    Category = reader.GetString("Category"),
                    Description = reader.IsDBNull("Description") ? null : reader.GetString("Description"),
                    IsAvailable = reader.GetBoolean("IsAvailable"),
                    CreatedAt = reader.GetDateTime("CreatedAt"),
                    UpdatedAt = reader.IsDBNull("UpdatedAt") ? null : reader.GetDateTime("UpdatedAt")
                });
            }

            return menuItems;
        }

        private Order GetCurrentOrder()
        {
            var session = _httpContextAccessor.HttpContext.Session;
            var orderJson = session.GetString("CurrentOrder");

            if (!string.IsNullOrEmpty(orderJson))
            {
                return JsonSerializer.Deserialize<Order>(orderJson);
            }

            return new Order { Items = new List<OrderItem>() };
        }

        private async Task<List<Table>> GetTables()
        {
            var tables = new List<Table>();

            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT t.*, o.Id as OrderId, o.OrderNumber, o.Total as OrderTotal, o.Status as OrderStatus
                FROM Tables t 
                LEFT JOIN Orders o ON t.CurrentOrderId = o.Id 
                ORDER BY t.Id";

            using var command = new SqlCommand(sql, connection);

            await connection.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var table = new Table
                {
                    Id = reader.GetInt32("Id"),
                    Status = reader.GetString("Status"),
                    Capacity = reader.IsDBNull("Capacity") ? null : reader.GetInt32("Capacity"),
                    Location = reader.IsDBNull("Location") ? null : reader.GetString("Location"),
                    Type = reader.IsDBNull("Type") ? null : reader.GetString("Type"),
                    Notes = reader.IsDBNull("Notes") ? null : reader.GetString("Notes"),
                    CurrentOrderId = reader.IsDBNull("CurrentOrderId") ? null : reader.GetInt32("CurrentOrderId"),
                    CreatedAt = reader.GetDateTime("CreatedAt"),
                    UpdatedAt = reader.IsDBNull("UpdatedAt") ? null : reader.GetDateTime("UpdatedAt")
                };

                if (!reader.IsDBNull("OrderId"))
                {
                    table.CurrentOrder = new Order
                    {
                        Id = reader.GetInt32("OrderId"),
                        OrderNumber = reader.GetString("OrderNumber"),
                        Total = reader.GetDecimal("OrderTotal"),
                        Status = reader.GetString("OrderStatus")
                    };
                }

                tables.Add(table);
            }

            return tables;
        }
    }
}
