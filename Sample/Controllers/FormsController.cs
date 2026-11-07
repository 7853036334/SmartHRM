using System.Linq;
using DocumentFormat.OpenXml.Office2010.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sample.Data;
using Sample.Models;

namespace Sample.Controllers
{
    public class FormsController : Controller
    {
        private readonly SampleContext _data;

        public FormsController(SampleContext data)
        {
            _data = data;
        }

        // GET: Forms
        public IActionResult Index()
        {
            try
            {
               var applications = _data.Applications
              .Include(a => a.Employee)
              .ToList();
                if (applications == null)
                {
                    return BadRequest();
                }
                return View(applications);
            }
            catch
            {
                return BadRequest("the service you are requesting is not available");
            }
          
        }

        // GET: Forms/Update/5
        [HttpGet]
        public IActionResult Update(int id)
        {
            var application = _data.Applications.Find(id);
            if (application == null)
            {
                return NotFound();
            }

            return View(application);
        }

        // POST: Forms/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Update(Forms form)
        {
            // Enforce 1-to-1 relationship: ensure Employee is not assigned to another Form
            bool employeeAssigned = _data.Applications
                .Any(a => a.EmployeeId == form.EmployeeId && a.Id != form.Id);

            if (employeeAssigned)
            {
                ModelState.AddModelError("EmployeeId", "This Employee is already linked to another application.");
            }

            if (!ModelState.IsValid)
            {
                return View(form);
            }

            var existingRecord = _data.Applications.Find(form.Id);
            if (existingRecord == null)
            {
                return NotFound();
            }

            existingRecord.EmployeeId = form.EmployeeId;
            _data.SaveChanges();

            return RedirectToAction(nameof(Index));
        }
        public IActionResult Create(Forms form)
        {
            if(form == null || !ModelState.IsValid)
            {
                return NotFound();
            }
            var employee= new Forms
            {
                EmployeeId = form.EmployeeId,
            };
            _data.Applications.Add(employee);
            _data.SaveChanges();
            return View();
        }

        // GET: Forms/Delete/5
        [HttpGet]
        public IActionResult Delete(int id)
        {
            var application = _data.Applications
                .Include(a => a.Employee)
                .FirstOrDefault(a => a.Id == id);

            if (application == null)
            {
                return NotFound();
            }

            return View(application);
        }

        //----------------
        //Search by id 
        //----------------
        public IActionResult GetByID(int Id)
        {
            var result = _data.Applications.Where(e => e.Id== Id);
            if(result==null)
            {
                return NotFound($"the {Id} your are not present yet");
            }
            return View(result);
        }

 

        // POST: Forms/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            var application = _data.Applications.Find(id);
            if (application == null)
            {
                return NotFound();
            }

            _data.Applications.Remove(application);
            _data.SaveChanges();

            return RedirectToAction(nameof(Index));
        }
    }
}