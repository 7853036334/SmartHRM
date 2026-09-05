namespace SmartHRM.DataAccess.Entities;

public class DashboardViewModel
{
    public int TotalEmployees { get; set; }
    public int PresentToday { get; set; }
    public int AbsentToday { get; set; }
    public int LateArrivals { get; set; }

    // Computed attendance rate displayed on the KPI card
    public int PresentPercentage =>
        TotalEmployees > 0 ? (int)Math.Round((double)PresentToday / TotalEmployees * 100) : 0;

    // Chart Data Sources
    public Dictionary<string, int> DepartmentWiseCount { get; set; } = new();
    public List<int> WeeklyAttendanceTrends { get; set; } = new();

    // SignalR & Activity Feed
    public List<RecentActivityItem> RecentActivities { get; set; } = new();
}

public class RecentActivityItem
{
    public string FullName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TimeString { get; set; } = string.Empty;
    public string? PhotoPath { get; set; }
}