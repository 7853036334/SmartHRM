using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmartHRM.DataAccess.Entities;

namespace SmartHRM.DataAccess;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<FaceEmbedding> FaceEmbeddings => Set<FaceEmbedding>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map FaceEmbedding bidirectional relationship
        modelBuilder.Entity<FaceEmbedding>(entity =>
        {
            entity.Property(f => f.EmbeddingVector)
                  .HasColumnType("varbinary(max)");

            entity.HasOne(f => f.Employee)
                  .WithMany(e => e.FaceEmbeddings) // Links directly to Employee.FaceEmbeddings
                  .HasForeignKey(f => f.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Map AttendanceLog bidirectional relationship
        modelBuilder.Entity<AttendanceLog>(entity =>
        {
            entity.HasOne(a => a.Employee)
                  .WithMany(e => e.AttendanceLogs) // Links directly to Employee.AttendanceLogs
                  .HasForeignKey(a => a.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}