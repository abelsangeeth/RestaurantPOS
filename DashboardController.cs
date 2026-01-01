using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace RestaurantPOS.Controllers
{
    public class DashboardController : Controller
    {
        private readonly IConfiguration _config;

        public DashboardController(IConfiguration config)
        {
            _config = config;
        }

        // ==========================
        // DASHBOARD MAIN VIEW
        // ==========================
        public IActionResult Index()
        {
            return View();
        }

        // ==========================
        // LIVE SALES (TODAY - HOURLY)
        // ==========================
        [HttpGet]
        public IActionResult GetLiveSalesChart()
        {
            var labels = new List<string>();
            var data = new List<decimal>();

            using (SqlConnection con =
                new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                con.Open();

                string query = @"
                    SELECT 
                        FORMAT(CreatedAt, 'HH:mm') AS TimeLabel,
                        SUM(Total) AS TotalSales
                    FROM Orders
                    WHERE 
                        CAST(CreatedAt AS DATE) = CAST(GETDATE() AS DATE)
                        AND Status = 'completed'
                    GROUP BY FORMAT(CreatedAt, 'HH:mm')
                    ORDER BY TimeLabel;
                ";

                using (SqlCommand cmd = new SqlCommand(query, con))
                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        labels.Add(dr["TimeLabel"].ToString());
                        data.Add(Convert.ToDecimal(dr["TotalSales"]));
                    }
                }
            }

            return Json(new { labels, data });
        }

        // ==========================
        // ORDER STATUS CHART
        // ==========================
        [HttpGet]
        public IActionResult GetOrderStatusChart()
        {
            var labels = new List<string>();
            var data = new List<int>();

            using (SqlConnection con =
                new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                con.Open();

                string query = @"
                    SELECT Status, COUNT(*) AS TotalCount
                    FROM Orders
                    GROUP BY Status;
                ";

                using (SqlCommand cmd = new SqlCommand(query, con))
                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        labels.Add(dr["Status"].ToString());
                        data.Add(Convert.ToInt32(dr["TotalCount"]));
                    }
                }
            }

            return Json(new { labels, data });
        }

        // ==========================
        // ORDER TYPE CHART (OPTIONAL)
        // ==========================
        [HttpGet]
        public IActionResult GetOrderTypeChart()
        {
            var labels = new List<string>();
            var data = new List<int>();

            using (SqlConnection con =
                new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                con.Open();

                string query = @"
                    SELECT OrderType, COUNT(*) AS TotalCount
                    FROM Orders
                    GROUP BY OrderType;
                ";

                using (SqlCommand cmd = new SqlCommand(query, con))
                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        labels.Add(dr["OrderType"].ToString());
                        data.Add(Convert.ToInt32(dr["TotalCount"]));
                    }
                }
            }

            return Json(new { labels, data });
        }
    }
}
