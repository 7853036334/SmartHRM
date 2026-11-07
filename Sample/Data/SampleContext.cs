using Microsoft.EntityFrameworkCore;
using Sample.Models;

namespace Sample.Data
{
    public class SampleContext : DbContext
    {
        public SampleContext(DbContextOptions<SampleContext> options)
            : base(options)
        {
        }
        public DbSet<Forms> Applications { get; set; }

        public DbSet<Employee> Employees { get; set; }

        
    }
}