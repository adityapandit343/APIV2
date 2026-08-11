using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatbotApi.DTOs;

namespace ChatbotApi.Services;

public interface IMetaOAuthService
{
    Task<OAuthTokenResult> ExchangeEmbeddedSignupCodeAsync(string code, CancellationToken ct = default);
    Task<string> GetLongLivedTokenAsync(string shortLivedToken, CancellationToken ct = default);
    Task<bool> SubscribeToWebhookAsync(string phoneNumberId, string accessToken, string webhookUrl, string verifyToken, CancellationToken ct = default);
    Task<(string? WabaId, string? PhoneNumberId)> GetWabaAndPhoneDetailsAsync(string accessToken, CancellationToken ct = default);
}

public record OAuthTokenResult(string AccessToken, string TokenType, int ExpiresIn);

public class MetaOAuthService : IMetaOAuthService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MetaOAuthService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MetaOAuthService(HttpClient http, IConfiguration config, ILogger<MetaOAuthService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    private string AppId => _config["Meta:AppId"] ?? throw new InvalidOperationException("Meta:AppId not configured");
    private string AppSecret => _config["Meta:AppSecret"] ?? throw new InvalidOperationException("Meta:AppSecret not configured");
    private string GraphApiVersion => _config["Meta:GraphApiVersion"] ?? "v23.0";

    public async Task<OAuthTokenResult> ExchangeEmbeddedSignupCodeAsync(string code, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://graph.facebook.com/{GraphApiVersion}/oauth/access_token?" +
           $"client_id={AppId}&" +
           $"client_secret={AppSecret}&" +
           $"code={Uri.EscapeDataString(code)}&" +
           $"redirect_uri=";

            var response = await _http.GetAsync(url, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Embedded signup token exchange failed: {Status} {Content}", response.StatusCode, content);
                throw new InvalidOperationException($"Embedded signup token exchange failed: {content}");
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            return new OAuthTokenResult(
                root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("No access token"),
                root.GetProperty("token_type").GetString() ?? "bearer",
                root.GetProperty("expires_in").GetInt32()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging embedded signup code for token");
            throw;
        }
    }

    public async Task<string> GetLongLivedTokenAsync(string shortLivedToken, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://graph.facebook.com/{GraphApiVersion}/oauth/access_token?" +
                      $"grant_type=fb_exchange_token&" +
                      $"client_id={AppId}&" +
                      $"client_secret={AppSecret}&" +
                      $"fb_exchange_token={Uri.EscapeDataString(shortLivedToken)}";

            var response = await _http.GetAsync(url, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get long-lived token: {Status} {Content}", response.StatusCode, content);
                return shortLivedToken;
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            return root.GetProperty("access_token").GetString() ?? shortLivedToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting long-lived token, using short-lived");
            return shortLivedToken;
        }
    }

    /// <summary>
    /// Fallback: Automatically fetches WABA ID and Phone Number ID from Meta Graph API
    /// if frontend sends them as null.
    /// </summary>
    public async Task<(string? WabaId, string? PhoneNumberId)> GetWabaAndPhoneDetailsAsync(string accessToken, CancellationToken ct = default)
    {
        try
        {
            // 1. Get Shared WABA ID
            var wabaUrl = $"https://graph.facebook.com/{GraphApiVersion}/me/shared_whatsapp_business_accounts?access_token={accessToken}";
            var wabaRes = await _http.GetAsync(wabaUrl, ct);
            var wabaContent = await wabaRes.Content.ReadAsStringAsync(ct);

            string? wabaId = null;
            string? phoneId = null;

            if (wabaRes.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(wabaContent);
                if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.GetArrayLength() > 0)
                {
                    wabaId = dataArray[0].GetProperty("id").GetString();
                }
            }

            // 2. Fetch Phone Number ID using WABA ID
            if (!string.IsNullOrEmpty(wabaId))
            {
                var phoneUrl = $"https://graph.facebook.com/{GraphApiVersion}/{wabaId}/phone_numbers?access_token={accessToken}";
                var phoneRes = await _http.GetAsync(phoneUrl, ct);
                var phoneContent = await phoneRes.Content.ReadAsStringAsync(ct);

                if (phoneRes.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(phoneContent);
                    if (doc.RootElement.TryGetProperty("data", out var phoneArray) && phoneArray.GetArrayLength() > 0)
                    {
                        phoneId = phoneArray[0].GetProperty("id").GetString();
                    }
                }
            }

            return (wabaId, phoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching WABA or Phone details from Graph API");
            return (null, null);
        }
    }

    public async Task<bool> SubscribeToWebhookAsync(string phoneNumberId, string accessToken, string webhookUrl, string verifyToken, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                webhook_url = webhookUrl,
                verify_token = verifyToken,
                fields = new[] { "messages" }
            };

            var url = $"https://graph.facebook.com/{GraphApiVersion}/{phoneNumberId}/subscribed_apps";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(req, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook subscription failed: {Status} {Content}", response.StatusCode, content);
                return false;
            }

            _logger.LogInformation("Webhook subscription successful for phone number {PhoneNumberId}", phoneNumberId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to webhook");
            return false;
        }
    }
}