using Microsoft.EntityFrameworkCore;
using ChatbotApi.Data;
using ChatbotApi.Models;

namespace ChatbotApi.Services;

public class MessageRetryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MessageRetryBackgroundService> _logger;

    public MessageRetryBackgroundService(IServiceProvider services, ILogger<MessageRetryBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RetryFailedMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message retry worker error");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task RetryFailedMessagesAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppCloudService>();

        const int maxRetries = 3;
        var now = DateTime.UtcNow;

        var pending = await db.Messages
            .Include(m => m.Conversation)
            .ThenInclude(c => c.Contact)
            .Where(m =>
                m.Direction == MessageDirection.Outbound &&
                m.DeliveryStatus == MessageDeliveryStatus.Failed &&
                m.RetryCount < maxRetries &&
                m.NextRetryAt != null &&
                m.NextRetryAt <= now)
            .OrderBy(m => m.NextRetryAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            var tenant = await db.Tenants.FindAsync([message.Conversation.TenantId], ct);
            if (tenant == null) continue;

            var creds = tenant.ToCredentials();
            if (creds == null) continue;

            var result = await whatsApp.SendTextMessageAsync(
                creds, message.Conversation.Contact.PhoneNumber, message.Body, ct);

            message.RetryCount++;

            if (result.Success)
            {
                message.WhatsAppMessageId = result.WhatsAppMessageId;
                message.DeliveryStatus = MessageDeliveryStatus.Sent;
                message.ErrorMessage = null;
                message.NextRetryAt = null;
                message.StatusUpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Retried message {MessageId} successfully", message.Id);
            }
            else
            {
                message.ErrorMessage = result.Error?.Length > 1000 ? result.Error[..1000] : result.Error;
                message.StatusUpdatedAt = DateTime.UtcNow;

                if (message.RetryCount >= maxRetries)
                {
                    message.NextRetryAt = null;
                    _logger.LogWarning("Message {MessageId} exhausted retries", message.Id);
                }
                else
                {
                    var delayMinutes = Math.Pow(2, message.RetryCount);
                    message.NextRetryAt = DateTime.UtcNow.AddMinutes(delayMinutes);
                }
            }
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
