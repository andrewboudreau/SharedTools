using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ExampleWebModule.Controllers;

/// <summary>
/// Example controller that demonstrates automatic discovery through ApplicationParts.
/// </summary>
[ApiController]
[Route("api/example-module")]
public class ExampleModuleController : ControllerBase
{
    private readonly ILogger<ExampleModuleController> _logger;
    private readonly IExampleBlobStorage _blobStorage;

    public ExampleModuleController(
        ILogger<ExampleModuleController> logger,
        IExampleBlobStorage blobStorage)
    {
        _logger = logger;
        _blobStorage = blobStorage;
    }

    [HttpGet("test")]
    public IActionResult Test()
    {
        _logger.LogInformation("ExampleModuleController test endpoint called");
        return Ok(new
        {
            Message = "Hello from ExampleWebModule controller!",
            Module = "ExampleWebModule",
            Timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("blob-info")]
    public IActionResult GetBlobInfo()
    {
        try
        {
            // This is just an example - in real usage you'd have actual blob operations
            return Ok(new
            {
                Service = "Azure Blob Storage",
                Status = "Configured",
                Module = "ExampleWebModule"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accessing blob storage");
            return StatusCode(500, new { Error = "Failed to access blob storage" });
        }
    }
}