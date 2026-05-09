using Microsoft.AspNetCore.Mvc;
using TaxFilingAPI.Models;
using TaxFilingAPI.Services;

namespace TaxFilingAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaskController : ControllerBase
    {
        private readonly ITaskService _taskService;
        private readonly ILogger<TaskController> _logger;

        public TaskController(ITaskService taskService, ILogger<TaskController> logger)
        {
            _taskService = taskService;
            _logger = logger;
        }

        /// <summary>
        /// Get all tax filing tasks
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<TaskItem>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllTasks()
        {
            var tasks = await _taskService.GetAllTasksAsync();
            return Ok(tasks);
        }

        /// <summary>
        /// Get a specific task by ID
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(TaskItem), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTaskById(int id)
        {
            var task = await _taskService.GetTaskByIdAsync(id);
            if (task == null)
                return NotFound(new { message = $"Task {id} not found" });

            return Ok(task);
        }

        /// <summary>
        /// Search tasks by status, assignee, or tax year
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(IEnumerable<TaskItem>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SearchTasks(
            [FromQuery] string? status,
            [FromQuery] string? assignedTo,
            [FromQuery] string? taxYear)
        {
            var tasks = await _taskService.SearchTasksAsync(status, assignedTo, taxYear);
            return Ok(tasks);
        }

        /// <summary>
        /// Create a new tax filing task
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(TaskItem), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateTask([FromBody] TaskItem task)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _taskService.CreateTaskAsync(task);
            return CreatedAtAction(
                nameof(GetTaskById),
                new { id = created.Id },
                created);
        }

        /// <summary>
        /// Move a task to a new status
        /// </summary>
        [HttpPatch("{id:int}/status")]
        [ProducesResponseType(typeof(TaskItem), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> MoveTaskStatus(
            int id,
            [FromBody] MoveStatusRequest request)
        {
            try
            {
                var task = await _taskService.MoveTaskStatusAsync(id, request.NewStatus);
                if (task == null)
                    return NotFound(new { message = $"Task {id} not found" });

                return Ok(task);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }

    /// <summary>
    /// Request model for status transition
    /// </summary>
    public class MoveStatusRequest
    {
        public string NewStatus { get; set; } = string.Empty;
    }
}