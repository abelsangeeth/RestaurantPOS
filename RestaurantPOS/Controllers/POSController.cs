using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantPOS.Models;
using System.Data;
using System.Text.Json;
using System.Text;

namespace RestaurantPOS.Controllers
{
    public class POSController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _connectionString;

        public POSController(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _connectionString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // Helper method to get current order from session
        private Order GetCurrentOrder()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null)
            {
                return new Order { Items = new List<OrderItem>() };
            }

            var orderJson = session.GetString("CurrentOrder");

            if (!string.IsNullOrEmpty(orderJson))
            {
                try
                {
                    var order = JsonSerializer.Deserialize<Order>(orderJson);
                    return order ?? new Order { Items = new List<OrderItem>() };
                }
                catch
                {
                    return new Order { Items = new List<OrderItem>() };
                }
            }

            return new Order { Items = new List<OrderItem>() };
        }

        // Helper method to save current order to session
        private void SaveCurrentOrder(Order order)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null)
            {
                return;
            }

            var orderJson = JsonSerializer.Serialize(order);
            session.SetString("CurrentOrder", orderJson);
        }

        // Clear current order from session
        private void ClearCurrentOrder()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            session?.Remove("CurrentOrder");
        }

        // Generate unique order number
        private string GenerateOrderNumber()
        {
            return $"ORD-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        // Helper method to get user by ID
        private async Task<User?> GetUserById(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Id, Username, Name, Role, IsActive 
                FROM Users 
                WHERE Id = @UserId";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt32("Id"),
                    Username = reader.GetString("Username"),
                    Name = reader.GetString("Name"),
                    Role = reader.GetString("Role"),
                    IsActive = reader.GetBoolean("IsActive")
                };
            }

            return null;
        }

        // Helper method to get menu item by ID
        private async Task<MenuItem?> GetMenuItemById(int itemId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Id, Name, Price, Category, Description, IsAvailable 
                FROM MenuItems 
                WHERE Id = @ItemId AND IsAvailable = 1";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@ItemId", itemId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new MenuItem
                {
                    Id = reader.GetInt32("Id"),
                    Name = reader.GetString("Name"),
                    Price = reader.GetDecimal("Price"),
                    Category = reader.GetString("Category"),
                    Description = reader.IsDBNull("Description") ? "" : reader.GetString("Description"),
                    IsAvailable = reader.GetBoolean("IsAvailable")
                };
            }

            return null;
        }

        // Helper method to create POS ViewModel
        private async Task<POSViewModel> CreatePOSViewModel(User user)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var viewModel = new POSViewModel
            {
                CurrentUser = user,
                RestaurantName = "Restaurant POS",
                ContactInfo = "123-456-7890 | 123 Main St",
                CurrentOrder = GetCurrentOrder()
            };

            // Get all menu items
            var menuItemsQuery = "SELECT Id, Name, Price, Category, Description, IsAvailable FROM MenuItems WHERE IsAvailable = 1 ORDER BY Category, Name";
            using (var command = new SqlCommand(menuItemsQuery, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    viewModel.MenuItems.Add(new MenuItem
                    {
                        Id = reader.GetInt32("Id"),
                        Name = reader.GetString("Name"),
                        Price = reader.GetDecimal("Price"),
                        Category = reader.GetString("Category"),
                        Description = reader.IsDBNull("Description") ? "" : reader.GetString("Description"),
                        IsAvailable = reader.GetBoolean("IsAvailable")
                    });
                }
            }

            // Get all tables
            var tablesQuery = @"
                SELECT t.Id, t.Capacity, t.Location, t.Type, t.Status, t.Notes,
                       o.Id as OrderId, o.OrderNumber, o.Total, o.Status as OrderStatus
                FROM Tables t
                LEFT JOIN Orders o ON t.CurrentOrderId = o.Id
                ORDER BY t.Id";

            using (var command = new SqlCommand(tablesQuery, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var table = new Table
                    {
                        Id = reader.GetInt32("Id"),
                        Capacity = reader.GetInt32("Capacity"),
                        Location = reader.IsDBNull("Location") ? "" : reader.GetString("Location"),
                        Type = reader.IsDBNull("Type") ? "" : reader.GetString("Type"),
                        Status = reader.GetString("Status"),
                        Notes = reader.IsDBNull("Notes") ? "" : reader.GetString("Notes")
                    };

                    if (!reader.IsDBNull("OrderId"))
                    {
                        table.CurrentOrder = new Order
                        {
                            Id = reader.GetInt32("OrderId"),
                            OrderNumber = reader.IsDBNull("OrderNumber") ? "" : reader.GetString("OrderNumber"),
                            Total = reader.GetDecimal("Total"),
                            Status = reader.IsDBNull("OrderStatus") ? "" : reader.GetString("OrderStatus")
                        };
                    }

                    viewModel.Tables.Add(table);
                }
            }

            // Get all orders
            var ordersQuery = @"
                SELECT o.Id, o.OrderNumber, o.OrderType, o.CustomerName, o.Total, o.Status, o.CreatedAt
                FROM Orders o
                ORDER BY 
                    CASE 
                        WHEN o.Status = 'pending' THEN 1
                        WHEN o.Status = 'preparing' THEN 2
                        WHEN o.Status = 'ready' THEN 3
                        WHEN o.Status = 'completed' THEN 4
                        WHEN o.Status = 'cancelled' THEN 5
                        ELSE 6
                    END,
                    o.CreatedAt DESC";

            using (var command = new SqlCommand(ordersQuery, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    viewModel.Orders.Add(new Order
                    {
                        Id = reader.GetInt32("Id"),
                        OrderNumber = reader.IsDBNull(reader.GetOrdinal("OrderNumber")) ? "" : reader.GetString("OrderNumber"),
                        OrderType = reader.IsDBNull(reader.GetOrdinal("OrderType")) ? "" : reader.GetString("OrderType"),
                        CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Walk-in" : reader.GetString("CustomerName"),
                        Total = reader.GetDecimal("Total"),
                        Status = reader.IsDBNull(reader.GetOrdinal("Status")) ? "" : reader.GetString("Status"),
                        CreatedAt = reader.GetDateTime("CreatedAt")
                    });
                }
            }

            // Calculate dashboard stats
            viewModel.TodaySales = viewModel.Orders
                .Where(o => o.Status == "completed" && o.CreatedAt.Date == DateTime.Now.Date)
                .Sum(o => o.Total);

            viewModel.ActiveOrdersCount = viewModel.Orders.Count(o => o.Status == "pending" || o.Status == "preparing");
            viewModel.OccupiedTablesCount = viewModel.Tables.Count(t => t.Status == "occupied");
            viewModel.AvailableTablesCount = viewModel.Tables.Count(t => t.Status == "available");

            return viewModel;
        }

        public async Task<IActionResult> Index()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null)
            {
                return RedirectToAction("Login", "Login");
            }

            var userIdObj = session.GetInt32("UserId");
            if (!userIdObj.HasValue)
            {
                return RedirectToAction("Login", "Login");
            }

            var user = await GetUserById(userIdObj.Value);
            if (user == null)
            {
                return RedirectToAction("Login", "Login");
            }

            var viewModel = await CreatePOSViewModel(user);
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToOrder(int itemId)
        {
            try
            {
                var menuItem = await GetMenuItemById(itemId);
                if (menuItem == null)
                {
                    return Json(new { success = false, message = "Menu item not found" });
                }

                var currentOrder = GetCurrentOrder();
                var existingItem = currentOrder.Items.FirstOrDefault(i => i.MenuItemId == itemId);

                if (existingItem != null)
                {
                    existingItem.Quantity++;
                    existingItem.CalculateAndSetTotal();
                }
                else
                {
                    var newItem = new OrderItem
                    {
                        MenuItemId = menuItem.Id,
                        Name = menuItem.Name,
                        Price = menuItem.Price,
                        Quantity = 1
                    };
                    newItem.CalculateAndSetTotal();
                    currentOrder.Items.Add(newItem);
                }

                // Recalculate order total
                currentOrder.CalculateAndSetTotal();
                SaveCurrentOrder(currentOrder);

                return Json(new
                {
                    success = true,
                    message = $"{menuItem.Name} added to order",
                    itemCount = currentOrder.Items.Count,
                    total = currentOrder.Total,
                    subtotal = currentOrder.Total,
                    tax = currentOrder.Total * 0.10m
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding item to order: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateOrderQuantity(int itemId, int change)
        {
            try
            {
                var currentOrder = GetCurrentOrder();
                var item = currentOrder.Items.FirstOrDefault(i => i.MenuItemId == itemId);

                if (item != null)
                {
                    item.Quantity += change;
                    if (item.Quantity <= 0)
                    {
                        currentOrder.Items.Remove(item);
                    }
                    else
                    {
                        item.CalculateAndSetTotal();
                    }

                    // Recalculate order total
                    currentOrder.CalculateAndSetTotal();
                    SaveCurrentOrder(currentOrder);

                    return Json(new
                    {
                        success = true,
                        message = "Order updated",
                        total = currentOrder.Total,
                        subtotal = currentOrder.Total,
                        tax = currentOrder.Total * 0.10m,
                        itemCount = currentOrder.Items.Count
                    });
                }

                return Json(new { success = false, message = "Item not found in order" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating order: {ex.Message}" });
            }
        }

        [HttpGet]
        public IActionResult GetCurrentOrderPartial()
        {
            var currentOrder = GetCurrentOrder();
            return PartialView("_CurrentOrderPartial", currentOrder);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ClearOrder()
        {
            try
            {
                ClearCurrentOrder();
                return Json(new { success = true, message = "Order cleared" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error clearing order: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessOrder(string orderType, string customerName, string? customerPhone, int? tableId)
        {
            var currentOrder = GetCurrentOrder();

            if (currentOrder?.Items?.Any() != true)
            {
                return Json(new { success = false, message = "Please add items to the order before processing" });
            }

            if (string.IsNullOrEmpty(orderType) || string.IsNullOrEmpty(customerName))
            {
                return Json(new { success = false, message = "Please fill in all required fields" });
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // For dine-in orders without a table, auto-assign one
                if (orderType == "dine-in" && !tableId.HasValue)
                {
                    var findTableSql = "SELECT TOP 1 Id FROM Tables WHERE Status = 'available' ORDER BY Id";
                    using var findCmd = new SqlCommand(findTableSql, connection);
                    var availableTableId = await findCmd.ExecuteScalarAsync();
                    if (availableTableId != null)
                    {
                        tableId = Convert.ToInt32(availableTableId);
                    }
                }

                // Create new order
                var orderNumber = GenerateOrderNumber();
                var orderTotal = currentOrder.Total;

                var insertOrderSql = @"
                    INSERT INTO Orders (OrderNumber, OrderType, CustomerName, CustomerPhone, TableId, Status, Total, CreatedAt)
                    VALUES (@OrderNumber, @OrderType, @CustomerName, @CustomerPhone, @TableId, @Status, @Total, @CreatedAt);
                    SELECT CAST(SCOPE_IDENTITY() as int);";

                using var command = new SqlCommand(insertOrderSql, connection);
                command.Parameters.AddWithValue("@OrderNumber", orderNumber);
                command.Parameters.AddWithValue("@OrderType", orderType);
                command.Parameters.AddWithValue("@CustomerName", customerName);
                command.Parameters.AddWithValue("@CustomerPhone", string.IsNullOrEmpty(customerPhone) ? (object)DBNull.Value : customerPhone);
                command.Parameters.AddWithValue("@TableId", tableId.HasValue ? (object)tableId.Value : DBNull.Value);
                command.Parameters.AddWithValue("@Status", "pending");
                command.Parameters.AddWithValue("@Total", orderTotal);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                var result = await command.ExecuteScalarAsync();
                var orderId = result != null ? Convert.ToInt32(result) : 0;

                if (orderId == 0)
                {
                    throw new InvalidOperationException("Failed to create order");
                }

                // Add order items
                foreach (var item in currentOrder.Items)
                {
                    var insertOrderItemSql = @"
                        INSERT INTO OrderItems (OrderId, MenuItemId, Name, Price, Quantity, Total)
                        VALUES (@OrderId, @MenuItemId, @Name, @Price, @Quantity, @Total)";

                    using var itemCommand = new SqlCommand(insertOrderItemSql, connection);
                    itemCommand.Parameters.AddWithValue("@OrderId", orderId);
                    itemCommand.Parameters.AddWithValue("@MenuItemId", item.MenuItemId);
                    itemCommand.Parameters.AddWithValue("@Name", item.Name);
                    itemCommand.Parameters.AddWithValue("@Price", item.Price);
                    itemCommand.Parameters.AddWithValue("@Quantity", item.Quantity);
                    itemCommand.Parameters.AddWithValue("@Total", item.Price * item.Quantity);

                    await itemCommand.ExecuteNonQueryAsync();
                }

                // Update table status if it's a dine-in order with table
                if (tableId.HasValue && orderType == "dine-in")
                {
                    var updateTableSql = @"
                        UPDATE Tables 
                        SET Status = 'occupied', CurrentOrderId = @CurrentOrderId, UpdatedAt = @UpdatedAt
                        WHERE Id = @TableId";

                    using var tableCommand = new SqlCommand(updateTableSql, connection);
                    tableCommand.Parameters.AddWithValue("@CurrentOrderId", orderId);
                    tableCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    tableCommand.Parameters.AddWithValue("@TableId", tableId.Value);

                    await tableCommand.ExecuteNonQueryAsync();
                }

                // Clear current order from session
                ClearCurrentOrder();

                // Return the correct redirect URL (Login/Index is the main dashboard)
                return Json(new
                {
                    success = true,
                    message = $"Order #{orderNumber} processed successfully! Total: ${orderTotal:F2}",
                    orderNumber = orderNumber,
                    tableId = tableId,
                    redirectUrl = "/Login/Index"  // Changed to correct URL
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error processing order: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(int orderId, string status)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            try
            {
                // Verify order exists and get tableId and orderType
                var checkQuery = "SELECT TableId, OrderType, Status FROM Orders WHERE Id = @OrderId";
                int? tableId = null;
                string? orderType = null;

                using (var checkCommand = new SqlCommand(checkQuery, connection))
                {
                    checkCommand.Parameters.AddWithValue("@OrderId", orderId);
                    using var reader = await checkCommand.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        return Json(new { success = false, message = "Order not found" });
                    }
                    tableId = reader.IsDBNull("TableId") ? null : (int?)reader.GetInt32("TableId");
                    orderType = reader.IsDBNull("OrderType") ? null : reader.GetString("OrderType");
                }

                // Update order status
                var updateQuery = @"
                    UPDATE Orders 
                    SET Status = @Status, 
                        UpdatedAt = @UpdatedAt,
                        CompletedAt = CASE WHEN @Status = 'completed' THEN @UpdatedAt ELSE CompletedAt END
                    WHERE Id = @OrderId";

                using (var updateCommand = new SqlCommand(updateQuery, connection))
                {
                    updateCommand.Parameters.AddWithValue("@Status", status);
                    updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    updateCommand.Parameters.AddWithValue("@OrderId", orderId);

                    var rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        // If completed and has table (for dine-in orders), free up the table
                        if (status == "completed" && tableId.HasValue && orderType == "dine-in")
                        {
                            var freeTableQuery = @"
                                UPDATE Tables 
                                SET Status = 'available',
                                    CurrentOrderId = NULL,
                                    UpdatedAt = @UpdatedAt
                                WHERE Id = @TableId";

                            using var freeCommand = new SqlCommand(freeTableQuery, connection);
                            freeCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                            freeCommand.Parameters.AddWithValue("@TableId", tableId.Value);
                            await freeCommand.ExecuteNonQueryAsync();

                            return Json(new
                            {
                                success = true,
                                message = $"Order completed and Table {tableId} is now available",
                                tableFreed = true,
                                tableId = tableId
                            });
                        }

                        return Json(new { success = true, message = $"Order status updated to {status}" });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Failed to update order" });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var orders = new List<object>();

                var sql = @"
                    SELECT o.Id, o.OrderNumber, o.OrderType, o.CustomerName, o.Total, o.Status, o.CreatedAt
                    FROM Orders o
                    ORDER BY o.CreatedAt DESC";

                using (var cmd = new SqlCommand(sql, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var created = reader.GetDateTime("CreatedAt");
                        orders.Add(new
                        {
                            id = reader.GetInt32("Id"),
                            customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Walk-in" : reader.GetString("CustomerName"),
                            type = reader.IsDBNull(reader.GetOrdinal("OrderType")) ? "" : reader.GetString("OrderType"),
                            status = reader.IsDBNull(reader.GetOrdinal("Status")) ? "" : reader.GetString("Status"),
                            total = reader.GetDecimal("Total"),
                            date = created.ToString("yyyy-MM-dd"),
                            time = created.ToString("HH:mm"),
                            items = "" // populate if you need item summaries
                        });
                    }
                }

                return Json(orders);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading orders: {ex.Message}" });
            }
        }

        // Add this method alongside the existing GetOrders method
        [HttpGet]
        public async Task<IActionResult> GetAllOrders()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var orders = new List<object>();

                var sql = @"
                    SELECT o.Id, o.OrderNumber, o.OrderType, o.CustomerName, o.CustomerPhone, o.TableId, o.Total, o.Status, o.CreatedAt
                    FROM Orders o
                    ORDER BY o.CreatedAt DESC";

                using (var cmd = new SqlCommand(sql, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var created = reader.GetDateTime("CreatedAt");
                        orders.Add(new
                        {
                            id = reader.GetInt32("Id"),
                            orderNumber = reader.IsDBNull(reader.GetOrdinal("OrderNumber")) ? "" : reader.GetString("OrderNumber"),
                            orderType = reader.IsDBNull(reader.GetOrdinal("OrderType")) ? "" : reader.GetString("OrderType"),
                            customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Walk-in" : reader.GetString("CustomerName"),
                            customerPhone = reader.IsDBNull(reader.GetOrdinal("CustomerPhone")) ? "" : reader.GetString("CustomerPhone"),
                            tableId = reader.IsDBNull(reader.GetOrdinal("TableId")) ? (int?)null : reader.GetInt32("TableId"),
                            total = reader.GetDecimal("Total"),
                            status = reader.IsDBNull(reader.GetOrdinal("Status")) ? "" : reader.GetString("Status"),
                            createdAt = created.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }
                }

                return Json(new { success = true, orders = orders });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading orders: {ex.Message}" });
            }
        }

        // keep signature, but include fields expected by JS
        [HttpGet]
        public async Task<IActionResult> GetKitchenOrders()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var orders = new List<object>();
                var sql = @"
            SELECT o.Id, o.OrderNumber, o.OrderType, o.Status, o.CreatedAt
            FROM Orders o
            WHERE o.Status IN ('pending','preparing')
            ORDER BY o.CreatedAt ASC";

                using (var cmd = new SqlCommand(sql, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var orderId = reader.GetInt32("Id");
                        var orderItems = await GetOrderItems(orderId);

                        orders.Add(new
                        {
                            id = orderId,
                            ticket = reader.GetString("OrderNumber"),
                            status = reader.GetString("Status"),
                            items = orderItems, // Changed from itemsHtml to items
                            time = reader.GetDateTime("CreatedAt").ToString("HH:mm")
                        });
                    }
                }

                return Json(orders);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading kitchen orders: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetReports()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                decimal todaySales = 0;
                int completed = 0, pending = 0, total = 0;

                var salesSql = @"SELECT ISNULL(SUM(Total),0) FROM Orders WHERE CAST(CreatedAt AS DATE)=CAST(GETDATE() AS DATE) AND Status='completed'";
                var completedSql = @"SELECT COUNT(*) FROM Orders WHERE CAST(CreatedAt AS DATE)=CAST(GETDATE() AS DATE) AND Status='completed'";
                var pendingSql = @"SELECT COUNT(*) FROM Orders WHERE CAST(CreatedAt AS DATE)=CAST(GETDATE() AS DATE) AND Status IN ('pending','preparing')";
                var totalSql = @"SELECT COUNT(*) FROM Orders WHERE CAST(CreatedAt AS DATE)=CAST(GETDATE() AS DATE)";
                var recentSql = @"
                    SELECT Id, CustomerName, OrderType, Total, Status, CreatedAt
                    FROM Orders
                    WHERE CAST(CreatedAt AS DATE)=CAST(GETDATE() AS DATE)
                    ORDER BY CreatedAt DESC
                    OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY";

                using (var cmd = new SqlCommand(salesSql, connection))
                    todaySales = Convert.ToDecimal(await cmd.ExecuteScalarAsync() ?? 0);

                using (var cmd = new SqlCommand(completedSql, connection))
                    completed = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

                using (var cmd = new SqlCommand(pendingSql, connection))
                    pending = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

                using (var cmd = new SqlCommand(totalSql, connection))
                    total = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

                var recent = new List<object>();
                using (var cmd = new SqlCommand(recentSql, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        recent.Add(new
                        {
                            id = reader.GetInt32("Id"),
                            customer = reader.IsDBNull("CustomerName") ? "Walk-in" : reader.GetString("CustomerName"),
                            type = reader.GetString("OrderType"),
                            total = reader.GetDecimal("Total"),
                            status = reader.GetString("Status"),
                            time = reader.GetDateTime("CreatedAt").ToString("hh:mm tt")
                        });
                    }
                }

                return Json(new
                {
                    todaySales,
                    completed,
                    pending,
                    total,
                    recent
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading reports: {ex.Message}" });
            }
        }

        // Test endpoint to check if API is working
        [HttpGet]
        public IActionResult TestOrders()
        {
            // Create a list of anonymous objects
            var testOrders = new List<object>
    {
        new
        {
            id = 1,
            orderNumber = "ORD-20240115-123456",
            orderType = "dine-in",
            customerName = "John Doe",
            customerPhone = "555-1234",
            tableId = 5,
            status = "pending",
            total = 45.99m,
            createdAt = DateTime.Now.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss")
        },
        new
        {
            id = 2,
            orderNumber = "ORD-20240115-789012",
            orderType = "delivery",
            customerName = "Jane Smith",
            customerPhone = "555-5678",
            tableId = (int?)null,
            status = "preparing",
            total = 32.50m,
            createdAt = DateTime.Now.AddMinutes(-30).ToString("yyyy-MM-dd HH:mm:ss")
        },
        new
        {
            id = 3,
            orderNumber = "ORD-20240115-345678",
            orderType = "takeaway",
            customerName = "Bob Johnson",
            customerPhone = "555-9012",
            tableId = (int?)null,
            status = "completed",
            total = 28.75m,
            createdAt = DateTime.Now.AddHours(-2).ToString("yyyy-MM-dd HH:mm:ss")
        }
    };

            return Json(new { success = true, orders = testOrders });
        }        // Simple endpoint that always works for testing
        [HttpGet]
        public IActionResult Ping()
        {
            return Json(new
            {
                success = true,
                message = "POS Controller is working!",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken] // ensure JSON fetches without an antiforgery token still get a JSON body
        public async Task<IActionResult> AddTable([FromBody] AddTableRequest request)
        {
            if (request == null || request.Capacity <= 0)
            {
                return Json(new { success = false, message = "Invalid table data." });
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string insertSql = @"
                    INSERT INTO Tables (Capacity, Location, Type, Status, Notes, CreatedAt)
                    VALUES (@Capacity, @Location, @Type, @Status, @Notes, @CreatedAt);";

                using var cmd = new SqlCommand(insertSql, connection);
                cmd.Parameters.AddWithValue("@Capacity", request.Capacity);
                cmd.Parameters.AddWithValue("@Location", string.IsNullOrWhiteSpace(request.Location) ? (object)DBNull.Value : request.Location);
                cmd.Parameters.AddWithValue("@Type", string.IsNullOrWhiteSpace(request.Type) ? (object)DBNull.Value : request.Type);
                cmd.Parameters.AddWithValue("@Status", "available");
                cmd.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(request.Notes) ? (object)DBNull.Value : request.Notes);
                cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                await cmd.ExecuteNonQueryAsync();

                return Json(new { success = true, message = "Table added successfully." });
            }
            catch (Exception ex)
            {
                Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Json(new { success = false, message = $"Error adding table: {ex.Message}" });
            }
        }

        public class AddTableRequest
        {
            public int TableNumber { get; set; }
            public int Capacity { get; set; }
            public string? Location { get; set; }
            public string? Type { get; set; }
            public string? Notes { get; set; }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> StartTableOrder([FromBody] StartTableOrderRequest request)
        {
            if (request == null || request.TableId <= 0)
            {
                return Json(new { success = false, message = "Invalid table ID." });
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if table exists and is available
                const string checkTableSql = "SELECT Id, Status FROM Tables WHERE Id = @TableId";
                using var checkCmd = new SqlCommand(checkTableSql, connection);
                checkCmd.Parameters.AddWithValue("@TableId", request.TableId);

                using var reader = await checkCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Json(new { success = false, message = "Table not found." });
                }

                var status = reader.GetString("Status");
                reader.Close();

                if (status == "occupied")
                {
                    return Json(new { success = false, message = "Table is already occupied." });
                }

                // Update table status to occupied (order will be created when items are added)
                const string updateSql = @"
                    UPDATE Tables 
                    SET Status = 'occupied', UpdatedAt = @UpdatedAt
                    WHERE Id = @TableId";

                using var updateCmd = new SqlCommand(updateSql, connection);
                updateCmd.Parameters.AddWithValue("@TableId", request.TableId);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                await updateCmd.ExecuteNonQueryAsync();

                // Store the selected table in session for the current order
                var currentOrder = GetCurrentOrder();
                currentOrder.TableId = request.TableId;
                SaveCurrentOrder(currentOrder);

                return Json(new { success = true, message = $"Order started for Table {request.TableId}. Add items from the menu." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error starting order: {ex.Message}" });
            }
        }

        public class StartTableOrderRequest
        {
            public int TableId { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompletePayment(int orderId, string paymentMethod)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get order details including tableId and orderType
                int? tableId = null;
                string? orderType = null;
                string? currentStatus = null;

                var getOrderSql = "SELECT TableId, OrderType, Status FROM Orders WHERE Id = @OrderId";
                using (var getCmd = new SqlCommand(getOrderSql, connection))
                {
                    getCmd.Parameters.AddWithValue("@OrderId", orderId);
                    using var reader = await getCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        return Json(new { success = false, message = "Order not found" });
                    }
                    tableId = reader.IsDBNull("TableId") ? null : (int?)reader.GetInt32("TableId");
                    orderType = reader.IsDBNull("OrderType") ? null : reader.GetString("OrderType");
                    currentStatus = reader.IsDBNull("Status") ? null : reader.GetString("Status");
                }

                // Check if order is already completed
                if (currentStatus == "completed")
                {
                    return Json(new { success = false, message = "Order is already completed" });
                }

                // Update order status to completed and set payment method
                var updateOrderSql = @"
                    UPDATE Orders 
                    SET Status = 'completed', 
                        PaymentMethod = @PaymentMethod, 
                        CompletedAt = @CompletedAt,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @OrderId";

                using (var updateCmd = new SqlCommand(updateOrderSql, connection))
                {
                    updateCmd.Parameters.AddWithValue("@OrderId", orderId);
                    updateCmd.Parameters.AddWithValue("@PaymentMethod", string.IsNullOrEmpty(paymentMethod) ? "cash" : paymentMethod);
                    updateCmd.Parameters.AddWithValue("@CompletedAt", DateTime.Now);
                    updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                    var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        return Json(new { success = false, message = "Failed to update order" });
                    }
                }

                // Free up the table if it's a dine-in order with an assigned table
                if (tableId.HasValue && orderType == "dine-in")
                {
                    var freeTableSql = @"
                        UPDATE Tables 
                        SET Status = 'available', 
                            CurrentOrderId = NULL, 
                            UpdatedAt = @UpdatedAt
                        WHERE Id = @TableId";

                    using var freeCmd = new SqlCommand(freeTableSql, connection);
                    freeCmd.Parameters.AddWithValue("@TableId", tableId.Value);
                    freeCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                    await freeCmd.ExecuteNonQueryAsync();
                }

                return Json(new
                {
                    success = true,
                    message = "Payment completed successfully! Table is now available.",
                    tableId = tableId,
                    tableFreed = tableId.HasValue && orderType == "dine-in"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error completing payment: {ex.Message}" });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CompletePaymentJson([FromBody] CompletePaymentRequest request)
        {
            if (request == null || request.OrderId <= 0)
            {
                return Json(new { success = false, message = "Invalid order ID" });
            }

            return await CompletePayment(request.OrderId, request.PaymentMethod ?? "cash");
        }

        public class CompletePaymentRequest
        {
            public int OrderId { get; set; }
            public string? PaymentMethod { get; set; }
        }

        // ==================== TABLE SETTINGS ENDPOINTS ====================

        [HttpGet]
        public async Task<IActionResult> GetTableDetails(int tableId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT t.Id, t.Capacity, t.Location, t.Type, t.Status, t.Notes, t.CurrentOrderId,
                           o.Id as OrderId, o.OrderNumber, o.Total as OrderTotal, o.Status as OrderStatus
                    FROM Tables t
                    LEFT JOIN Orders o ON t.CurrentOrderId = o.Id
                    WHERE t.Id = @TableId";

                using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@TableId", tableId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Json(new { success = false, message = "Table not found" });
                }

                var table = new
                {
                    id = reader.GetInt32("Id"),
                    capacity = reader.IsDBNull("Capacity") ? 0 : reader.GetInt32("Capacity"),
                    location = reader.IsDBNull("Location") ? "" : reader.GetString("Location"),
                    type = reader.IsDBNull("Type") ? "" : reader.GetString("Type"),
                    status = reader.GetString("Status"),
                    notes = reader.IsDBNull("Notes") ? "" : reader.GetString("Notes"),
                    currentOrderId = reader.IsDBNull("CurrentOrderId") ? (int?)null : reader.GetInt32("CurrentOrderId"),
                    currentOrder = reader.IsDBNull("OrderId") ? null : new
                    {
                        id = reader.GetInt32("OrderId"),
                        orderNumber = reader.IsDBNull("OrderNumber") ? "" : reader.GetString("OrderNumber"),
                        total = reader.GetDecimal("OrderTotal"),
                        status = reader.IsDBNull("OrderStatus") ? "" : reader.GetString("OrderStatus")
                    }
                };

                return Json(new { success = true, table = table });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading table details: {ex.Message}" });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> UpdateTable([FromBody] UpdateTableRequest request)
        {
            if (request == null || request.TableId <= 0)
            {
                return Json(new { success = false, message = "Invalid table data" });
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if table exists
                var checkSql = "SELECT Id, Status, CurrentOrderId FROM Tables WHERE Id = @TableId";
                string? currentStatus = null;
                int? currentOrderId = null;

                using (var checkCmd = new SqlCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@TableId", request.TableId);
                    using var reader = await checkCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        return Json(new { success = false, message = "Table not found" });
                    }
                    currentStatus = reader.GetString("Status");
                    currentOrderId = reader.IsDBNull("CurrentOrderId") ? null : (int?)reader.GetInt32("CurrentOrderId");
                }

                // If trying to set status to 'available' and table has an active order, warn the user
                if (request.Status == "available" && currentOrderId.HasValue)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Cannot set table to available while it has an active order. Please complete or cancel the order first."
                    });
                }

                // Update table
                var updateSql = @"
                    UPDATE Tables 
                    SET Capacity = @Capacity,
                        Location = @Location,
                        Type = @Type,
                        Status = @Status,
                        Notes = @Notes,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @TableId";

                using var updateCmd = new SqlCommand(updateSql, connection);
                updateCmd.Parameters.AddWithValue("@TableId", request.TableId);
                updateCmd.Parameters.AddWithValue("@Capacity", request.Capacity > 0 ? request.Capacity : 4);
                updateCmd.Parameters.AddWithValue("@Location", string.IsNullOrWhiteSpace(request.Location) ? (object)DBNull.Value : request.Location);
                updateCmd.Parameters.AddWithValue("@Type", string.IsNullOrWhiteSpace(request.Type) ? (object)DBNull.Value : request.Type);
                updateCmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(request.Status) ? "available" : request.Status);
                updateCmd.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(request.Notes) ? (object)DBNull.Value : request.Notes);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var rowsAffected = await updateCmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Json(new { success = true, message = "Table updated successfully" });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to update table" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating table: {ex.Message}" });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SetTableAvailable([FromBody] TableIdRequest request)
        {
            if (request == null || request.TableId <= 0)
            {
                return Json(new { success = false, message = "Invalid table ID" });
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if table has an active order
                var checkSql = "SELECT CurrentOrderId FROM Tables WHERE Id = @TableId";
                using (var checkCmd = new SqlCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@TableId", request.TableId);
                    var currentOrderId = await checkCmd.ExecuteScalarAsync();

                    if (currentOrderId != null && currentOrderId != DBNull.Value)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Cannot set table to available while it has an active order. Please complete the order first."
                        });
                    }
                }

                // Set table to available
                var updateSql = @"
                    UPDATE Tables 
                    SET Status = 'available',
                        CurrentOrderId = NULL,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @TableId";

                using var updateCmd = new SqlCommand(updateSql, connection);
                updateCmd.Parameters.AddWithValue("@TableId", request.TableId);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var rowsAffected = await updateCmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Json(new { success = true, message = $"Table {request.TableId} is now available" });
                }
                else
                {
                    return Json(new { success = false, message = "Table not found" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteTable([FromBody] TableIdRequest request)
        {
            if (request == null || request.TableId <= 0)
            {
                return Json(new { success = false, message = "Invalid table ID" });
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if table has any orders (current or historical)
                var checkSql = "SELECT COUNT(*) FROM Orders WHERE TableId = @TableId";
                using (var checkCmd = new SqlCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@TableId", request.TableId);
                    var orderCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (orderCount > 0)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Cannot delete table. It has {orderCount} associated order(s). Consider marking it as unavailable instead."
                        });
                    }
                }

                // Delete table
                var deleteSql = "DELETE FROM Tables WHERE Id = @TableId";
                using var deleteCmd = new SqlCommand(deleteSql, connection);
                deleteCmd.Parameters.AddWithValue("@TableId", request.TableId);

                var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Json(new { success = true, message = "Table deleted successfully" });
                }
                else
                {
                    return Json(new { success = false, message = "Table not found" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error deleting table: {ex.Message}" });
            }
        }

        public class UpdateTableRequest
        {
            public int TableId { get; set; }
            public int Capacity { get; set; }
            public string? Location { get; set; }
            public string? Type { get; set; }
            public string? Status { get; set; }
            public string? Notes { get; set; }
        }

        public class TableIdRequest
        {
            public int TableId { get; set; }
        }

        // Request/Response classes
        public class ReservationRequest
        {
            public int TableId { get; set; }
            public string CustomerName { get; set; } = "";
            public string CustomerPhone { get; set; } = "";
            public string ReservationDate { get; set; } = "";
            public string ReservationTime { get; set; } = "";
            public int PartySize { get; set; }
            public string? SpecialRequests { get; set; }
        }

        [HttpGet]
        public IActionResult GetCurrentOrderTotal()
        {
            try
            {
                var currentOrder = GetCurrentOrder();
                var total = currentOrder?.Items?.Sum(i => i.Price * i.Quantity) ?? 0;
                var itemCount = currentOrder?.Items?.Count ?? 0;

                return Json(new
                {
                    success = true,
                    total = total,
                    itemCount = itemCount
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task<List<OrderItem>> GetOrderItems(int orderId)
        {
            var items = new List<OrderItem>();
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = "SELECT Name, Quantity FROM OrderItems WHERE OrderId = @OrderId";
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@OrderId", orderId);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new OrderItem
                {
                    Name = reader.GetString("Name"),
                    Quantity = reader.GetInt32("Quantity")
                });
            }

            return items;
        }
    }
}