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

    

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        string? department)
    {
        IQueryable<Employee> query = _context.Employees
            .AsNoTracking();

       
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(e =>
                e.FullName.Contains(search) ||
                e.EmployeeCode.Contains(search) ||
                e.Email.Contains(search));
        }

        
        if (!string.IsNullOrWhiteSpace(department))
        {
            department = department.Trim();

            query = query.Where(e =>
                e.Department == department);
        }

        var employees = await query
            .OrderByDescending(e => e.JoiningDate)
            .ToListAsync();
        ViewBag.SearchTerm = search;
        ViewBag.SelectedDepartment = department;

        return View(employees);
    }


    

    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }


    

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] EmployeeCreateDto dto)
    {
        if (dto == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "Invalid employee registration data."
            });
        }

        if (dto.CapturedFaceImages == null ||
            dto.CapturedFaceImages.Count == 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Please capture face images before submitting."
            });
        }


        
        byte[] embeddingBytes;

        try
        {
            embeddingBytes =
                await _faceService.GetMasterEmbeddingAsync(
                    dto.CapturedFaceImages);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                success = false,
                message = $"AI Service Error: {ex.Message}"
            });
        }


        

        string empCode;

        if (string.IsNullOrWhiteSpace(dto.EmployeeCode))
        {
            empCode =
                $"EMP{Random.Shared.Next(10000, 99999)}";
        }
        else
        {
            empCode =
                dto.EmployeeCode.Trim();
        }



        string savedPhotoPath = string.Empty;

        try
        {
            string webRootPath =
                _env.WebRootPath ??
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot");

            string uploadFolder =
                Path.Combine(
                    webRootPath,
                    "uploads",
                    "employees");

            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
            }

            string base64Data =
                dto.CapturedFaceImages[0];

            if (base64Data.Contains(","))
            {
                base64Data =
                    base64Data.Substring(
                        base64Data.IndexOf(",") + 1);
            }

            byte[] imageBytes =
                Convert.FromBase64String(base64Data);

            string fileName =
                $"{empCode}_{Guid.NewGuid():N}.jpg";

            string filePath =
                Path.Combine(
                    uploadFolder,
                    fileName);

            await System.IO.File.WriteAllBytesAsync(
                filePath,
                imageBytes);

            savedPhotoPath =
                $"/uploads/employees/{fileName}";
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[WARNING] Failed to save profile image: {ex.Message}");
        }


        

        var employee = new Employee
        {
            EmployeeCode = empCode,

            FullName =
                dto.FullName ?? string.Empty,

            Email =
                dto.Email ?? string.Empty,

            Phone =
                dto.Phone ?? string.Empty,

            Department =
                string.IsNullOrWhiteSpace(dto.Department)
                    ? "General"
                    : dto.Department,

            Designation =
                string.IsNullOrWhiteSpace(dto.Designation)
                    ? "Employee"
                    : dto.Designation,

            DateOfBirth =
                dto.DateOfBirth == default
                    ? new DateTime(1995, 1, 1)
                    : dto.DateOfBirth,

            JoiningDate =
                dto.JoiningDate == default
                    ? DateTime.UtcNow.Date
                    : dto.JoiningDate,

            Gender =
                string.IsNullOrWhiteSpace(dto.Gender)
                    ? "Unspecified"
                    : dto.Gender,

            Address =
                dto.Address ?? string.Empty,

            PhotoPath =
                savedPhotoPath,

            IsActive = true
        };


        

        try
        {
            _context.Employees.Add(employee);

            await _context.SaveChangesAsync();

            var faceRecord = new FaceEmbedding
            {
                EmployeeId = employee.Id,

                EmbeddingVector =
                    embeddingBytes,

                CreatedAt =
                    DateTime.UtcNow
            };

            _context.FaceEmbeddings.Add(faceRecord);

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] =
                $"Employee {employee.FullName} registered successfully!";

            return Ok(new
            {
                success = true
            });
        }
        catch (DbUpdateException ex)
        {
            var message =
                ex.InnerException?.Message ??
                ex.Message;

            return StatusCode(
                500,
                new
                {
                    success = false,
                    message =
                        $"Database Error: {message}"
                });
        }
    }


   

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var employee =
            await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            TempData["ErrorMessage"] =
                "Employee not found.";

            return RedirectToAction(
                nameof(Index));
        }

        return View(employee);
    }


   

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        Employee model)
    {
        
        if (id != model.Id)
        {
            return BadRequest();
        }

        
        if (!ModelState.IsValid)
        {
            return View(model);
        }

       
        var employee =
            await _context.Employees
                .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            TempData["ErrorMessage"] =
                "Employee not found.";

            return RedirectToAction(
                nameof(Index));
        }

        

        employee.FullName =
            model.FullName?.Trim() ?? string.Empty;

        employee.Email =
            model.Email?.Trim() ?? string.Empty;

        employee.Phone =
            model.Phone?.Trim();

        employee.Department =
            model.Department?.Trim() ?? string.Empty;

        employee.Designation =
            model.Designation?.Trim() ?? string.Empty;

        employee.Gender =
            model.Gender?.Trim();

        employee.Address =
            model.Address?.Trim();

        employee.DateOfBirth =
            model.DateOfBirth;

        employee.JoiningDate =
            model.JoiningDate;

        employee.IsActive =
            model.IsActive;


        try
        {
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] =
                $"Employee {employee.FullName} updated successfully.";

            return RedirectToAction(
                nameof(Index));
        }
        catch (DbUpdateException ex)
        {
            var message =
                ex.InnerException?.Message ??
                ex.Message;

            ModelState.AddModelError(
                string.Empty,
                $"Unable to save changes: {message}");

            return View(model);
        }
    }


    

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var employee =
            await _context.Employees
                .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            TempData["ErrorMessage"] =
                "Employee not found.";

            return RedirectToAction(
                nameof(Index));
        }

        employee.IsActive =
            !employee.IsActive;

        try
        {
            await _context.SaveChangesAsync();

            if (employee.IsActive)
            {
                TempData["SuccessMessage"] =
                    $"{employee.FullName} is now Active.";
            }
            else
            {
                TempData["SuccessMessage"] =
                    $"{employee.FullName} is now Inactive.";
            }
        }
        catch (DbUpdateException ex)
        {
            TempData["ErrorMessage"] =
                $"Could not update status: " +
                $"{ex.InnerException?.Message ?? ex.Message}";
        }

        return RedirectToAction(
            nameof(Index));
    }


  

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var employee =
            await _context.Employees
                .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            TempData["ErrorMessage"] =
                "Employee not found.";

            return RedirectToAction(
                nameof(Index));
        }

        try
        {
            

            if (!string.IsNullOrWhiteSpace(
                employee.PhotoPath))
            {
                string webRootPath =
                    _env.WebRootPath ??
                    Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot");

                string physicalPath =
                    Path.Combine(
                        webRootPath,
                        employee.PhotoPath
                            .TrimStart('/', '\\'));

                if (System.IO.File.Exists(
                    physicalPath))
                {
                    System.IO.File.Delete(
                        physicalPath);
                }
            }


           

            var embeddings =
                await _context.FaceEmbeddings
                    .Where(f =>
                        f.EmployeeId == id)
                    .ToListAsync();

            if (embeddings.Count > 0)
            {
                _context.FaceEmbeddings
                    .RemoveRange(embeddings);
            }


           
            _context.Employees.Remove(employee);

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] =
                $"Employee {employee.FullName} was deleted successfully.";
        }
        catch (DbUpdateException ex)
        {
            TempData["ErrorMessage"] =
                $"Could not delete employee: " +
                $"{ex.InnerException?.Message ?? ex.Message}";
        }

        return RedirectToAction(
            nameof(Index));
    }


}
