using Microsoft.AspNetCore.Mvc;
using Sample.Models;
using System.Diagnostics;

namespace Sample.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            Response.Cookies.Append("Username", "amit");
            return View();
        }

        public IActionResult Privacy()
        {
            string? Name = Request.Cookies["Username"];
            ViewBag.name = Name;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

