using Microsoft.AspNetCore.Mvc;

namespace RestaurantPOS.Controllers
{
    public class TablesController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
