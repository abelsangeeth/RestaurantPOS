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

        public IActionResult Staff()
        {
            // If user is already logged in, redirect to dashboard
            if (_httpContextAccessor.HttpContext.Session.GetInt32("UserId") != null)
            {
                return RedirectToAction("Index");
            }

            var rememberedUsername = Request.Cookies["RememberUsername"];
            if (!string.IsNullOrEmpty(rememberedUsername))
            {
                ViewBag.RememberedUsername = rememberedUsername;
                ViewBag.RememberMe = true;
            }

            return View(new POSViewModel());
        }

        [HttpGet]
        public IActionResult Login()
        {
            // This action now simply shows the staff login page.
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe = false)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                TempData["Error"] = "Please enter both username and password";
                return RedirectToAction("Staff");
            }

            var user = await GetUserByCredentials(username, password);
            if (user != null)
            {
                // Role check to prevent role confusion
                if (user.Role != "Manager" && user.Role != "Staff")
                {
                    TempData["Error"] = "You do not have permission to access this page.";
                    TempData["RememberedUsername"] = username;
                    return RedirectToAction("Staff");
                }

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
                }
                else
                {
                    Response.Cookies.Delete("RememberUsername");
                }

                TempData["Success"] = $"Welcome back, {user.Name}!";
                return RedirectToAction("Index");
            }

            TempData["Error"] = "Invalid username or password";
            // Remember username if login fails but remember me was not checked
            TempData["RememberedUsername"] = username;
            return RedirectToAction("Staff");
        }

        [HttpPost]
        public IActionResult Logout()
        {
            _httpContextAccessor.HttpContext.Session.Remove("UserId");
            _httpContextAccessor.HttpContext.Session.Remove("CurrentOrder");
            Response.Cookies.Delete("RememberUsername");

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
                // User not logged in - show the main chooser/landing page
                return View("Chooser", new POSViewModel());
            }

            var user = await GetUserById(userId.Value);
            if (user == null)
            {
                // User not found, clear session and show chooser
                _httpContextAccessor.HttpContext.Session.Remove("UserId");
                return View("Chooser", new POSViewModel());
            }

            // User is logged in, show the main POS dashboard
            var viewModel = await CreatePOSViewModel(user);
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> AddTable()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                // Get the last table ID to determine the new table name
                var lastIdCommand = new SqlCommand("SELECT ISNULL(MAX(Id), 0) FROM Tables", connection);
                var lastId = (int)await lastIdCommand.ExecuteScalarAsync();
                var newTableName = "T" + (lastId + 1);

                var insertCommand = new SqlCommand("INSERT INTO Tables (TableName, Status, Capacity, Location,CreatedAt) VALUES (@TableName, 'Available', 4, 'Main Dining', GETDATE())", connection);
                insertCommand.Parameters.AddWithValue("@TableName", newTableName);
                await insertCommand.ExecuteNonQueryAsync();
            }
            return RedirectToAction("Index");
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
                RecentOrders = orders.Take(2).ToList(),
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
                    TableName = reader.IsDBNull("TableName") ? "T" + reader.GetInt32("Id") : reader.GetString("TableName"),
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

        // ==================== STAFF MANAGEMENT METHODS ====================

        [HttpGet]
        public async Task<IActionResult> GetAllStaff()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var staffList = new List<object>();
                var sql = @"
                    SELECT Id, Username, Name, Role, Phone, CreatedAt, IsActive 
                    FROM Users 
                    WHERE Role IN ('Manager', 'Staff')
                    ORDER BY Role, Name";

                using (var cmd = new SqlCommand(sql, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        staffList.Add(new
                        {
                            id = reader.GetInt32("Id"),
                            username = reader.GetString("Username"),
                            name = reader.GetString("Name"),
                            role = reader.GetString("Role"),
                            phone = reader.IsDBNull("Phone") ? "" : reader.GetString("Phone"),
                            createdAt = reader.GetDateTime("CreatedAt").ToString("yyyy-MM-dd"),
                            isActive = reader.GetBoolean("IsActive")
                        });
                    }
                }

                return Json(new { success = true, staff = staffList });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading staff: {ex.Message}" });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AddStaff([FromBody] AddStaffRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password) ||
                    string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Role))
                {
                    return Json(new { success = false, message = "All required fields must be filled" });
                }

                // Check if username already exists
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var checkSql = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                using (var checkCmd = new SqlCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@Username", request.Username);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists > 0)
                    {
                        return Json(new { success = false, message = "Username already exists" });
                    }
                }

                // Insert new staff
                var insertSql = @"
                    INSERT INTO Users (Username, Password, Name, Role, Phone, IsActive, CreatedAt)
                    VALUES (@Username, @Password, @Name, @Role, @Phone, @IsActive, @CreatedAt);
                    SELECT CAST(SCOPE_IDENTITY() as int)";

                using var insertCmd = new SqlCommand(insertSql, connection);
                insertCmd.Parameters.AddWithValue("@Username", request.Username);
                insertCmd.Parameters.AddWithValue("@Password", request.Password);
                insertCmd.Parameters.AddWithValue("@Name", request.Name);
                insertCmd.Parameters.AddWithValue("@Role", request.Role);
                insertCmd.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(request.Phone) ? (object)DBNull.Value : request.Phone);
                insertCmd.Parameters.AddWithValue("@IsActive", true);
                insertCmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                var newId = await insertCmd.ExecuteScalarAsync();

                return Json(new
                {
                    success = true,
                    message = $"{request.Role} added successfully",
                    staffId = newId
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding staff: {ex.Message}" });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> UpdateStaff([FromBody] UpdateStaffRequest request)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var updateSql = @"
                    UPDATE Users 
                    SET Name = @Name,
                        Role = @Role,
                        Phone = @Phone,
                        IsActive = @IsActive,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var cmd = new SqlCommand(updateSql, connection);
                cmd.Parameters.AddWithValue("@Id", request.Id);
                cmd.Parameters.AddWithValue("@Name", request.Name);
                cmd.Parameters.AddWithValue("@Role", request.Role);
                cmd.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(request.Phone) ? (object)DBNull.Value : request.Phone);
                cmd.Parameters.AddWithValue("@IsActive", request.IsActive);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Json(new { success = true, message = "Staff updated successfully" });
                }
                else
                {
                    return Json(new { success = false, message = "Staff not found or cannot be updated" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating staff: {ex.Message}" });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteStaff([FromBody] DeleteStaffRequest request)
        {
            try
            {
                // Prevent deleting self
                var currentUserId = _httpContextAccessor.HttpContext.Session.GetInt32("UserId");
                if (currentUserId.HasValue && currentUserId.Value == request.Id)
                {
                    return Json(new { success = false, message = "Cannot delete your own account" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Instead of deleting, deactivate the account
                var deactivateSql = "UPDATE Users SET IsActive = 0 WHERE Id = @Id";
                using var deactivateCmd = new SqlCommand(deactivateSql, connection);
                deactivateCmd.Parameters.AddWithValue("@Id", request.Id);

                var rowsAffected = await deactivateCmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Json(new { success = true, message = "Staff deactivated successfully" });
                }
                else
                {
                    return Json(new { success = false, message = "Staff not found" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error deleting staff: {ex.Message}" });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ResetStaffPassword([FromBody] ResetPasswordRequest request)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var updateSql = "UPDATE Users SET Password = @Password, UpdatedAt = @UpdatedAt WHERE Id = @Id";
                using var cmd = new SqlCommand(updateSql, connection);
                cmd.Parameters.AddWithValue("@Id", request.Id);
                cmd.Parameters.AddWithValue("@Password", request.NewPassword);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Json(new { success = true, message = "Password reset successfully" });
                }
                else
                {
                    return Json(new { success = false, message = "Staff not found" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error resetting password: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStaffStats()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var stats = new
                {
                    totalStaff = 0,
                    managers = 0,
                    staffMembers = 0,
                    active = 0
                };

                var sql = @"
                    SELECT 
                        COUNT(*) as Total,
                        SUM(CASE WHEN Role = 'Manager' THEN 1 ELSE 0 END) as Managers,
                        SUM(CASE WHEN Role = 'Staff' THEN 1 ELSE 0 END) as StaffMembers,
                        SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) as Active
                    FROM Users 
                    WHERE Role IN ('Manager', 'Staff')";

                using var cmd = new SqlCommand(sql, connection);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    stats = new
                    {
                        totalStaff = reader.GetInt32("Total"),
                        managers = reader.GetInt32("Managers"),
                        staffMembers = reader.GetInt32("StaffMembers"),
                        active = reader.GetInt32("Active")
                    };
                }

                return Json(new { success = true, stats = stats });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading staff stats: {ex.Message}" });
            }
        }

        // Request models
        public class AddStaffRequest
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string Name { get; set; } = "";
            public string Role { get; set; } = "";
            public string? Phone { get; set; }
        }

        public class UpdateStaffRequest
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Role { get; set; } = "";
            public string? Phone { get; set; }
            public bool IsActive { get; set; }
        }

        public class DeleteStaffRequest
        {
            public int Id { get; set; }
        }

        public class ResetPasswordRequest
        {
            public int Id { get; set; }
            public string NewPassword { get; set; } = "";
        }
    }
}