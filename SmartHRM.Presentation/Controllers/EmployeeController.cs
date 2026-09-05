using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartHRM.BusinessLogic;
using SmartHRM.BusinessLogic.DTOs;
using SmartHRM.DataAccess;
using SmartHRM.DataAccess.Entities;

namespace SmartHRM.Presentation.Controllers;

[Authorize]
public class EmployeeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IFaceService _faceService;
    private readonly IWebHostEnvironment _env;

    public EmployeeController(
        ApplicationDbContext context,
        IFaceService faceService,
        IWebHostEnvironment env)
    {
        _context = context;
        _faceService = faceService;
        _env = env;
    }

    // GET: /Employee/Index
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? department)
    {
        var query = _context.Employees.AsQueryable();

        // Search by Full Name or Employee Code
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e => e.FullName.Contains(search) || e.EmployeeCode.Contains(search));
        }

        // Filter by Department
        if (!string.IsNullOrWhiteSpace(department))
        {
            query = query.Where(e => e.Department == department);
        }

        var employees = await query
            .OrderByDescending(e => e.JoiningDate)
            .ToListAsync();

        ViewBag.SelectedDepartment = department;
        ViewBag.SearchTerm = search;

        return View(employees);
    }

    // GET: /Employee/Create
    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    // POST: /Employee/Create
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmployeeCreateDto dto)
    {
        if (dto == null)
        {
            return BadRequest(new { message = "Invalid employee registration data." });
        }

        if (dto.CapturedFaceImages == null || dto.CapturedFaceImages.Count == 0)
        {
            return BadRequest(new { message = "Please capture face images before submitting." });
        }

        // 1. Extract 512-d master embedding vector via Python AI service
        byte[] embeddingBytes;
        try
        {
            embeddingBytes = await _faceService.GetMasterEmbeddingAsync(dto.CapturedFaceImages);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"AI Service Error: {ex.Message}" });
        }

        // 2. Format employee identifier and profile picture
        string empCode = string.IsNullOrWhiteSpace(dto.EmployeeCode)
            ? $"EMP{Random.Shared.Next(10000, 99999)}"
            : dto.EmployeeCode.Trim();

        string savedPhotoPath = string.Empty;
        try
        {
            string webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            string uploadFolder = Path.Combine(webRootPath, "uploads", "employees");

            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
            }

            // Extract base64 image data from the first captured frame
            string base64Data = dto.CapturedFaceImages[0];
            if (base64Data.Contains(","))
            {
                base64Data = base64Data.Substring(base64Data.IndexOf(",") + 1);
            }

            byte[] imageBytes = Convert.FromBase64String(base64Data);
            string fileName = $"{empCode}_{Guid.NewGuid().ToString("N")[..8]}.jpg";
            string filePath = Path.Combine(uploadFolder, fileName);

            await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
            savedPhotoPath = $"/uploads/employees/{fileName}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Failed to save profile image: {ex.Message}");
        }

        // 3. Populate entity fields
        var employee = new Employee
        {
            EmployeeCode = empCode,
            FullName = dto.FullName ?? string.Empty,
            Email = dto.Email ?? string.Empty,
            Phone = dto.Phone ?? string.Empty,
            Department = string.IsNullOrWhiteSpace(dto.Department) ? "General" : dto.Department,
            Designation = string.IsNullOrWhiteSpace(dto.Designation) ? "Employee" : dto.Designation,
            DateOfBirth = dto.DateOfBirth == default ? new DateTime(1995, 1, 1) : dto.DateOfBirth,
            JoiningDate = dto.JoiningDate == default ? DateTime.UtcNow.Date : dto.JoiningDate,
            Gender = string.IsNullOrWhiteSpace(dto.Gender) ? "Unspecified" : dto.Gender,
            Address = dto.Address ?? string.Empty,
            PhotoPath = savedPhotoPath,
            IsActive = true
        };

        try
        {
            // 4. Persist Employee and FaceEmbedding to SQL Server
            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            var faceRecord = new FaceEmbedding
            {
                EmployeeId = employee.Id,
                EmbeddingVector = embeddingBytes,
                CreatedAt = DateTime.UtcNow
            };

            _context.FaceEmbeddings.Add(faceRecord);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                employeeId = employee.Id,
                employeeCode = employee.EmployeeCode,
                photoPath = savedPhotoPath
            });
        }
        catch (DbUpdateException ex)
        {
            var innerMessage = ex.InnerException?.Message ?? ex.Message;
            return StatusCode(500, new { message = $"Database Error: {innerMessage}" });
        }
    }
}