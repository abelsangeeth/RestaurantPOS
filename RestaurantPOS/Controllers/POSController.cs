// Controllers/POSController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantPOS.Models;
using System.Data;
using System.Text.Json;

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
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        // Helper method to get current order from session
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

        // Helper method to save current order to session
        private void SaveCurrentOrder(Order order)
        {
            var session = _httpContextAccessor.HttpContext.Session;
            var orderJson = JsonSerializer.Serialize(order);
            session.SetString("CurrentOrder", orderJson);
        }

        // Generate unique order number
        private string GenerateOrderNumber()
        {
            return $"ORD-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        public async Task<IActionResult> Index()
        {
            var userId = _httpContextAccessor.HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("Login");
            }

            var user = await GetUserById(userId.Value);
            if (user == null)
            {
                return RedirectToAction("Login");
            }

            var viewModel = await CreatePOSViewModel(user);
            return View(viewModel);
        }

        [HttpGet]
       

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
                    total = currentOrder.Total
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding item to order: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderQuantity(int itemId, int change)
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
                        TempData["Success"] = "Item removed from order";
                    }
                    else
                    {
                        item.CalculateAndSetTotal();
                        TempData["Success"] = "Order updated";
                    }

                    // Recalculate order total
                    currentOrder.CalculateAndSetTotal();
                }

                SaveCurrentOrder(currentOrder);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error updating order: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessOrder(string orderType, string customerName, string customerPhone, int? tableId)
        {
            var currentOrder = GetCurrentOrder();

            if (currentOrder?.Items?.Any() != true)
            {
                TempData["Error"] = "Please add items to the order before processing";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrEmpty(orderType) || string.IsNullOrEmpty(customerName))
            {
                TempData["Error"] = "Please fill in all required fields";
                return RedirectToAction("Index");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Create new order
                var orderNumber = GenerateOrderNumber();
                var orderTotal = currentOrder.Items.Sum(item => item.Price * item.Quantity);

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

                var orderId = Convert.ToInt32(await command.ExecuteScalarAsync());

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

                // Update table status if it's a dine-in order
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
                SaveCurrentOrder(new Order { Items = new List<OrderItem>() });

                TempData["Success"] = $"Order #{orderNumber} processed successfully! Order total: ${orderTotal:F2}";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error processing order: {ex.Message}";
            }

            return RedirectToAction("Index");
        }
        [HttpPost]
      /*  public async Task<IActionResult> UpdateOrderStatus(int orderId, string status)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    UPDATE Orders 
                    SET Status = @Status, UpdatedAt = @UpdatedAt
                    WHERE Id = @OrderId";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Status", status);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@OrderId", orderId);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0 && status == "completed")
                {
                    // Clear table if it was a dine-in order
                    var clearTableSql = @"
                        UPDATE Tables 
                        SET Status = 'available', CurrentOrderId = NULL, UpdatedAt = @UpdatedAt
                        WHERE CurrentOrderId = @OrderId";

                    using var clearCommand = new SqlCommand(clearTableSql, connection);
                    clearCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    clearCommand.Parameters.AddWithValue("@OrderId", orderId);

                    await clearCommand.ExecuteNonQueryAsync();
                }

                TempData["Success"] = $"Order status updated to {status}";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error updating order status: {ex.Message}";
            }

            return RedirectToAction("Index");
        }*/

        [HttpPost]
        public async Task<IActionResult> CompletePayment(int orderId, string paymentMethod)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    UPDATE Orders 
                    SET Status = 'completed', PaymentMethod = @PaymentMethod, 
                        CompletedAt = @CompletedAt, UpdatedAt = @UpdatedAt
                    WHERE Id = @OrderId";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@PaymentMethod", paymentMethod);
                command.Parameters.AddWithValue("@CompletedAt", DateTime.Now);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@OrderId", orderId);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    // Clear table if it was a dine-in order
                    var clearTableSql = @"
                        UPDATE Tables 
                        SET Status = 'available', CurrentOrderId = NULL, UpdatedAt = @UpdatedAt
                        WHERE CurrentOrderId = @OrderId";

                    using var clearCommand = new SqlCommand(clearTableSql, connection);
                    clearCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    clearCommand.Parameters.AddWithValue("@OrderId", orderId);

                    await clearCommand.ExecuteNonQueryAsync();
                }

                TempData["Success"] = $"Payment completed successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error processing payment: {ex.Message}";
            }

            return RedirectToAction("Index");
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTable(int tableNumber, int capacity, string location = "Main Dining", string type = "Regular", string notes = "")
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    // Check if table number already exists
                    var checkQuery = "SELECT COUNT(*) FROM Tables WHERE Id = @TableNumber";
                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@TableNumber", tableNumber);
                        var tableExists = (int)await checkCommand.ExecuteScalarAsync() > 0;

                        if (tableExists)
                        {
                            return Json(new { success = false, message = $"Table {tableNumber} already exists" });
                        }
                    }

                    // Insert new table
                    var insertQuery = @"
                        INSERT INTO Tables (Id, Capacity, Location, Type, Status, Notes, CreatedDate)
                        VALUES (@Id, @Capacity, @Location, @Type, @Status, @Notes, @CreatedDate)";

                    using (var insertCommand = new SqlCommand(insertQuery, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@Id", tableNumber);
                        insertCommand.Parameters.AddWithValue("@Capacity", capacity);
                        insertCommand.Parameters.AddWithValue("@Location", location);
                        insertCommand.Parameters.AddWithValue("@Type", type);
                        insertCommand.Parameters.AddWithValue("@Status", "available");
                        insertCommand.Parameters.AddWithValue("@Notes", notes ?? "");
                        insertCommand.Parameters.AddWithValue("@CreatedDate", DateTime.Now);

                        var rowsAffected = await insertCommand.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = $"Table {tableNumber} added successfully" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Failed to add table" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error adding table: {ex.Message}" });
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTableStatus(int tableId, string status)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    // First check if table exists
                    var checkQuery = "SELECT COUNT(*) FROM Tables WHERE Id = @TableId";
                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@TableId", tableId);
                        var tableExists = (int)await checkCommand.ExecuteScalarAsync() > 0;

                        if (!tableExists)
                        {
                            return Json(new { success = false, message = "Table not found" });
                        }
                    }

                    // Update table status
                    var updateQuery = @"
                        UPDATE Tables 
                        SET Status = @Status, 
                            Updatedat = @Updatedat
                        WHERE Id = @TableId";

                    // If setting to available, clear any current order
                    if (status == "available")
                    {
                        var clearOrderQuery = "UPDATE Tables SET CurrentOrderId = NULL WHERE Id = @TableId";
                        using (var clearCommand = new SqlCommand(clearOrderQuery, connection))
                        {
                            clearCommand.Parameters.AddWithValue("@TableId", tableId);
                            await clearCommand.ExecuteNonQueryAsync();
                        }
                    }

                    using (var updateCommand = new SqlCommand(updateQuery, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@Status", status);
                        updateCommand.Parameters.AddWithValue("@Updatedat", DateTime.Now);
                        updateCommand.Parameters.AddWithValue("@TableId", tableId);

                        var rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = $"Table {tableId} status updated to {status}" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Failed to update table status" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error updating table: {ex.Message}" });
                }
            }
        }

        //000
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignOrderToTable(int tableId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    // Get current order from session
                    var currentOrder = GetCurrentOrder();
                    if (currentOrder?.Items?.Any() != true)
                    {
                        return Json(new { success = false, message = "No active order to assign" });
                    }

                    // Check if table exists
                    var checkTableQuery = "SELECT COUNT(*) FROM Tables WHERE Id = @TableId";
                    using (var checkCommand = new SqlCommand(checkTableQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@TableId", tableId);
                        var tableExists = (int)await checkCommand.ExecuteScalarAsync() > 0;

                        if (!tableExists)
                        {
                            return Json(new { success = false, message = "Table not found" });
                        }
                    }

                    // Insert the order
                    var orderNumber = $"ORD-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

                    var insertOrderQuery = @"
                        INSERT INTO Orders (OrderNumber, TableId, OrderType, CustomerName, Status, Total, CreatedAt)
                        OUTPUT INSERTED.Id
                        VALUES (@OrderNumber, @TableId, @OrderType, @CustomerName, @Status, @Total, @CreatedAt)";

                    int orderId;
                    using (var orderCommand = new SqlCommand(insertOrderQuery, connection))
                    {
                        orderCommand.Parameters.AddWithValue("@OrderNumber", orderNumber);
                        orderCommand.Parameters.AddWithValue("@TableId", tableId);
                        orderCommand.Parameters.AddWithValue("@OrderType", "dine-in");
                        orderCommand.Parameters.AddWithValue("@CustomerName", currentOrder.CustomerName ?? "Walk-in Customer");
                        orderCommand.Parameters.AddWithValue("@Status", "pending");
                        orderCommand.Parameters.AddWithValue("@Total", currentOrder.Total);
                        orderCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                        orderId = (int)await orderCommand.ExecuteScalarAsync();
                    }

                    // Insert order items
                    var insertItemQuery = @"
                        INSERT INTO OrderItems (OrderId, MenuItemId, Name, Price, Quantity, Total)
                        VALUES (@OrderId, @MenuItemId, @Name, @Price, @Quantity, @Total)";

                    foreach (var item in currentOrder.Items)
                    {
                        using (var itemCommand = new SqlCommand(insertItemQuery, connection))
                        {
                            itemCommand.Parameters.AddWithValue("@OrderId", orderId);
                            itemCommand.Parameters.AddWithValue("@MenuItemId", item.MenuItemId);
                            itemCommand.Parameters.AddWithValue("@Name", item.Name);
                            itemCommand.Parameters.AddWithValue("@Price", item.Price);
                            itemCommand.Parameters.AddWithValue("@Quantity", item.Quantity);
                            itemCommand.Parameters.AddWithValue("@Total", item.Price * item.Quantity);

                            await itemCommand.ExecuteNonQueryAsync();
                        }
                    }

                    // Update table status and assign order
                    var updateTableQuery = @"
                        UPDATE Tables 
                        SET Status = @Status, 
                            CurrentOrderId = @CurrentOrderId,
                            Updatedat = @Updatedat
                        WHERE Id = @TableId";

                    using (var updateCommand = new SqlCommand(updateTableQuery, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@Status", "occupied");
                        updateCommand.Parameters.AddWithValue("@CurrentOrderId", orderId);
                        updateCommand.Parameters.AddWithValue("@Updatedat", DateTime.Now);
                        updateCommand.Parameters.AddWithValue("@TableId", tableId);

                        var rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            // Clear current order from session
                            ClearCurrentOrder();

                            return Json(new
                            {
                                success = true,
                                message = $"Order assigned to Table {tableId}",
                                orderNumber = orderNumber
                            });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Failed to assign order to table" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error assigning order: {ex.Message}" });
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTableDetails(int tableId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    var query = @"
                        SELECT t.Id, t.Capacity, t.Location, t.Type, t.Status, t.Notes, 
                               o.OrderNumber, o.Total as OrderTotal, o.Status as OrderStatus,
                               o.CustomerName
                        FROM Tables t
                        LEFT JOIN Orders o ON t.CurrentOrderId = o.Id
                        WHERE t.Id = @TableId";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@TableId", tableId);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var tableDetails = new
                                {
                                    id = reader.GetInt32("Id"),
                                    capacity = reader.GetInt32("Capacity"),
                                    location = reader.GetString("Location"),
                                    type = reader.GetString("Type"),
                                    status = reader.GetString("Status"),
                                    notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? "" : reader.GetString("Notes"),
                                    currentOrder = !reader.IsDBNull(reader.GetOrdinal("OrderNumber")) ? new
                                    {
                                        orderNumber = reader.GetString("OrderNumber"),
                                        total = reader.GetDecimal("OrderTotal"),
                                        status = reader.GetString("OrderStatus"),
                                        customerName = reader.GetString("CustomerName")
                                    } : null
                                };

                                return Json(new { success = true, table = tableDetails });
                            }
                            else
                            {
                                return Json(new { success = false, message = "Table not found" });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error getting table details: {ex.Message}" });
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateReservation([FromBody] ReservationRequest request)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    // Combine date and time
                    var reservationDateTime = DateTime.Parse($"{request.ReservationDate} {request.ReservationTime}");

                    // Check if table exists and is available
                    var checkTableQuery = @"
                        SELECT Status 
                        FROM Tables 
                        WHERE Id = @TableId";

                    using (var checkCommand = new SqlCommand(checkTableQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@TableId", request.TableId);

                        using (var reader = await checkCommand.ExecuteReaderAsync())
                        {
                            if (!await reader.ReadAsync())
                            {
                                return Json(new { success = false, message = "Table not found" });
                            }

                            var tableStatus = reader.GetString("Status");
                            if (tableStatus != "available")
                            {
                                return Json(new { success = false, message = "Table is not available for reservation" });
                            }
                        }
                    }

                    // Create reservation
                    var insertReservationQuery = @"
                        INSERT INTO Reservations (TableId, CustomerName, CustomerPhone, ReservationDateTime, PartySize, SpecialRequests, Status, CreatedDate)
                        VALUES (@TableId, @CustomerName, @CustomerPhone, @ReservationDateTime, @PartySize, @SpecialRequests, @Status, @CreatedDate)";

                    using (var insertCommand = new SqlCommand(insertReservationQuery, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@TableId", request.TableId);
                        insertCommand.Parameters.AddWithValue("@CustomerName", request.CustomerName);
                        insertCommand.Parameters.AddWithValue("@CustomerPhone", request.CustomerPhone);
                        insertCommand.Parameters.AddWithValue("@ReservationDateTime", reservationDateTime);
                        insertCommand.Parameters.AddWithValue("@PartySize", request.PartySize);
                        insertCommand.Parameters.AddWithValue("@SpecialRequests", request.SpecialRequests ?? "");
                        insertCommand.Parameters.AddWithValue("@Status", "confirmed");
                        insertCommand.Parameters.AddWithValue("@CreatedDate", DateTime.Now);

                        await insertCommand.ExecuteNonQueryAsync();
                    }

                    // Update table status
                    var updateTableQuery = @"
                        UPDATE Tables 
                        SET Status = @Status, 
                            Updatedat = @Updatedat
                        WHERE Id = @TableId";

                    using (var updateCommand = new SqlCommand(updateTableQuery, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@Status", "reserved");
                        updateCommand.Parameters.AddWithValue("@Updatedat", DateTime.Now);
                        updateCommand.Parameters.AddWithValue("@TableId", request.TableId);

                        await updateCommand.ExecuteNonQueryAsync();
                    }

                    return Json(new { success = true, message = "Reservation created successfully" });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error creating reservation: {ex.Message}" });
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartTableOrder(int tableId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    // Check if table exists and is available
                    var checkQuery = @"
                        SELECT Status 
                        FROM Tables 
                        WHERE Id = @TableId";

                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@TableId", tableId);

                        using (var reader = await checkCommand.ExecuteReaderAsync())
                        {
                            if (!await reader.ReadAsync())
                            {
                                return Json(new { success = false, message = "Table not found" });
                            }

                            var tableStatus = reader.GetString("Status");
                            if (tableStatus != "available")
                            {
                                return Json(new { success = false, message = "Table is not available" });
                            }
                        }
                    }

                    // Update table status to occupied
                    var updateQuery = @"
                        UPDATE Tables 
                        SET Status = @Status, 
                            Updatedat = @Updatedat
                        WHERE Id = @TableId";

                    using (var updateCommand = new SqlCommand(updateQuery, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@Status", "occupied");
                        updateCommand.Parameters.AddWithValue("@Updatedat", DateTime.Now);
                        updateCommand.Parameters.AddWithValue("@TableId", tableId);

                        var rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = "Table order started" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Failed to start table order" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error starting order: {ex.Message}" });
                }
            }
        }

        [HttpGet]
     /*   public async Task<IActionResult> GetTables()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    var query = @"
                        SELECT t.Id, t.Capacity, t.Location, t.Type, t.Status, t.Notes,
                               o.OrderNumber, o.Total as OrderTotal, o.CustomerName
                        FROM Tables t
                        LEFT JOIN Orders o ON t.CurrentOrderId = o.Id AND o.Status IN ('pending', 'preparing')
                        ORDER BY t.Id";

                    var tables = new List<object>();

                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var table = new
                            {
                                id = reader.GetInt32("Id"),
                                capacity = reader.GetInt32("Capacity"),
                                location = reader.GetString("Location"),
                                type = reader.GetString("Type"),
                                status = reader.GetString("Status"),
                                notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? "" : reader.GetString("Notes"),
                                currentOrder = !reader.IsDBNull(reader.GetOrdinal("OrderNumber")) ? new
                                {
                                    orderNumber = reader.GetString("OrderNumber"),
                                    total = reader.GetDecimal("OrderTotal"),
                                    customerName = reader.GetString("CustomerName")
                                } : null
                            };

                            tables.Add(table);
                        }
                    }

                    return Json(new { success = true, tables = tables });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error getting tables: {ex.Message}" });
                }
            }
        }*/

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTable(int tableId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    // First check if table has active orders
                    var checkOrdersQuery = @"
                        SELECT COUNT(*) 
                        FROM Orders 
                        WHERE TableId = @TableId AND Status IN ('pending', 'preparing')";

                    using (var checkCommand = new SqlCommand(checkOrdersQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@TableId", tableId);
                        var hasActiveOrders = (int)await checkCommand.ExecuteScalarAsync() > 0;

                        if (hasActiveOrders)
                        {
                            return Json(new { success = false, message = "Cannot delete table with active orders" });
                        }
                    }

                    // DELETE query
                    var deleteQuery = "DELETE FROM Tables WHERE Id = @TableId";

                    using (var deleteCommand = new SqlCommand(deleteQuery, connection))
                    {
                        deleteCommand.Parameters.AddWithValue("@TableId", tableId);

                        var rowsAffected = await deleteCommand.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = $"Table {tableId} deleted successfully" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Table not found" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error deleting table: {ex.Message}" });
                }
            }
        }

        // Helper methods for session management
       

        private void ClearCurrentOrder()
        {
            HttpContext.Session.Remove("CurrentOrder");
        }
    




        //0000


        private async Task DebugTables()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if Tables table exists
                var checkTableExists = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Tables'";
                using var checkCommand = new SqlCommand(checkTableExists, connection);
                var tableExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                Console.WriteLine($"Tables table exists: {tableExists > 0}");

                if (tableExists > 0)
                {
                    // Get all tables
                    var getTables = "SELECT Id, Status, Capacity FROM Tables ORDER BY Id";
                    using var tablesCommand = new SqlCommand(getTables, connection);
                    using var reader = await tablesCommand.ExecuteReaderAsync();

                    Console.WriteLine("Current tables in database:");
                    while (await reader.ReadAsync())
                    {
                        Console.WriteLine($"Table {reader.GetInt32("Id")} - Status: {reader.GetString("Status")} - Capacity: {reader.GetInt32("Capacity")}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Debug error: {ex.Message}");
            }
        }
        // Database Query Methods using SqlDataReader
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
                    Id = reader.GetInt32("Id"),
                    Name = reader.GetString("Name"),
                    Price = reader.GetDecimal("Price"),
                    Category = reader.GetString("Category"),
                    Description = reader.IsDBNull("Description") ? null : reader.GetString("Description"),
                    IsAvailable = reader.GetBoolean("IsAvailable"),
                    CreatedAt = reader.GetDateTime("CreatedAt"),
                    UpdatedAt = reader.IsDBNull("UpdatedAt") ? null : reader.GetDateTime("UpdatedAt")
                };
            }

            return null;
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

                // Create current order if exists
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

        private async Task<List<Order>> GetOrders()
        {
            var orders = new List<Order>();
            var orderItems = new Dictionary<int, List<OrderItem>>();

            // First get all orders
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get orders
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

                // Create table if exists
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

            // Now get all order items
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

            // Assign items to orders
            foreach (var order in orders)
            {
                if (orderItems.ContainsKey(order.Id))
                {
                    order.Items = orderItems[order.Id];
                }
            }

            return orders;
        }

        private async Task<POSViewModel> CreatePOSViewModel(User user)
        {
            var today = DateTime.Today;

            // Get data from database using raw ADO.NET
            var menuItems = await GetMenuItems();
            var tables = await GetTables();
            var orders = await GetOrders();

            var todayOrders = orders.Where(o => o.CreatedAt.Date == today).ToList();

            // Calculate dashboard statistics
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
        // AJAX method to add item to order
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> AddToOrderAjax(int itemId)
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

                currentOrder.CalculateAndSetTotal();
                SaveCurrentOrder(currentOrder);

                return Json(new { success = true, message = $"{menuItem.Name} added to order" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // AJAX method to update quantity
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> UpdateOrderQuantityAjax(int itemId, int change)
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

                    currentOrder.CalculateAndSetTotal();
                    SaveCurrentOrder(currentOrder);
                }

                return Json(new { success = true, message = "Order updated" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Method to get current order partial view for AJAX updates
        [HttpGet]
        //public IActionResult GetCurrentOrderPartial()
        //{
        //    var currentOrder = GetCurrentOrder();
        //    return PartialView("_CurrentOrderPartial", currentOrder);
        //}

        /// Order Management
        // Add these methods to your POSController
        [HttpGet]
        public async Task<IActionResult> GetAllOrders()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    var query = @"
                SELECT 
                    o.Id, o.OrderNumber, o.OrderType, o.CustomerName, o.CustomerPhone,
                    o.TableId, o.Status, o.Total, o.CreatedAt,
                    o.DeliveryAddress, o.DriverId, o.EstimatedDeliveryTime,
                    (SELECT COUNT(*) FROM OrderItems WHERE OrderId = o.Id) as ItemCount
                FROM Orders o
                WHERE o.Status != 'deleted'
                ORDER BY 
                    CASE 
                        WHEN o.Status = 'pending' THEN 1
                        WHEN o.Status = 'preparing' THEN 2
                        WHEN o.Status = 'ready' THEN 3
                        WHEN o.Status = 'completed' THEN 4
                        ELSE 5
                    END,
                    o.CreatedAt DESC";

                    var orders = new List<object>();

                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var orderId = reader.GetInt32("Id");

                            // Get order items
                            var items = await GetOrderItemsAsync(connection, orderId);

                            var order = new
                            {
                                id = orderId,
                                orderNumber = reader.GetString("OrderNumber"),
                                orderType = reader.GetString("OrderType"),
                                customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString("CustomerName"),
                                customerPhone = reader.IsDBNull(reader.GetOrdinal("CustomerPhone")) ? null : reader.GetString("CustomerPhone"),
                                tableId = reader.IsDBNull(reader.GetOrdinal("TableId")) ? null : (int?)reader.GetInt32("TableId"),
                                status = reader.GetString("Status"),
                                total = reader.GetDecimal("Total"),
                                createdAt = reader.GetDateTime("CreatedAt"),
                                deliveryAddress = reader.IsDBNull(reader.GetOrdinal("DeliveryAddress")) ? null : reader.GetString("DeliveryAddress"),
                                itemCount = reader.GetInt32("ItemCount"),
                                items = items
                            };

                            orders.Add(order);
                        }
                    }

                    return Json(new { success = true, orders = orders });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error getting orders: {ex.Message}" });
                }
            }
        }

        private async Task<List<object>> GetOrderItemsAsync(SqlConnection connection, int orderId)
        {
            var items = new List<object>();

            var itemsQuery = @"
        SELECT MenuItemId, Name, Price, Quantity, Total
        FROM OrderItems 
        WHERE OrderId = @OrderId";

            using (var itemsCommand = new SqlCommand(itemsQuery, connection))
            {
                itemsCommand.Parameters.AddWithValue("@OrderId", orderId);

                using (var itemsReader = await itemsCommand.ExecuteReaderAsync())
                {
                    while (await itemsReader.ReadAsync())
                    {
                        var item = new
                        {
                            menuItemId = itemsReader.GetInt32("MenuItemId"),
                            name = itemsReader.GetString("Name"),
                            price = itemsReader.GetDecimal("Price"),
                            quantity = itemsReader.GetInt32("Quantity"),
                            total = itemsReader.GetDecimal("Total")
                        };
                        items.Add(item);
                    }
                }
            }

            return items;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(int orderId, string status)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    var query = @"
                UPDATE Orders 
                SET Status = @Status,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @OrderId";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Status", status);
                        command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                        command.Parameters.AddWithValue("@OrderId", orderId);

                        var rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            // If order is completed and it's a dine-in order, free up the table
                            if (status == "completed")
                            {
                                await FreeTableForCompletedOrder(connection, orderId);
                            }

                            return Json(new { success = true, message = $"Order status updated to {status}" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Order not found" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error updating order status: {ex.Message}" });
                }
            }
        }

        private async Task FreeTableForCompletedOrder(SqlConnection connection, int orderId)
        {
            var freeTableQuery = @"
        UPDATE Tables 
        SET Status = 'available',
            CurrentOrderId = NULL,
            UpdatedDate = @UpdatedDate
        WHERE CurrentOrderId = @OrderId";

            using (var command = new SqlCommand(freeTableQuery, connection))
            {
                command.Parameters.AddWithValue("@OrderId", orderId);
                command.Parameters.AddWithValue("@UpdatedDate", DateTime.Now);
                await command.ExecuteNonQueryAsync();
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetOrdersByType(string orderType)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    var query = @"
                SELECT Id, OrderNumber, OrderType, CustomerName, Status, Total, CreatedAt
                FROM Orders 
                WHERE OrderType = @OrderType AND Status != 'deleted'
                ORDER BY CreatedAt DESC";

                    var orders = new List<object>();

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@OrderType", orderType);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var order = new
                                {
                                    id = reader.GetInt32("Id"),
                                    orderNumber = reader.GetString("OrderNumber"),
                                    orderType = reader.GetString("OrderType"),
                                    customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString("CustomerName"),
                                    status = reader.GetString("Status"),
                                    total = reader.GetDecimal("Total"),
                                    createdAt = reader.GetDateTime("CreatedAt")
                                };
                                orders.Add(order);
                            }
                        }
                    }

                    return Json(new { success = true, orders = orders });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"Error getting orders: {ex.Message}" });
                }
            }
        }

        [HttpGet]
        public IActionResult GetCurrentOrderPartial()
        {
            try
            {
                var currentOrder = GetCurrentOrder();
                return PartialView("_CurrentOrderPartial", currentOrder);
            }
            catch (Exception ex)
            {
                // Return empty order on error
                return PartialView("_CurrentOrderPartial", new Order { Items = new List<OrderItem>() });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ClearOrder()
        {
            try
            {
                SaveCurrentOrder(new Order { Items = new List<OrderItem>() });
                return Json(new { success = true, message = "Order cleared" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error clearing order: {ex.Message}" });
            }
        }

    }
}