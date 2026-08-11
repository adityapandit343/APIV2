using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChatbotApi.Data;
using ChatbotApi.Models;
using ChatbotApi.Services;
using ChatbotApi.DTOs;
using Microsoft.AspNetCore.Authorization;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/oauth")]
public class OAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMetaOAuthService _oauthService;
    private readonly IConfiguration _config;
    private readonly ILogger<OAuthController> _logger;

    public OAuthController(
        AppDbContext db,
        IMetaOAuthService oauthService,
        IConfiguration config,
        ILogger<OAuthController> logger)
    {
        _db = db;
        _oauthService = oauthService;
        _config = config;
        _logger = logger;
    }

    /// <summary>Handle Meta WhatsApp Embedded Signup - exchanges code for token and configures WhatsApp.</summary>
    [HttpPost("embedded-signup")]
    [Authorize]
    public async Task<ActionResult<EmbeddedSignupResponse>> EmbeddedSignup(
        [FromBody] EmbeddedSignupRequest request,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                _logger.LogWarning("Embedded signup: No code provided");
                return BadRequest(new EmbeddedSignupResponse(false, "Authorization code is required", null, null));
            }

            var tenantId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var tenant = await _db.Tenants.FindAsync(tenantId, ct);

            if (tenant == null)
            {
                _logger.LogWarning("Embedded signup: Tenant not found for ID {TenantId}", tenantId);
                return NotFound(new EmbeddedSignupResponse(false, "Tenant not found", null, null));
            }

            // 1. Exchange code for access token
            var tokenResult = await _oauthService.ExchangeEmbeddedSignupCodeAsync(request.Code, ct);

            // 2. Get long-lived token
            var longLivedToken = await _oauthService.GetLongLivedTokenAsync(tokenResult.AccessToken, ct);

            string? wabaId = request.WabaId;
            string? phoneNumberId = request.PhoneNumberId;

            // 3. Fallback: Fetch WABA ID & Phone Number ID if missing from request payload
            if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(phoneNumberId))
            {
                _logger.LogInformation("Embedded signup: Missing WABA or Phone ID in payload, fetching from Meta Graph API...");
                var (fetchedWaba, fetchedPhone) = await _oauthService.GetWabaAndPhoneDetailsAsync(longLivedToken, ct);

                wabaId ??= fetchedWaba;
                phoneNumberId ??= fetchedPhone;
            }

            if (string.IsNullOrWhiteSpace(wabaId))
            {
                return BadRequest(new EmbeddedSignupResponse(false, "Could not retrieve WhatsApp Business Account ID from Meta", null, null));
            }

            if (string.IsNullOrWhiteSpace(phoneNumberId))
            {
                return BadRequest(new EmbeddedSignupResponse(false, "Could not retrieve Phone Number ID from Meta", null, null));
            }

            // 4. Subscribe to webhook
            var webhookUrl = $"{_config["App:BaseUrl"]}/api/webhooks/whatsapp";
            var verifyToken = _config["WhatsApp:VerifyToken"];
            var webhookSubscribed = await _oauthService.SubscribeToWebhookAsync(
                phoneNumberId,
                longLivedToken,
                webhookUrl,
                verifyToken ?? "",
                ct);

            if (!webhookSubscribed)
            {
                _logger.LogWarning("Embedded signup: Webhook subscription failed for phone {PhoneId}", phoneNumberId);
            }

            // 5. Update tenant with OAuth data
            tenant.WhatsAppAccessToken = longLivedToken;
            tenant.WhatsAppPhoneNumberId = phoneNumberId;
            tenant.WhatsAppBusinessAccountId = wabaId;
            tenant.WhatsAppConnectedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Embedded signup successful for tenant {TenantId}, phone {PhoneId}, WABA {WabaId}",
                tenantId, phoneNumberId, wabaId);

            return Ok(new EmbeddedSignupResponse(
                true,
                "WhatsApp connected successfully",
                phoneNumberId,
                wabaId
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedded signup error");
            return StatusCode(500, new EmbeddedSignupResponse(false, $"Connection failed: {ex.Message}", null, null));
        }
    }

    /// <summary>Get OAuth connection status for current tenant.</summary>
    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<OAuthStatusResponse>> GetStatus(CancellationToken ct)
    {
        var tenantId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var tenant = await _db.Tenants.FindAsync(tenantId, ct);

        if (tenant == null)
            return NotFound("Tenant not found");

        return Ok(new OAuthStatusResponse(
            IsConnected: tenant.IsWhatsAppConfigured,
            ConnectedAt: tenant.WhatsAppConnectedAt?.ToString("o"),
            PhoneNumberId: tenant.WhatsAppPhoneNumberId,
            BusinessAccountId: tenant.WhatsAppBusinessAccountId
        ));
    }

    /// <summary>Disconnect WhatsApp for current tenant.</summary>
    [HttpPost("disconnect")]
    [Authorize]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var tenantId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var tenant = await _db.Tenants.FindAsync(tenantId, ct);

        if (tenant == null)
            return NotFound("Tenant not found");

        tenant.WhatsAppAccessToken = null;
        tenant.WhatsAppPhoneNumberId = null;
        tenant.WhatsAppBusinessAccountId = null;
        tenant.WhatsAppConnectedAt = null;
        tenant.OAuthState = null;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("WhatsApp disconnected for tenant {TenantId}", tenantId);

        return Ok(new { message = "WhatsApp disconnected successfully" });
    }
}