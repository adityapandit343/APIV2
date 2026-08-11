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

            if (string.IsNullOrWhiteSpace(request.WabaId))
            {
                _logger.LogWarning("Embedded signup: No WABA ID provided");
                return BadRequest(new EmbeddedSignupResponse(false, "WhatsApp Business Account ID is required", null, null));
            }

            if (string.IsNullOrWhiteSpace(request.PhoneNumberId))
            {
                _logger.LogWarning("Embedded signup: No phone number ID provided");
                return BadRequest(new EmbeddedSignupResponse(false, "Phone number ID is required", null, null));
            }

            var tenantId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var tenant = await _db.Tenants.FindAsync(tenantId, ct);
            
            if (tenant == null)
            {
                _logger.LogWarning("Embedded signup: Tenant not found for ID {TenantId}", tenantId);
                return NotFound(new EmbeddedSignupResponse(false, "Tenant not found", null, null));
            }

            // Exchange code for access token
            var tokenResult = await _oauthService.ExchangeEmbeddedSignupCodeAsync(request.Code, ct);
            
            // Get long-lived token
            var longLivedToken = await _oauthService.GetLongLivedTokenAsync(tokenResult.AccessToken, ct);

            // Subscribe to webhook
            var webhookUrl = $"{_config["App:BaseUrl"]}/api/webhooks/whatsapp";
            var verifyToken = _config["WhatsApp:VerifyToken"];
            var webhookSubscribed = await _oauthService.SubscribeToWebhookAsync(
                request.PhoneNumberId, 
                longLivedToken, 
                webhookUrl, 
                verifyToken ?? "", 
                ct);

            if (!webhookSubscribed)
            {
                _logger.LogWarning("Embedded signup: Webhook subscription failed for phone {PhoneId}", request.PhoneNumberId);
                // Continue anyway - webhook can be configured manually
            }

            // Update tenant with OAuth data
            tenant.WhatsAppAccessToken = longLivedToken;
            tenant.WhatsAppPhoneNumberId = request.PhoneNumberId;
            tenant.WhatsAppBusinessAccountId = request.WabaId;
            tenant.WhatsAppConnectedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Embedded signup successful for tenant {TenantId}, phone {PhoneId}, WABA {WabaId}", 
                tenantId, request.PhoneNumberId, request.WabaId);

            return Ok(new EmbeddedSignupResponse(
                true, 
                "WhatsApp connected successfully", 
                request.PhoneNumberId, 
                request.WabaId
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
