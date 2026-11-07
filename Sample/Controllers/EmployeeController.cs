using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sample.Data;
using Sample.Models;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Presentation;
using Azure.Messaging;
using Microsoft.Identity.Client.NativeInterop;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Sample.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly SampleContext _data;

        public EmployeeController(SampleContext data)
        {
            _data = data;
        }


        // =========================
        // EMPLOYEE INDEX
        // =========================

        public IActionResult Index(int pageNumber = 1, int pageSize = 5)
        { 
            try
            {
                if (pageNumber < 1)
                    pageNumber = 1;

                int totalEmployees = _data.Employees.Count();

                int totalPages =
                    (int)Math.Ceiling(
                        totalEmployees / (double)pageSize);

                ViewBag.CurrentPage = pageNumber;
                ViewBag.TotalPages = totalPages;

                ViewBag.TotalSum =
                    _data.Employees.Sum(e => e.Id);

                var employees = _data.Employees
                    .OrderBy(e => e.Id)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return View(employees);

            }
            catch
            {
                return BadRequest("The service you are requesting is not available.\n pleace contact with devlopment team");
            }
        }
        //-----------------------------
        //getbyalphabate search
        //--------------------------------
        public IActionResult GetByAlphabateName(string name) 
        {
            try 
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return BadRequest("Please provide a name to search for.");
                }

                var result = _data.Employees
                                  .Where(e => e.Name.Contains(name))
                                  .ToList();

                if (result.Count == 0)
                {
                    return BadRequest("The word you are searching for is not present here.");
                }

                return View(result);
            }
            catch 
            {
                return BadRequest("The service you are requesting is not available.");
            }
        }



        // =========================
        // IMPORT GET
        // =========================

        [HttpGet]
        public IActionResult Import()
        {
            return View();
        }


        // =========================
        // IMPORT POST
        // =========================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(
            ImportViewModel model)
        {

            // --------------------------------
            // 1. Check file
            // --------------------------------

            if (model.ExcelFile == null ||
                model.ExcelFile.Length == 0)
            {
                ViewBag.Message =
                    "Please select an Excel file.";

                return View(model);
            }


            // --------------------------------
            // 2. Check extension
            // --------------------------------

            string extension =
                Path.GetExtension(
                    model.ExcelFile.FileName)
                    .ToLowerInvariant();

            if (extension != ".xlsx")
            {
                ViewBag.Message =
                    "Only .xlsx Excel files are allowed.";

                return View(model);
            }


            // --------------------------------
            // 3. Check file size
            // --------------------------------

            if (model.ExcelFile.Length >
                5 * 1024 * 1024)
            {
                ViewBag.Message =
                    "File size cannot exceed 5 MB.";

                return View(model);
            }

            if(model.ExcelFile.Length < 1)
            {
                ViewBag.Message =
                    "File is empty.";
                return View(model);
            }   

      

            // --------------------------------
            // 4. Open Excel
            // --------------------------------

            using var stream =
                model.ExcelFile.OpenReadStream();

            using var workbook =
                new XLWorkbook(stream);


            // --------------------------------
            // 5. Get first worksheet
            // --------------------------------

            var worksheet =
                workbook.Worksheet(1);


            // --------------------------------
            // 6. Check headers
            // --------------------------------

            string nameHeader =
                worksheet.Cell(1, 1)
                .GetString()
                .Trim();

            string emailHeader =
                worksheet.Cell(1, 2)
                .GetString()
                .Trim();

            string phoneHeader =
                worksheet.Cell(1, 3)
                .GetString()
                .Trim();

            string departmentHeader =
                worksheet.Cell(1, 4)
                .GetString()
                .Trim();

            string salaryHeader =
                worksheet.Cell(1, 5)
                .GetString()
                .Trim();


            if (nameHeader != "Name" ||
                emailHeader != "Email" ||
                phoneHeader != "Phone" ||
                departmentHeader != "Department" ||
                salaryHeader != "Salary")
            {
                ViewBag.Message =
                    "Invalid Excel format. " +
                    "Required columns are: " +
                    "Name, Email, Phone, Department, Salary.";

                return View(model);
            }


            // --------------------------------
            // 7. Get last row
            // --------------------------------

            var lastRow =
                worksheet.LastRowUsed()
                          .RowNumber();


            int successCount = 0;

            int errorCount = 0;


            // --------------------------------
            // 8. Read each row
            // --------------------------------

            for (int row = 2;
                 row <= lastRow;
                 row++)
            {

                string name =
                    worksheet.Cell(row, 1)
                    .GetString()
                    .Trim();


                string email =
                    worksheet.Cell(row, 2)
                    .GetString()
                    .Trim();


                string phone =
                    worksheet.Cell(row, 3)
                    .GetString()
                    .Trim();


                string department =
                    worksheet.Cell(row, 4)
                    .GetString()
                    .Trim();


                string salaryText =
                    worksheet.Cell(row, 5)
                    .GetString()
                    .Trim();


                // =========================
                // VALIDATION
                // =========================


                // Name validation

                if (string.IsNullOrWhiteSpace(name))
                {
                    errorCount++;
                    continue;
                }


                // Email validation

                if (string.IsNullOrWhiteSpace(email) ||
                    !email.Contains("@"))
                {
                    errorCount++;
                    continue;
                }


                // Phone validation

                if (phone.Length != 10 ||
                    !phone.All(char.IsDigit))
                {
                    errorCount++;
                    continue;
                }


                // Department validation

                if (string.IsNullOrWhiteSpace(department))
                {
                    errorCount++;
                    continue;
                }


                // Salary validation

                if (!decimal.TryParse(
                    salaryText,
                    out decimal salary))
                {
                    errorCount++;
                    continue;
                }


                // =========================
                // DUPLICATE CHECK
                // =========================

                bool emailExists =
                    await _data.Employees
                    .AnyAsync(e =>
                        e.Email == email);


                if (emailExists)
                {
                    errorCount++;
                    continue;
                }


                // =========================
                // CREATE EMPLOYEE
                // =========================

                Employee employee =
                    new Employee
                    {
                        Name = name,

                        Email = email,

                        Phone = phone,

                        Department = department,

                        Salary = salary
                    };


                // =========================
                // ADD TO DATABASE
                // =========================

                _data.Employees.Add(employee);
                if(true)
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                }

            }


            // --------------------------------
            // 9. Save database
            // --------------------------------

            await _data.SaveChangesAsync();


            // --------------------------------
            // 10. Show result
            // --------------------------------

            ViewBag.Message =
                $"Import completed. " +
                $"Successful: {successCount}, " +
                $"Failed: {errorCount}.";


            return View(model);
        }
    }
}