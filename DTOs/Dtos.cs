namespace ChatbotApi.DTOs;

// Auth
public record RegisterRequest(string TenantName, string Email, string Password, string WhatsAppPhoneNumber);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, int TenantId, string TenantName, string Email);

// QnA
public record QnAPairDto(int Id, string Question, string Answer, bool IsActive, DateTime CreatedAt);
public record CreateQnAPairRequest(string Question, string Answer);
public record UpdateQnAPairRequest(string Question, string Answer, bool IsActive);

// Tenant
public record TenantDto(int Id, string TenantName, string Email, string WhatsAppPhoneNumber,
    bool IsWhatsAppConnected, string ApiKey, DateTime CreatedAt);
public record UpdateTenantRequest(string TenantName, string? WhatsAppPhoneNumber);

// Webhook
public record IncomingWhatsAppMessage(
    string RecipientPhoneNumber,  // tenant's WhatsApp number
    string SenderPhoneNumber,     // user who sent message
    string MessageText,
    string MessageId
);
public record WebhookResponse(bool Success, string? Answer, string? Fallback);

// WhatsApp Connect
public record ConnectWhatsAppRequest(string TenantId, string CallbackUrl);
public record QrCodeResponse(string? QrCode, string Status, string? SessionId);

// Chat Preview
public record ChatPreviewRequest(string Question);
public record ChatPreviewResponse(string Answer, bool Matched);
