namespace SmartHRM.BusinessLogic.DTOs;

public class EmployeeCreateDto
{
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public DateTime JoiningDate { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? PhotoPath { get; set; } // Add this line
    public List<string> CapturedFaceImages { get; set; } = new();
}