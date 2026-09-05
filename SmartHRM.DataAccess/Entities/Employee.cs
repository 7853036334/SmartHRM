namespace SmartHRM.DataAccess.Entities;

public class Employee
{
    public int Id { get; set; }

    // Required business identifiers
    public string EmployeeCode { get; set; } = string.Empty; // e.g., EMP00123
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;

    // Optional fields (marked with ? to allow NULL in SQL Server)
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public string? Address { get; set; }
    public string? PhotoPath { get; set; }
    public string? FaceEmbeddingJson { get; set; }

    public DateTime? DateOfBirth { get; set; }
    public DateTime JoiningDate { get; set; } = DateTime.Today;
    public bool IsActive { get; set; } = true;

    // Navigation Properties
    public ICollection<FaceEmbedding> FaceEmbeddings { get; set; } = new List<FaceEmbedding>();
    public ICollection<AttendanceLog> AttendanceLogs { get; set; } = new List<AttendanceLog>();
}