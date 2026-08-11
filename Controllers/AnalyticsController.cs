using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChatbotApi.Data;
using ChatbotApi.DTOs;
using ChatbotApi.Models;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db) => _db = db;

    private int CurrentTenantId =>
        int.Parse(User.FindFirstValue("tenantId") ?? "0");

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var weekStart = todayStart.AddDays(-7);
        var dayAgo = now.AddHours(-24);

        var tenantConversationIds = _db.Conversations
            .Where(c => c.TenantId == CurrentTenantId)
            .Select(c => c.Id);

        var messagesToday = await _db.Messages
            .CountAsync(m => tenantConversationIds.Contains(m.ConversationId) && m.CreatedAt >= todayStart, ct);

        var messagesThisWeek = await _db.Messages
            .CountAsync(m => tenantConversationIds.Contains(m.ConversationId) && m.CreatedAt >= weekStart, ct);

        var openConversations = await _db.Conversations
            .CountAsync(c => c.TenantId == CurrentTenantId && c.Status == ConversationStatus.Open, ct);

        var unresolvedConversations = await _db.Conversations
            .CountAsync(c => c.TenantId == CurrentTenantId && c.IsUnresolved, ct);

        var failedMessages = await _db.Messages
            .CountAsync(m =>
                tenantConversationIds.Contains(m.ConversationId) &&
                m.Direction == MessageDirection.Outbound &&
                m.DeliveryStatus == MessageDeliveryStatus.Failed &&
                m.CreatedAt >= dayAgo, ct);

        var totalConversations = await _db.Conversations
            .CountAsync(c => c.TenantId == CurrentTenantId, ct);

        return Ok(new AnalyticsOverviewDto(
            messagesToday,
            messagesThisWeek,
            openConversations,
            unresolvedConversations,
            failedMessages,
            totalConversations));
    }

    [HttpGet("webhook-logs")]
    public async Task<IActionResult> WebhookLogs([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var logs = await _db.WebhookEventLogs
            .Where(w => w.TenantId == null || w.TenantId == CurrentTenantId)
            .OrderByDescending(w => w.CreatedAt)
            .Take(limit)
            .Select(w => new WebhookLogDto(
                w.Id, w.EventType, w.ProcessedSuccessfully, w.ErrorMessage, w.CreatedAt))
            .ToListAsync(ct);

        return Ok(logs);
    }
}
