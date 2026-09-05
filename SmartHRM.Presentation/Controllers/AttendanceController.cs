using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartHRM.DataAccess;

namespace SmartHRM.Presentation.Controllers;

[Authorize]
public class AttendanceController : Controller
{
    private readonly ApplicationDbContext _context;

    public AttendanceController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Attendance or /Attendance/Index
    [HttpGet]
    public IActionResult Index(DateTime? startDate, DateTime? endDate, string? department, string? status)
    {
        return RedirectToAction(nameof(Reports), new { startDate, endDate, department, status });
    }

    // GET: /Attendance/Reports
    [HttpGet]
    public async Task<IActionResult> Reports(DateTime? startDate, DateTime? endDate, string? department, string? status)
    {
        var start = startDate ?? DateTime.Today.AddDays(-7);
        var end = endDate ?? DateTime.Today;

        var query = _context.AttendanceLogs
            .Include(a => a.Employee)
            .Where(a => a.Date >= start && a.Date <= end)
            .AsQueryable();

        if (!string.IsNullOrEmpty(department) && department != "All")
        {
            query = query.Where(a => a.Employee != null && a.Employee.Department == department);
        }

        if (!string.IsNullOrEmpty(status) && status != "All")
        {
            query = query.Where(a => a.Status == status);
        }

        var logs = await query
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.TimeIn)
            .ToListAsync();

        ViewBag.StartDate = start.ToString("yyyy-MM-dd");
        ViewBag.EndDate = end.ToString("yyyy-MM-dd");
        ViewBag.Department = department ?? "All";
        ViewBag.Status = status ?? "All";

        return View("Reports", logs);
    }

    // GET: /Attendance/ExportToExcel
    [HttpGet]
    public async Task<IActionResult> ExportToExcel(DateTime? startDate, DateTime? endDate, string? department, string? status)
    {
        var start = startDate ?? DateTime.Today.AddDays(-7);
        var end = endDate ?? DateTime.Today;

        var query = _context.AttendanceLogs
            .Include(a => a.Employee)
            .Where(a => a.Date >= start && a.Date <= end)
            .AsQueryable();

        if (!string.IsNullOrEmpty(department) && department != "All")
        {
            query = query.Where(a => a.Employee != null && a.Employee.Department == department);
        }

        if (!string.IsNullOrEmpty(status) && status != "All")
        {
            query = query.Where(a => a.Status == status);
        }

        var logs = await query
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.TimeIn)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Attendance Logs");

        // Table Header Definitions
        worksheet.Cell(1, 1).Value = "Employee Code";
        worksheet.Cell(1, 2).Value = "Full Name";
        worksheet.Cell(1, 3).Value = "Department";
        worksheet.Cell(1, 4).Value = "Date";
        worksheet.Cell(1, 5).Value = "Time In";
        worksheet.Cell(1, 6).Value = "Time Out";
        worksheet.Cell(1, 7).Value = "Status";
        worksheet.Cell(1, 8).Value = "Working Hours";

        // Style the Header Row
        var headerRow = worksheet.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
        headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Populate Table Records
        for (int i = 0; i < logs.Count; i++)
        {
            var log = logs[i];
            int row = i + 2;

            worksheet.Cell(row, 1).Value = log.Employee?.EmployeeCode ?? "--";
            worksheet.Cell(row, 2).Value = log.Employee?.FullName ?? "--";
            worksheet.Cell(row, 3).Value = log.Employee?.Department ?? "--";
            worksheet.Cell(row, 4).Value = log.Date.ToString("yyyy-MM-dd");
            worksheet.Cell(row, 5).Value = log.TimeIn?.ToString(@"hh\:mm") ?? "--:--";
            worksheet.Cell(row, 6).Value = log.TimeOut?.ToString(@"hh\:mm") ?? "--:--";
            worksheet.Cell(row, 7).Value = log.Status;
            worksheet.Cell(row, 8).Value = log.WorkingHours?.ToString(@"hh\:mm") ?? "--:--";
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();

        return File(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Attendance_Report_{DateTime.Today:yyyyMMdd}.xlsx"
        );
    }
}