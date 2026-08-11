using System.ComponentModel.DataAnnotations;



namespace ChatbotApi.Models;



public class Tenant

{

    public int Id { get; set; }

    public string TenantName { get; set; } = string.Empty;

    public string WhatsAppPhoneNumber { get; set; } = string.Empty;

    public string ApiKey { get; set; } = Guid.NewGuid().ToString("N");

    public string PasswordHash { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;



    // Meta WhatsApp Cloud API (per tenant)

    public string? WhatsAppPhoneNumberId { get; set; }

    public string? WhatsAppAccessToken { get; set; }

    public string? WhatsAppBusinessAccountId { get; set; }

    public string FallbackMessage { get; set; } =

        "Sorry, I didn't understand that. Reply with your question or type *agent* to speak with a person.";

    public string HandoffMessage { get; set; } =

        "Connecting you with an agent. Someone will reply shortly.";

    public bool IsWhatsAppConfigured =>

        !string.IsNullOrWhiteSpace(WhatsAppPhoneNumberId) &&

        !string.IsNullOrWhiteSpace(WhatsAppAccessToken);

    // OAuth state management

    public string? OAuthState { get; set; }

    public DateTime? WhatsAppConnectedAt { get; set; }



    public ICollection<QnAPair> QnAPairs { get; set; } = new List<QnAPair>();

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

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



public class Contact

{

    public int Id { get; set; }

    public int TenantId { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Email { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

}



public enum ConversationMode { Bot = 0, Human = 1 }

public enum ConversationStatus { Open = 0, Resolved = 1 }



public class Conversation

{

    public int Id { get; set; }

    public int TenantId { get; set; }

    public int ContactId { get; set; }

    public ConversationMode Mode { get; set; } = ConversationMode.Bot;

    public ConversationStatus Status { get; set; } = ConversationStatus.Open;

    public bool IsUnresolved { get; set; }

    public string? LastMessagePreview { get; set; }

    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;

    public Contact Contact { get; set; } = null!;

    public ICollection<Message> Messages { get; set; } = new List<Message>();

}



public enum MessageDirection { Inbound = 0, Outbound = 1 }

public enum MessageDeliveryStatus { Pending = 0, Sent = 1, Delivered = 2, Read = 3, Failed = 4 }

public enum MessageSenderType { Customer = 0, Bot = 1, Agent = 2 }



public class Message

{

    public int Id { get; set; }

    public int ConversationId { get; set; }

    public MessageDirection Direction { get; set; }

    public MessageSenderType SenderType { get; set; }

    public string Body { get; set; } = string.Empty;

    public string? WhatsAppMessageId { get; set; }

    public MessageDeliveryStatus DeliveryStatus { get; set; } = MessageDeliveryStatus.Pending;

    public int RetryCount { get; set; }

    public DateTime? NextRetryAt { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StatusUpdatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;

}



public class WebhookEventLog

{

    public int Id { get; set; }

    public int? TenantId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string RawPayload { get; set; } = string.Empty;

    public bool ProcessedSuccessfully { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

}

