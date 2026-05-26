using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChatbotApi.Data;
using ChatbotApi.DTOs;
using ChatbotApi.Models;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        if (await _db.Tenants.AnyAsync(t => t.Email == req.Email))
            return Conflict(new { message = "Email already registered." });

        if (await _db.Tenants.AnyAsync(t => t.WhatsAppPhoneNumber == req.WhatsAppPhoneNumber))
            return Conflict(new { message = "WhatsApp number already registered." });

        var tenant = new Tenant
        {
            TenantName = req.TenantName,
            Email = req.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            WhatsAppPhoneNumber = req.WhatsAppPhoneNumber,
            ApiKey = Guid.NewGuid().ToString("N")
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Registered successfully. Please log in." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Email == req.Email);
        if (tenant == null || !BCrypt.Net.BCrypt.Verify(req.Password, tenant.PasswordHash))
            return Unauthorized(new { message = "Invalid credentials." });

        var token = GenerateJwt(tenant);
        return Ok(new AuthResponse(token, tenant.Id, tenant.TenantName, tenant.Email));
    }

    private string GenerateJwt(Tenant tenant)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, tenant.Id.ToString()),
            new Claim(ClaimTypes.Email, tenant.Email),
            new Claim("tenantId", tenant.Id.ToString()),
            new Claim(ClaimTypes.Name, tenant.TenantName)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
