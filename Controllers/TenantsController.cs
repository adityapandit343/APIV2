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
[Route("api/[controller]")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public TenantsController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private int CurrentTenantId =>
        int.Parse(User.FindFirstValue("tenantId") ?? "0");

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null) return NotFound();
        return Ok(ToDto(tenant));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateTenantRequest req)
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null) return NotFound();

        tenant.TenantName = req.TenantName;
        if (req.WhatsAppPhoneNumber != null)
            tenant.WhatsAppPhoneNumber = req.WhatsAppPhoneNumber;

        await _db.SaveChangesAsync();
        return Ok(ToDto(tenant));
    }

    [HttpPut("me/whatsapp-config")]
    public async Task<IActionResult> UpdateWhatsAppConfig(UpdateWhatsAppConfigRequest req)
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null) return NotFound();

        if (req.WhatsAppPhoneNumberId != null)
            tenant.WhatsAppPhoneNumberId = string.IsNullOrWhiteSpace(req.WhatsAppPhoneNumberId)
                ? null : req.WhatsAppPhoneNumberId.Trim();

        if (req.WhatsAppAccessToken != null && !string.IsNullOrWhiteSpace(req.WhatsAppAccessToken))
            tenant.WhatsAppAccessToken = req.WhatsAppAccessToken.Trim();

        if (req.WhatsAppBusinessAccountId != null)
            tenant.WhatsAppBusinessAccountId = string.IsNullOrWhiteSpace(req.WhatsAppBusinessAccountId)
                ? null : req.WhatsAppBusinessAccountId.Trim();

        if (req.FallbackMessage != null)
            tenant.FallbackMessage = req.FallbackMessage.Trim();

        if (req.HandoffMessage != null)
            tenant.HandoffMessage = req.HandoffMessage.Trim();

        await _db.SaveChangesAsync();
        return Ok(ToDto(tenant));
    }

    [HttpGet("me/whatsapp-setup")]
    public IActionResult GetWhatsAppSetupInfo()
    {
        var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            webhookUrl = $"{baseUrl}/api/webhooks/whatsapp",
            verifyTokenHint = "Set WhatsApp__VerifyToken in your environment variables",
            fields = new[] { "messages" }
        });
    }
    [HttpDelete("{id}/whatsapp-credentials")]
    public async Task<IActionResult> DisconnectWhatsApp(int id)
    {
        var tenant = await _db.Tenants.FindAsync(id);

        if (tenant == null)
        {
            return NotFound(new { message = "Tenant not found." });
        }

       
        tenant.WhatsAppPhoneNumberId = null;
        tenant.WhatsAppAccessToken = null;
        tenant.WhatsAppBusinessAccountId = null;
        tenant.OAuthState = null;
        tenant.WhatsAppConnectedAt = null;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "WhatsApp integration removed successfully.",
            isWhatsAppConfigured = tenant.IsWhatsAppConfigured
        });
    }

    [HttpPost("me/regenerate-key")]
    public async Task<IActionResult> RegenerateApiKey()
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null) return NotFound();
        tenant.ApiKey = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();
        return Ok(new { apiKey = tenant.ApiKey });
    }

    private static TenantDto ToDto(Tenant tenant) => new(
        tenant.Id, tenant.TenantName, tenant.Email, tenant.WhatsAppPhoneNumber,
        tenant.IsWhatsAppConfigured, tenant.ApiKey, tenant.CreatedAt,
        tenant.WhatsAppPhoneNumberId, tenant.WhatsAppBusinessAccountId,
        tenant.FallbackMessage, tenant.HandoffMessage,
        !string.IsNullOrWhiteSpace(tenant.WhatsAppAccessToken));
}
