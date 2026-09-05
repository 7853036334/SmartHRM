namespace SmartHRM.DataAccess.Entities;

public class AttendanceLog
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public TimeSpan? TimeIn { get; set; }
    public TimeSpan? TimeOut { get; set; }
    public string Status { get; set; } = "Present"; // Present, Late, Absent
    public TimeSpan? WorkingHours { get; set; }
    public string CameraId { get; set; } = "Camera 01 - Main Gate";
    public double ConfidenceScore { get; set; }

    public Employee Employee { get; set; } = null!;
}
