using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChatbotApi.Data;
using ChatbotApi.Models;
using ChatbotApi.Services;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/webhooks/whatsapp")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMessagePipelineService _pipeline;
    private readonly IWhatsAppCloudService _whatsApp;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        AppDbContext db,
        IMessagePipelineService pipeline,
        IWhatsAppCloudService whatsApp,
        IConfiguration config,
        ILogger<WhatsAppWebhookController> logger)
    {
        _db = db;
        _pipeline = pipeline;
        _whatsApp = whatsApp;
        _config = config;
        _logger = logger;
    }

    /// <summary>Meta webhook verification handshake.</summary>
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expectedToken = _config["WhatsApp:VerifyToken"];
        if (mode == "subscribe" && token == expectedToken && !string.IsNullOrEmpty(challenge))
            return Content(challenge, "text/plain");

        _logger.LogWarning("Webhook verification failed. Mode={Mode}", mode);
        return Unauthorized();
    }

    /// <summary>Receives inbound messages and delivery status updates from Meta.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (!_whatsApp.VerifyWebhookSignature(rawBody, signature))
        {
            _logger.LogWarning("Invalid webhook signature");
            return Unauthorized();
        }

        var log = new WebhookEventLog
        {
            EventType = "whatsapp_webhook",
            RawPayload = rawBody.Length > 50000 ? rawBody[..50000] : rawBody
        };
        _db.WebhookEventLogs.Add(log);

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("object", out var obj) && obj.GetString() != "whatsapp_business_account")
            {
                log.ProcessedSuccessfully = true;
                await _db.SaveChangesAsync(ct);
                return Ok();
            }

            if (!root.TryGetProperty("entry", out var entries)) return Ok();

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes)) continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)) continue;

                    string? phoneNumberId = null;
                    if (value.TryGetProperty("metadata", out var metadata) &&
                        metadata.TryGetProperty("phone_number_id", out var pnid))
                        phoneNumberId = pnid.GetString();

                    Tenant? tenant = null;
                    if (!string.IsNullOrEmpty(phoneNumberId))
                    {
                        tenant = await _db.Tenants
                            .FirstOrDefaultAsync(t => t.WhatsAppPhoneNumberId == phoneNumberId, ct);
                        log.TenantId = tenant?.Id;
                    }

                    if (value.TryGetProperty("messages", out var messages))
                    {
                        foreach (var msg in messages.EnumerateArray())
                            await ProcessInboundMessageAsync(value, msg, tenant, ct);
                    }

                    if (value.TryGetProperty("statuses", out var statuses))
                    {
                        foreach (var status in statuses.EnumerateArray())
                        {
                            var msgId = status.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                            var statusVal = status.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
                            if (!string.IsNullOrEmpty(msgId) && !string.IsNullOrEmpty(statusVal))
                                await _pipeline.ProcessStatusUpdateAsync(msgId, statusVal, ct);
                        }
                    }
                }
            }

            log.ProcessedSuccessfully = true;
        }
        catch (Exception ex)
        {
            log.ProcessedSuccessfully = false;
            log.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Webhook processing failed");
        }

        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    private async Task ProcessInboundMessageAsync(
        JsonElement value, JsonElement msg, Tenant? tenant, CancellationToken ct)
    {
        if (tenant == null)
        {
            _logger.LogWarning("No tenant for inbound message");
            return;
        }

        var msgType = msg.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        if (msgType != "text") return;

        var messageId = msg.GetProperty("id").GetString()!;
        var from = msg.GetProperty("from").GetString()!;
        var text = msg.GetProperty("text").GetProperty("body").GetString()!;

        string? contactName = null;
        if (value.TryGetProperty("contacts", out var contacts))
        {
            foreach (var contact in contacts.EnumerateArray())
            {
                if (contact.TryGetProperty("wa_id", out var waId) &&
                    waId.GetString() == from &&
                    contact.TryGetProperty("profile", out var profile) &&
                    profile.TryGetProperty("name", out var nameEl))
                {
                    contactName = nameEl.GetString();
                    break;
                }
            }
        }

        await _pipeline.ProcessInboundMessageAsync(tenant, from, contactName, text, messageId, ct);
    }
}
