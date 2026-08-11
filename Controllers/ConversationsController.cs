using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChatbotApi.Data;
using ChatbotApi.DTOs;
using ChatbotApi.Models;
using ChatbotApi.Services;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/conversations")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMessagePipelineService _pipeline;

    public ConversationsController(AppDbContext db, IMessagePipelineService pipeline)
    {
        _db = db;
        _pipeline = pipeline;
    }

    private int CurrentTenantId =>
        int.Parse(User.FindFirstValue("tenantId") ?? "0");

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? filter,
        CancellationToken ct)
    {
        var query = _db.Conversations
            .Include(c => c.Contact)
            .Where(c => c.TenantId == CurrentTenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Contact.PhoneNumber.Contains(term) ||
                (c.Contact.DisplayName != null && c.Contact.DisplayName.ToLower().Contains(term)) ||
                (c.LastMessagePreview != null && c.LastMessagePreview.ToLower().Contains(term)));
        }

        query = filter?.ToLowerInvariant() switch
        {
            "open" => query.Where(c => c.Status == ConversationStatus.Open),
            "resolved" => query.Where(c => c.Status == ConversationStatus.Resolved),
            "unresolved" => query.Where(c => c.IsUnresolved),
            "bot" => query.Where(c => c.Mode == ConversationMode.Bot),
            "human" => query.Where(c => c.Mode == ConversationMode.Human),
            _ => query
        };

        var items = await query
            .OrderByDescending(c => c.LastMessageAt)
            .Take(100)
            .ToListAsync(ct);

        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .Include(c => c.Contact)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == CurrentTenantId, ct);

        if (conversation == null) return NotFound();
        return Ok(ToDto(conversation));
    }

    [HttpGet("{id:int}/messages")]
    public async Task<IActionResult> GetMessages(int id, CancellationToken ct)
    {
        var exists = await _db.Conversations
            .AnyAsync(c => c.Id == id && c.TenantId == CurrentTenantId, ct);
        if (!exists) return NotFound();

        var messages = await _db.Messages
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto(
                m.Id,
                (MessageDirectionDto)m.Direction,
                (MessageSenderTypeDto)m.SenderType,
                m.Body,
                (MessageDeliveryStatusDto)m.DeliveryStatus,
                m.CreatedAt))
            .ToListAsync(ct);

        return Ok(messages);
    }

    [HttpPost("{id:int}/messages")]
    public async Task<IActionResult> SendMessage(int id, SendAgentMessageRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { message = "Message body is required." });

        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == CurrentTenantId, ct);
        if (conversation == null) return NotFound();

        var tenant = await _db.Tenants.FindAsync([CurrentTenantId], ct);
        if (tenant == null) return NotFound();

        conversation.Mode = ConversationMode.Human;
        conversation.IsUnresolved = false;
        await _db.SaveChangesAsync(ct);

        var message = await _pipeline.SendOutboundAsync(
            conversation, tenant, req.Body.Trim(), MessageSenderType.Agent, ct);

        if (message == null)
            return BadRequest(new { message = "WhatsApp is not configured or send failed." });

        return Ok(new MessageDto(
            message.Id,
            (MessageDirectionDto)message.Direction,
            (MessageSenderTypeDto)message.SenderType,
            message.Body,
            (MessageDeliveryStatusDto)message.DeliveryStatus,
            message.CreatedAt));
    }

    [HttpPatch("{id:int}/mode")]
    public async Task<IActionResult> UpdateMode(int id, UpdateConversationModeRequest req, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == CurrentTenantId, ct);
        if (conversation == null) return NotFound();

        conversation.Mode = (ConversationMode)req.Mode;
        if (conversation.Mode == ConversationMode.Bot)
            conversation.IsUnresolved = false;
        else
            conversation.IsUnresolved = true;

        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var updated = await _db.Conversations.Include(c => c.Contact)
            .FirstAsync(c => c.Id == id, ct);
        return Ok(ToDto(updated));
    }

    [HttpPost("{id:int}/handoff")]
    public async Task<IActionResult> Handoff(int id, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == CurrentTenantId, ct);
        if (conversation == null) return NotFound();

        var tenant = await _db.Tenants.FindAsync([CurrentTenantId], ct);
        if (tenant == null) return NotFound();

        conversation.Mode = ConversationMode.Human;
        conversation.IsUnresolved = true;
        conversation.Status = ConversationStatus.Open;
        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _pipeline.SendOutboundAsync(
            conversation, tenant, tenant.HandoffMessage, MessageSenderType.Bot, ct);

        var updated = await _db.Conversations.Include(c => c.Contact)
            .FirstAsync(c => c.Id == id, ct);
        return Ok(ToDto(updated));
    }

    [HttpPost("{id:int}/resolve")]
    public async Task<IActionResult> Resolve(int id, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == CurrentTenantId, ct);
        if (conversation == null) return NotFound();

        conversation.Status = ConversationStatus.Resolved;
        conversation.IsUnresolved = false;
        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var updated = await _db.Conversations.Include(c => c.Contact)
            .FirstAsync(c => c.Id == id, ct);
        return Ok(ToDto(updated));
    }

    [HttpPatch("{id:int}/contact")]
    public async Task<IActionResult> UpdateContact(int id, UpdateContactRequest req, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .Include(c => c.Contact)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == CurrentTenantId, ct);
        if (conversation == null) return NotFound();

        if (req.DisplayName != null) conversation.Contact.DisplayName = req.DisplayName.Trim();
        if (req.Email != null) conversation.Contact.Email = req.Email.Trim();
        if (req.Notes != null) conversation.Contact.Notes = req.Notes.Trim();
        conversation.Contact.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new ContactDto(
            conversation.Contact.Id,
            conversation.Contact.PhoneNumber,
            conversation.Contact.DisplayName,
            conversation.Contact.Email,
            conversation.Contact.Notes));
    }

    private static ConversationDto ToDto(Conversation c) => new(
        c.Id, c.ContactId, c.Contact.PhoneNumber, c.Contact.DisplayName,
        c.Contact.Email, c.Contact.Notes,
        (ConversationModeDto)c.Mode, (ConversationStatusDto)c.Status, c.IsUnresolved,
        c.LastMessagePreview, c.LastMessageAt, c.CreatedAt);
}
