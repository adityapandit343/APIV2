using System.ComponentModel.DataAnnotations;

namespace ChatbotApi.Models;

public class Tenant
{
    public int Id { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string WhatsAppPhoneNumber { get; set; } = string.Empty; // E.164 format e.g. +1234567890
    public string ApiKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? WhatsAppSessionId { get; set; }
    public bool IsWhatsAppConnected { get; set; } = false;
    public string PasswordHash { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<QnAPair> QnAPairs { get; set; } = new List<QnAPair>();
}

public class QnAPair
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Tenant Tenant { get; set; } = null!;
}
