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

    [HttpGet("kafka-producer")]
    public async Task<IActionResult> GetEmails(int pageNumber = 1, int pageSize = 20)
    {
        var emails = await _emailService.GetEmails(pageNumber, pageSize);
        return Ok(emails);
    }

    [HttpGet("kafka-consumer")]
    public async Task<IActionResult> GetEmailsFromKafka(int pageNumber = 1, int pageSize = 20)
    {
        var emails = await _emailService.GetAllConsumers("", pageNumber, pageSize);
        return Ok(emails);
    }
    [HttpGet("folders")]
    public async Task<IActionResult> GetEmailFolders()
    {
        var emails = await _emailService.GetAllFoldersWithDetails();
        return Ok(emails);
    }

    [HttpGet("date-time")]
    public async Task<IActionResult> GetDateTime()
    {
        var data = $"{DateTimeOffset.Now} \n {DateTimeOffset.UtcNow}";
        var data1 = DateTimeOffset.Now;

        return Ok(new { DateTimeOffset.Now, DateTimeOffset.UtcNow });
    }
}
