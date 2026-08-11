using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatbotApi.Services;

public interface IWhatsAppCloudService
{
    bool VerifyWebhookSignature(string rawBody, string? signatureHeader);
    Task<WhatsAppSendResult> SendTextMessageAsync(TenantCredentials creds, string toPhone, string text, CancellationToken ct = default);
    Task<bool> MarkMessageAsReadAsync(TenantCredentials creds, string whatsAppMessageId, CancellationToken ct = default);
}

public record TenantCredentials(string PhoneNumberId, string AccessToken);

public record WhatsAppSendResult(bool Success, string? WhatsAppMessageId, string? Error);

public class WhatsAppCloudService : IWhatsAppCloudService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppCloudService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WhatsAppCloudService(HttpClient http, IConfiguration config, ILogger<WhatsAppCloudService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    private string GraphApiVersion => _config["WhatsApp:GraphApiVersion"] ?? "v23.0";
    private string AppSecret => _config["WhatsApp:AppSecret"] ?? "";

    public bool VerifyWebhookSignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(AppSecret))
        {
            _logger.LogWarning("WhatsApp AppSecret not configured — skipping signature verification.");
            return true;
        }

        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedHex = signatureHeader["sha256=".Length..];
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var computedHex = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(expectedHex.ToLowerInvariant()));
    }

    public async Task<WhatsAppSendResult> SendTextMessageAsync(
        TenantCredentials creds, string toPhone, string text, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = NormalizePhone(toPhone),
                type = "text",
                text = new { preview_url = false, body = text }
            };

            var url = $"https://graph.facebook.com/{GraphApiVersion}/{creds.PhoneNumberId}/messages";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", creds.AccessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("WhatsApp send failed: {Status} {Body}", res.StatusCode, body);
                return new WhatsAppSendResult(false, null, body);
            }

            using var doc = JsonDocument.Parse(body);
            var messageId = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();

            return new WhatsAppSendResult(true, messageId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp send exception");
            return new WhatsAppSendResult(false, null, ex.Message);
        }
    }

    public async Task<bool> MarkMessageAsReadAsync(
        TenantCredentials creds, string whatsAppMessageId, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                status = "read",
                message_id = whatsAppMessageId
            };

            var url = $"https://graph.facebook.com/{GraphApiVersion}/{creds.PhoneNumberId}/messages";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", creds.AccessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark message as read {MessageId}", whatsAppMessageId);
            return false;
        }
    }

    private static string NormalizePhone(string phone) =>
        phone.TrimStart('+').Replace(" ", "").Replace("-", "");
}

public static class TenantWhatsAppExtensions
{
    public static TenantCredentials? ToCredentials(this Models.Tenant tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant.WhatsAppPhoneNumberId) ||
            string.IsNullOrWhiteSpace(tenant.WhatsAppAccessToken))
            return null;

        return new TenantCredentials(tenant.WhatsAppPhoneNumberId, tenant.WhatsAppAccessToken);
    }
}
