using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using TaxFilingAPI.Models;

namespace TaxFilingAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<TaskItem> Tasks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TaskItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Status).HasDefaultValue("Pending");
                entity.Property(e => e.TaxYear).HasMaxLength(10);
                entity.Property(e => e.FilingType).HasMaxLength(50);
            });

            // Seed some initial data
            // Seed some initial data
            modelBuilder.Entity<TaskItem>().HasData(
                new TaskItem
                {
                    Id = 1,
                    Title = "File 1040 for John Smith",
                    Description = "Individual tax filing for FY2024",
                    Status = "Pending",
                    AssignedTo = "agent1@taxfiling.com",
                    TaxYear = "2024",
                    FilingType = "Individual",
                    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new TaskItem
                {
                    Id = 2,
                    Title = "File 1120 for Acme Corp",
                    Description = "Corporate tax filing for FY2024",
                    Status = "InProgress",
                    AssignedTo = "agent2@taxfiling.com",
                    TaxYear = "2024",
                    FilingType = "Corporate",
                    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        }
    }
}