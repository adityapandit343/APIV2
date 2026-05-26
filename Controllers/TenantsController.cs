using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChatbotApi.Data;
using ChatbotApi.DTOs;
using ChatbotApi.Services;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppBridgeService _whatsApp;

    public TenantsController(AppDbContext db, IWhatsAppBridgeService whatsApp)
    {
        _db = db;
        _whatsApp = whatsApp;
    }

    private int CurrentTenantId =>
        int.Parse(User.FindFirstValue("tenantId") ?? "0");

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null) return NotFound();
        return Ok(new TenantDto(
            tenant.Id, tenant.TenantName, tenant.Email,
            tenant.WhatsAppPhoneNumber, tenant.IsWhatsAppConnected,
            tenant.ApiKey, tenant.CreatedAt));
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
        return Ok(new TenantDto(
            tenant.Id, tenant.TenantName, tenant.Email,
            tenant.WhatsAppPhoneNumber, tenant.IsWhatsAppConnected,
            tenant.ApiKey, tenant.CreatedAt));
    }

    // Regenerate API key
    [HttpPost("me/regenerate-key")]
    public async Task<IActionResult> RegenerateApiKey()
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null) return NotFound();
        tenant.ApiKey = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();
        return Ok(new { apiKey = tenant.ApiKey });
    }

    // Initiate WhatsApp connection — calls Node.js service
    [HttpPost("me/whatsapp/connect")]
    public async Task<IActionResult> ConnectWhatsApp()
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null) return NotFound();

        var sessionId = $"tenant_{tenant.Id}_{Guid.NewGuid():N}";
        tenant.WhatsAppSessionId = sessionId;
        tenant.IsWhatsAppConnected = false;
        await _db.SaveChangesAsync();

        var result = await _whatsApp.InitiateConnectionAsync(tenant.Id, sessionId, tenant.WhatsAppPhoneNumber);
        return Ok(result);
    }

    // Poll connection status
    [HttpGet("me/whatsapp/status")]
    public async Task<IActionResult> WhatsAppStatus()
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null) return NotFound();
        if (string.IsNullOrEmpty(tenant.WhatsAppSessionId))
            return Ok(new { status = "not_configured" });

        var status = await _whatsApp.GetConnectionStatusAsync(tenant.WhatsAppSessionId);

        if (status == "connected" && !tenant.IsWhatsAppConnected)
        {
            tenant.IsWhatsAppConnected = true;
            await _db.SaveChangesAsync();
        }

        return Ok(new { status, sessionId = tenant.WhatsAppSessionId });
    }

    // Disconnect WhatsApp
    [HttpPost("me/whatsapp/disconnect")]
    public async Task<IActionResult> DisconnectWhatsApp()
    {
        var tenant = await _db.Tenants.FindAsync(CurrentTenantId);
        if (tenant == null || string.IsNullOrEmpty(tenant.WhatsAppSessionId))
            return BadRequest(new { message = "Not connected." });

        await _whatsApp.DisconnectAsync(tenant.WhatsAppSessionId);
        tenant.IsWhatsAppConnected = false;
        tenant.WhatsAppSessionId = null;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Disconnected." });
    }
}
