using System.ComponentModel.DataAnnotations;

namespace Sample.Models
{
    public class Forms
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public Employee Employee { get; set; } = null!;
    }
}