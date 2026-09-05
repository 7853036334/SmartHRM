using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartHRM.DataAccess;
using SmartHRM.DataAccess.Entities;
using SmartHRM.Presentation.Hubs;
using System.Text.Json;

namespace SmartHRM.Presentation.Controllers
{
    public class LiveAttendanceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<AttendanceHub> _hubContext;

        public LiveAttendanceController(ApplicationDbContext context, IHubContext<AttendanceHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var logs = await _context.AttendanceLogs
                .Include(a => a.Employee)
                .Where(a => a.Date == today)
                .OrderByDescending(a => a.TimeIn)
                .ToListAsync();

            return View(logs);
        }

        // Endpoint consumed by Python to cache active face models
        [HttpGet]
        public async Task<IActionResult> GetKnownEmbeddings()
        {
            var employees = await _context.Employees
                .Where(e => e.IsActive)
                .ToListAsync();

            var payload = new Dictionary<string, object>();

            foreach (var emp in employees)
            {
                float[] parsedVector = Array.Empty<float>();

                // Handle both JSON string or raw binary vectors if available
                if (!string.IsNullOrEmpty(emp.FaceEmbeddingJson))
                {
                    try
                    {
                        parsedVector = JsonSerializer.Deserialize<float[]>(emp.FaceEmbeddingJson) ?? Array.Empty<float>();
                    }
                    catch
                    {
                        parsedVector = Array.Empty<float>();
                    }
                }

                payload[emp.Id.ToString()] = new
                {
                    fullName = emp.FullName,
                    employeeCode = emp.EmployeeCode,
                    department = emp.Department ?? "General",
                    photoPath = emp.PhotoPath ?? "",
                    vector = parsedVector
                };
            }

            return Ok(payload);
        }

        // DTO for Python attendance push
        public class DetectionPayload
        {
            public int EmployeeId { get; set; }
            public string CameraId { get; set; } = string.Empty;
            public double ConfidenceScore { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> ProcessDetection([FromBody] DetectionPayload payload)
        {
            if (payload == null || payload.EmployeeId <= 0)
                return BadRequest("Invalid payload.");

            var employee = await _context.Employees.FindAsync(payload.EmployeeId);
            if (employee == null)
                return NotFound("Employee not found.");

            var today = DateTime.Today;
            var existingLog = await _context.AttendanceLogs
                .FirstOrDefaultAsync(a => a.EmployeeId == employee.Id && a.Date == today);

            var now = DateTime.Now;

            if (existingLog == null)
            {
                // Create new log entry
                var newLog = new AttendanceLog
                {
                    EmployeeId = employee.Id,
                    Date = today,
                    TimeIn = now.TimeOfDay,
                    Status = "Present"
                };

                _context.AttendanceLogs.Add(newLog);
                await _context.SaveChangesAsync();
            }

            // Calculate present and absent counts for today
            var presentCount = await _context.AttendanceLogs
                .Where(a => a.Date == today && a.Status == "Present")
                .Select(a => a.EmployeeId)
                .Distinct()
                .CountAsync();

            var totalEmployees = await _context.Employees.CountAsync(e => e.IsActive);
            var absentCount = Math.Max(0, totalEmployees - presentCount);

            // Broadcast to Razor UI via SignalR
            await _hubContext.Clients.All.SendAsync("ReceiveAttendanceUpdate", new
            {
                fullName = employee.FullName,
                employeeId = employee.EmployeeCode,
                time = now.ToString("hh:mm:ss tt"),
                status = "Present",
                photoPath = employee.PhotoPath,
                presentCount = presentCount,
                absentCount = absentCount
            });

            return Ok(new { status = "success" });
        }
    }
}