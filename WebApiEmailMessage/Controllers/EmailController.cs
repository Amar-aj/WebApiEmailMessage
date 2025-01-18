using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebApiEmailMessage.Models;
using WebApiEmailMessage.Services;

namespace WebApiEmailMessage.Controllers;

[Route("api/[controller]")]
[ApiController]
public class EmailController(IEmailService _emailService) : ControllerBase
{
    //[HttpGet]
    //public async Task<ActionResult<IEnumerable<EmailMessage>>> GetEmailMessages()
    //{
    //    var emails = await _emailService.GetEmails();
    //    return Ok(emails);
    //}

    [HttpGet]
    public async Task<IActionResult> GetEmails(int pageNumber = 1, int pageSize = 20)
    {
        var emails = await _emailService.GetEmails(pageNumber, pageSize);
        return Ok(emails);
    }
    [HttpGet("folders")]
    public async Task<IActionResult> GetEmailFolders()
    {
        var emails = await _emailService.GetAllFoldersWithDetails();
        return Ok(emails);
    }
}
