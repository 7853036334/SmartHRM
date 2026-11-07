using Microsoft.AspNetCore.Http;

namespace Sample.Models
{
    public class ImportViewModel
    {
        public IFormFile ExcelFile { get; set; }
    }
}