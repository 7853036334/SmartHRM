using Microsoft.AspNetCore.Mvc;

namespace SmartHRM.Presentation.Controllers
{
    public class AboutController : Controller
    {
        public IActionResult about()
        {
            return View();
        }
    }
}