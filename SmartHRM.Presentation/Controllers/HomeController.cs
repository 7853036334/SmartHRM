using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartHRM.DataAccess;

namespace SmartHRM.Presentation.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateTime.UtcNow.Date;
        var totalEmployees = await _context.Employees.CountAsync(e => e.IsActive);

        var todayLogs = await _context.AttendanceLogs
            .Include(a => a.Employee)
            .Where(a => a.Date == today)
            .ToListAsync();

        var presentCount = todayLogs.Count(a => a.Status == "Present");
        var lateCount = todayLogs.Count(a => a.Status == "Late");
        var absentCount = totalEmployees - (presentCount + lateCount);

        ViewBag.TotalEmployees = totalEmployees;
        ViewBag.PresentToday = presentCount;
        ViewBag.LateArrivals = lateCount;
        ViewBag.AbsentToday = Math.Max(0, absentCount);
        ViewBag.RecentLogs = todayLogs.OrderByDescending(a => a.TimeIn).Take(5).ToList();

        return View();
    }
}
