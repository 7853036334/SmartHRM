using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartHRM.DataAccess;
using SmartHRM.DataAccess.Entities;


namespace SmartHRM.Presentation.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;

    public DashboardController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateTime.Today;

        var totalEmployees = await _context.Employees.CountAsync(e => e.IsActive);
        var todayLogs = await _context.AttendanceLogs
            .Include(a => a.Employee)
            .Where(a => a.Date == today)
            .ToListAsync();

        var presentCount = todayLogs.Count;
        var absentCount = Math.Max(0, totalEmployees - presentCount);
        var lateCount = todayLogs.Count(a => a.Status == "Late");

        // Department-wise distribution
        var deptStats = await _context.Employees
            .Where(e => e.IsActive && !string.IsNullOrEmpty(e.Department))
            .GroupBy(e => e.Department!)
            .Select(g => new { Department = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Department, x => x.Count);

        // Recent real-time activities for the stream list
        var recentActivities = todayLogs
     .OrderByDescending(a => a.TimeIn)
     .Take(5)
     .Select(a => new RecentActivityItem
     {
         FullName = a.Employee?.FullName ?? "Unknown",
         Action = $"marked {a.Status}",
         TimeString = a.TimeIn.HasValue
             ? DateTime.Today.Add(a.TimeIn.Value).ToString("hh:mm tt")
             : "--:--",
         PhotoPath = a.Employee?.PhotoPath
     })
     .ToList();

        var model = new DashboardViewModel
        {
            TotalEmployees = totalEmployees,
            PresentToday = presentCount,
            AbsentToday = absentCount,
            LateArrivals = lateCount,
            DepartmentWiseCount = deptStats,
            RecentActivities = recentActivities,
            // Sample weekly curve Mon-Sun
            WeeklyAttendanceTrends = new List<int> { 420, 460, 480, 475, 490, 210, 50 }
        };

        return View(model);
    }
}
