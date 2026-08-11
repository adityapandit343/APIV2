using Microsoft.EntityFrameworkCore;
using ChatbotApi.Data;
using ChatbotApi.Models;

namespace ChatbotApi.Services;

public interface IMessagePipelineService
{
    Task ProcessInboundMessageAsync(
        Tenant tenant,
        string senderPhone,
        string? senderName,
        string messageText,
        string whatsAppMessageId,
        CancellationToken ct = default);

    Task ProcessStatusUpdateAsync(string whatsAppMessageId, string status, CancellationToken ct = default);

    Task<Message?> SendOutboundAsync(
        Conversation conversation,
        Tenant tenant,
        string body,
        MessageSenderType senderType,
        CancellationToken ct = default);
}

public class MessagePipelineService : IMessagePipelineService
{
    private static readonly string[] HandoffKeywords =
        ["agent", "human", "support", "representative", "talk to someone", "speak to someone"];

    private readonly AppDbContext _db;
    private readonly IWhatsAppCloudService _whatsApp;
    private readonly IQuestionMatchingService _matcher;
    private readonly ILogger<MessagePipelineService> _logger;

    public MessagePipelineService(
        AppDbContext db,
        IWhatsAppCloudService whatsApp,
        IQuestionMatchingService matcher,
        ILogger<MessagePipelineService> logger)
    {
        _db = db;
        _whatsApp = whatsApp;
        _matcher = matcher;
        _logger = logger;
    }

    public async Task ProcessInboundMessageAsync(
        Tenant tenant,
        string senderPhone,
        string? senderName,
        string messageText,
        string whatsAppMessageId,
        CancellationToken ct = default)
    {
        var creds = tenant.ToCredentials();
        if (creds != null)
            await _whatsApp.MarkMessageAsReadAsync(creds, whatsAppMessageId, ct);

        var contact = await GetOrCreateContactAsync(tenant.Id, senderPhone, senderName, ct);
        var conversation = await GetOrCreateOpenConversationAsync(tenant.Id, contact.Id, ct);

        var inbound = new Message
        {
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            SenderType = MessageSenderType.Customer,
            Body = messageText,
            WhatsAppMessageId = whatsAppMessageId,
            DeliveryStatus = MessageDeliveryStatus.Delivered,
            StatusUpdatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(inbound);

        conversation.LastMessagePreview = Truncate(messageText, 500);
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;
        conversation.Status = ConversationStatus.Open;

        await _db.SaveChangesAsync(ct);

        if (conversation.Mode == ConversationMode.Human)
        {
            conversation.IsUnresolved = true;
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (IsHandoffRequest(messageText))
        {
            await SwitchToHumanAsync(conversation, tenant, ct);
            return;
        }

        var qnaPairs = await _db.QnAPairs
            .Where(q => q.TenantId == tenant.Id && q.IsActive)
            .ToListAsync(ct);

        var (match, _) = _matcher.FindBestMatch(messageText, qnaPairs);
        if (match != null)
        {
            await SendOutboundAsync(conversation, tenant, match.Answer, MessageSenderType.Bot, ct);
            return;
        }

        await SendOutboundAsync(conversation, tenant, tenant.FallbackMessage, MessageSenderType.Bot, ct);
    }

    public async Task ProcessStatusUpdateAsync(string whatsAppMessageId, string status, CancellationToken ct = default)
    {
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.WhatsAppMessageId == whatsAppMessageId, ct);

        if (message == null) return;

        message.DeliveryStatus = status.ToLowerInvariant() switch
        {
            "sent" => MessageDeliveryStatus.Sent,
            "delivered" => MessageDeliveryStatus.Delivered,
            "read" => MessageDeliveryStatus.Read,
            "failed" => MessageDeliveryStatus.Failed,
            _ => message.DeliveryStatus
        };
        message.StatusUpdatedAt = DateTime.UtcNow;

        if (message.DeliveryStatus == MessageDeliveryStatus.Failed)
        {
            message.RetryCount = 0;
            message.NextRetryAt = DateTime.UtcNow.AddMinutes(1);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<Message?> SendOutboundAsync(
        Conversation conversation,
        Tenant tenant,
        string body,
        MessageSenderType senderType,
        CancellationToken ct = default)
    {
        var creds = tenant.ToCredentials();
        if (creds == null)
        {
            _logger.LogWarning("Tenant {TenantId} WhatsApp not configured", tenant.Id);
            return null;
        }

        var contact = await _db.Contacts.FindAsync([conversation.ContactId], ct);
        if (contact == null) return null;

        var outbound = new Message
        {
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            SenderType = senderType,
            Body = body,
            DeliveryStatus = MessageDeliveryStatus.Pending
        };
        _db.Messages.Add(outbound);

        conversation.LastMessagePreview = Truncate(body, 500);
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        var result = await _whatsApp.SendTextMessageAsync(creds, contact.PhoneNumber, body, ct);

        if (result.Success)
        {
            outbound.WhatsAppMessageId = result.WhatsAppMessageId;
            outbound.DeliveryStatus = MessageDeliveryStatus.Sent;
            outbound.StatusUpdatedAt = DateTime.UtcNow;
        }
        else
        {
            outbound.DeliveryStatus = MessageDeliveryStatus.Failed;
            outbound.ErrorMessage = Truncate(result.Error ?? "Send failed", 1000);
            outbound.RetryCount = 0;
            outbound.NextRetryAt = DateTime.UtcNow.AddMinutes(1);
            outbound.StatusUpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return outbound;
    }

    private async Task SwitchToHumanAsync(Conversation conversation, Tenant tenant, CancellationToken ct)
    {
        conversation.Mode = ConversationMode.Human;
        conversation.IsUnresolved = true;
        conversation.Status = ConversationStatus.Open;
        await _db.SaveChangesAsync(ct);
        await SendOutboundAsync(conversation, tenant, tenant.HandoffMessage, MessageSenderType.Bot, ct);
    }

    private async Task<Contact> GetOrCreateContactAsync(int tenantId, string phone, string? name, CancellationToken ct)
    {
        var normalized = NormalizePhone(phone);
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.PhoneNumber == normalized, ct);

        if (contact != null)
        {
            if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(contact.DisplayName))
            {
                contact.DisplayName = name;
                contact.UpdatedAt = DateTime.UtcNow;
            }
            return contact;
        }

        contact = new Contact
        {
            TenantId = tenantId,
            PhoneNumber = normalized,
            DisplayName = name
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync(ct);
        return contact;
    }

    private async Task<Conversation> GetOrCreateOpenConversationAsync(int tenantId, int contactId, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .Where(c => c.TenantId == tenantId && c.ContactId == contactId && c.Status == ConversationStatus.Open)
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync(ct);

        if (conversation != null) return conversation;

        conversation = new Conversation
        {
            TenantId = tenantId,
            ContactId = contactId,
            Mode = ConversationMode.Bot,
            Status = ConversationStatus.Open
        };
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    private static bool IsHandoffRequest(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        return HandoffKeywords.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePhone(string phone) =>
        phone.Trim().TrimStart('+').Replace(" ", "").Replace("-", "");

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
