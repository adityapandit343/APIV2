using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChatbotApi.Data;
using ChatbotApi.DTOs;
using ChatbotApi.Services;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/webhooks/whatsapp")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IQuestionMatchingService _matcher;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        AppDbContext db,
        IQuestionMatchingService matcher,
        IConfiguration config,
        ILogger<WhatsAppWebhookController> logger)
    {
        _db = db;
        _matcher = matcher;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Called by the Node.js service for every incoming WhatsApp message.
    /// Returns the answer to send back — does NOT call the Node service itself.
    /// The Node service is responsible for actually sending the reply.
    /// </summary>
    [HttpPost("incoming")]
    public async Task<IActionResult> Incoming([FromBody] IncomingWhatsAppMessage msg)
    {
        // Validate webhook secret to prevent unauthorized calls
        var secret = Request.Headers["x-webhook-secret"].FirstOrDefault();
        var expectedSecret = _config["NodeService:WebhookSecret"];
        if (!string.IsNullOrEmpty(expectedSecret) && secret != expectedSecret)
        {
            _logger.LogWarning("Webhook call with invalid secret from {IP}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(msg.RecipientPhoneNumber) || string.IsNullOrWhiteSpace(msg.MessageText))
            return BadRequest(new { message = "Missing required fields." });

        _logger.LogInformation("Incoming WA message to {Phone}: {Text}", msg.RecipientPhoneNumber, msg.MessageText);

        // 1. Lookup tenant by their WhatsApp number (the number the user messaged)
        var tenant = await _db.Tenants
            .Include(t => t.QnAPairs)
            .FirstOrDefaultAsync(t => t.WhatsAppPhoneNumber == msg.RecipientPhoneNumber);

        if (tenant == null)
        {
            _logger.LogWarning("No tenant found for WhatsApp number {Phone}", msg.RecipientPhoneNumber);
            return NotFound(new WebhookResponse(false, null, "Tenant not found."));
        }

        // 2. Match question
        var (match, score) = _matcher.FindBestMatch(msg.MessageText, tenant.QnAPairs);

        if (match != null)
        {
            _logger.LogInformation("Match found (score={Score}) for tenant {TenantId}", score, tenant.Id);
            return Ok(new WebhookResponse(true, match.Answer, null));
        }

        _logger.LogInformation("No match for tenant {TenantId}, returning fallback", tenant.Id);
        return Ok(new WebhookResponse(true, null, null));
    }

    /// <summary>
    /// Called by Node.js when WhatsApp session status changes (connected/disconnected).
    /// </summary>
    [HttpPost("status")]
    public async Task<IActionResult> StatusUpdate([FromBody] WhatsAppStatusUpdate update)
    {
        var secret = Request.Headers["x-webhook-secret"].FirstOrDefault();
        var expectedSecret = _config["NodeService:WebhookSecret"];
        if (!string.IsNullOrEmpty(expectedSecret) && secret != expectedSecret)
            return Unauthorized();

        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.WhatsAppSessionId == update.SessionId);

        if (tenant == null) return NotFound();

        tenant.IsWhatsAppConnected = update.Status == "connected";
        await _db.SaveChangesAsync();

        _logger.LogInformation("Tenant {TenantId} WhatsApp status: {Status}", tenant.Id, update.Status);
        return Ok();
    }
}

public record WhatsAppStatusUpdate(string SessionId, string Status);
