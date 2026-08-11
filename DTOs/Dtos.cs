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
public record TenantDto(
    int Id, string TenantName, string Email, string WhatsAppPhoneNumber,
    bool IsWhatsAppConfigured, string ApiKey, DateTime CreatedAt,
    string? WhatsAppPhoneNumberId, string? WhatsAppBusinessAccountId,
    string FallbackMessage, string HandoffMessage, bool HasAccessToken);

public record UpdateTenantRequest(string TenantName, string? WhatsAppPhoneNumber);

public record UpdateWhatsAppConfigRequest(
    string? WhatsAppPhoneNumberId,
    string? WhatsAppAccessToken,
    string? WhatsAppBusinessAccountId,
    string? FallbackMessage,
    string? HandoffMessage);

// Chat Preview
public record ChatPreviewRequest(string Question);
public record ChatPreviewResponse(string? Answer, bool Matched);

// Conversations
public record ContactDto(int Id, string PhoneNumber, string? DisplayName, string? Email, string? Notes);
public record ConversationDto(
    int Id, int ContactId, string ContactPhone, string? ContactName,
    string? ContactEmail, string? ContactNotes,
    ConversationModeDto Mode, ConversationStatusDto Status, bool IsUnresolved,
    string? LastMessagePreview, DateTime LastMessageAt, DateTime CreatedAt);

public record MessageDto(
    int Id, MessageDirectionDto Direction, MessageSenderTypeDto SenderType,
    string Body, MessageDeliveryStatusDto DeliveryStatus, DateTime CreatedAt);

public record SendAgentMessageRequest(string Body);
public record UpdateConversationModeRequest(ConversationModeDto Mode);
public record UpdateContactRequest(string? DisplayName, string? Email, string? Notes);

public enum ConversationModeDto { Bot = 0, Human = 1 }
public enum ConversationStatusDto { Open = 0, Resolved = 1 }
public enum MessageDirectionDto { Inbound = 0, Outbound = 1 }
public enum MessageDeliveryStatusDto { Pending = 0, Sent = 1, Delivered = 2, Read = 3, Failed = 4 }
public enum MessageSenderTypeDto { Customer = 0, Bot = 1, Agent = 2 }

// Analytics
public record AnalyticsOverviewDto(
    int MessagesToday,
    int MessagesThisWeek,
    int OpenConversations,
    int UnresolvedConversations,
    int FailedMessagesLast24Hours,
    int TotalConversations);

// Webhook logs
public record WebhookLogDto(int Id, string EventType, bool ProcessedSuccessfully, string? ErrorMessage, DateTime CreatedAt);

// Meta OAuth
public record OAuthRedirectResponse(string AuthUrl);
public record OAuthCallbackRequest(string Code, string State);
public record OAuthStatusResponse(bool IsConnected, string? ConnectedAt, string? PhoneNumberId, string? BusinessAccountId);
public record EmbeddedSignupRequest(string Code, string? WabaId, string? PhoneNumberId);
public record EmbeddedSignupResponse(bool Success, string Message, string? PhoneNumberId, string? BusinessAccountId);
