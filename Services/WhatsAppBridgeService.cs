using ChatbotApi.DTOs;

namespace ChatbotApi.Services;

public interface IWhatsAppBridgeService
{
    Task<QrCodeResponse> InitiateConnectionAsync(int tenantId, string sessionId, string tenantPhoneNumber, CancellationToken ct = default);
    Task<bool> SendMessageAsync(string sessionId, string recipientPhone, string message, CancellationToken ct = default);
    Task<string> GetConnectionStatusAsync(string sessionId, CancellationToken ct = default);
    Task<bool> DisconnectAsync(string sessionId, CancellationToken ct = default);
}

public class WhatsAppBridgeService : IWhatsAppBridgeService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppBridgeService> _logger;

    public WhatsAppBridgeService(HttpClient http, IConfiguration config, ILogger<WhatsAppBridgeService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    private string NodeBaseUrl => _config["NodeService:BaseUrl"] ?? "http://localhost:3001";
    private string NodeApiKey => _config["NodeService:ApiKey"] ?? "";

    private void AddAuth() => _http.DefaultRequestHeaders.Remove("x-api-key");

    public async Task<QrCodeResponse> InitiateConnectionAsync(int tenantId, string sessionId, string tenantPhoneNumber, CancellationToken ct = default)
    {
        try
        {
            var dotnetBaseUrl = _config["App:BaseUrl"] ?? "http://localhost:5000";
            var payload = new
            {
                sessionId,
                tenantId,
                tenantPhoneNumber,
                callbackUrl = $"{dotnetBaseUrl}/api/webhooks/whatsapp/incoming",
                statusCallbackUrl = $"{dotnetBaseUrl}/api/webhooks/whatsapp/status"
            };

            var req = new HttpRequestMessage(HttpMethod.Post, $"{NodeBaseUrl}/sessions/start");
            req.Headers.Add("x-api-key", NodeApiKey);
            req.Content = JsonContent.Create(payload);

            var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Node service returned {StatusCode}", res.StatusCode);
                return new QrCodeResponse(null, "error", sessionId);
            }

            var result = await res.Content.ReadFromJsonAsync<NodeQrResponse>(cancellationToken: ct);
            return new QrCodeResponse(result?.QrCode, result?.Status ?? "pending", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate WhatsApp connection for tenant {TenantId}", tenantId);
            return new QrCodeResponse(null, "error", sessionId);
        }
    }

    public async Task<bool> SendMessageAsync(string sessionId, string recipientPhone, string message, CancellationToken ct = default)
    {
        try
        {
            var payload = new { sessionId, to = recipientPhone, message };
            var req = new HttpRequestMessage(HttpMethod.Post, $"{NodeBaseUrl}/messages/send");
            req.Headers.Add("x-api-key", NodeApiKey);
            req.Content = JsonContent.Create(payload);

            var res = await _http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp message via session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<string> GetConnectionStatusAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{NodeBaseUrl}/sessions/{sessionId}/status");
            req.Headers.Add("x-api-key", NodeApiKey);
            var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return "unknown";
            var result = await res.Content.ReadFromJsonAsync<NodeStatusResponse>(cancellationToken: ct);
            return result?.Status ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    public async Task<bool> DisconnectAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{NodeBaseUrl}/sessions/{sessionId}/disconnect");
            req.Headers.Add("x-api-key", NodeApiKey);
            var res = await _http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect session {SessionId}", sessionId);
            return false;
        }
    }

    private record NodeQrResponse(string? QrCode, string? Status);
    private record NodeStatusResponse(string? Status);
}
