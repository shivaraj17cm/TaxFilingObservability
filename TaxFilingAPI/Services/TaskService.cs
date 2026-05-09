using Microsoft.EntityFrameworkCore;
using TaxFilingAPI.Data;
using TaxFilingAPI.Models;

namespace TaxFilingAPI.Services
{
    public interface ITaskService
    {
        Task<IEnumerable<TaskItem>> GetAllTasksAsync();
        Task<TaskItem?> GetTaskByIdAsync(int id);
        Task<IEnumerable<TaskItem>> SearchTasksAsync(string? status, string? assignedTo, string? taxYear);
        Task<TaskItem> CreateTaskAsync(TaskItem task);
        Task<TaskItem?> MoveTaskStatusAsync(int id, string newStatus);
    }

    public class TaskService : ITaskService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<TaskService> _logger;

        // Valid status transitions - mirrors real tax filing workflow
        private readonly Dictionary<string, List<string>> _allowedTransitions = new()
        {
            { "Pending",    new List<string> { "InProgress", "Cancelled" } },
            { "InProgress", new List<string> { "UnderReview", "Cancelled" } },
            { "UnderReview",new List<string> { "Completed", "InProgress" } },
            { "Completed",  new List<string>() },
            { "Cancelled",  new List<string>() }
        };

        public TaskService(AppDbContext context, ILogger<TaskService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<TaskItem>> GetAllTasksAsync()
        {
            _logger.LogInformation("Fetching all tasks");
            return await _context.Tasks
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<TaskItem?> GetTaskByIdAsync(int id)
        {
            _logger.LogInformation("Fetching task {TaskId}", id);
            return await _context.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task<IEnumerable<TaskItem>> SearchTasksAsync(
            string? status,
            string? assignedTo,
            string? taxYear)
        {
            _logger.LogInformation(
                "Searching tasks - Status: {Status}, AssignedTo: {AssignedTo}, TaxYear: {TaxYear}",
                status, assignedTo, taxYear);

            var query = _context.Tasks.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(status))
                query = query.Where(t => t.Status == status);

            if (!string.IsNullOrEmpty(assignedTo))
                query = query.Where(t => t.AssignedTo == assignedTo);

            if (!string.IsNullOrEmpty(taxYear))
                query = query.Where(t => t.TaxYear == taxYear);

            return await query.ToListAsync();
        }

        public async Task<TaskItem> CreateTaskAsync(TaskItem task)
        {
            task.CreatedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            task.Status = "Pending";

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created task {TaskId}", task.Id);
            return task;
        }

        public async Task<TaskItem?> MoveTaskStatusAsync(int id, string newStatus)
        {
            var task = await _context.Tasks.FindAsync(id);

            if (task == null)
            {
                _logger.LogWarning("Task {TaskId} not found", id);
                return null;
            }

            // Enforce valid workflow transitions
            if (!_allowedTransitions.ContainsKey(task.Status) ||
                !_allowedTransitions[task.Status].Contains(newStatus))
            {
                _logger.LogWarning(
                    "Invalid transition for task {TaskId}: {From} -> {To}",
                    id, task.Status, newStatus);
                throw new InvalidOperationException(
                    $"Cannot move task from '{task.Status}' to '{newStatus}'");
            }

            task.Status = newStatus;
            task.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Task {TaskId} moved from {From} to {To}",
                id, task.Status, newStatus);

            return task;
        }
    }
}