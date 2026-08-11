using Microsoft.EntityFrameworkCore;
using ChatbotApi.Models;

namespace ChatbotApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<QnAPair> QnAPairs => Set<QnAPair>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<WebhookEventLog> WebhookEventLogs => Set<WebhookEventLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.WhatsAppPhoneNumber).IsUnique();
            e.HasIndex(t => t.Email).IsUnique();
            e.HasIndex(t => t.ApiKey).IsUnique();
            e.HasIndex(t => t.WhatsAppPhoneNumberId);
            e.Property(t => t.TenantName).HasMaxLength(200).IsRequired();
            e.Property(t => t.Email).HasMaxLength(320).IsRequired();
            e.Property(t => t.WhatsAppPhoneNumber).HasMaxLength(20).IsRequired();
            e.Property(t => t.ApiKey).HasMaxLength(64).IsRequired();
            e.Property(t => t.WhatsAppPhoneNumberId).HasMaxLength(64);
            e.Property(t => t.WhatsAppAccessToken).HasMaxLength(512);
            e.Property(t => t.WhatsAppBusinessAccountId).HasMaxLength(64);
            e.Property(t => t.FallbackMessage).HasMaxLength(2000);
            e.Property(t => t.HandoffMessage).HasMaxLength(2000);
            e.Property(t => t.WhatsAppConnectedAt);
            e.Property(t => t.OAuthState).HasMaxLength(256);
        });

        modelBuilder.Entity<QnAPair>(e =>
        {
            e.HasKey(q => q.Id);
            e.HasOne(q => q.Tenant)
             .WithMany(t => t.QnAPairs)
             .HasForeignKey(q => q.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(q => q.Question).HasMaxLength(1000).IsRequired();
            e.Property(q => q.Answer).HasMaxLength(4000).IsRequired();
        });

        modelBuilder.Entity<Contact>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.TenantId, c.PhoneNumber }).IsUnique();
            e.HasOne(c => c.Tenant)
             .WithMany(t => t.Contacts)
             .HasForeignKey(c => c.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.PhoneNumber).HasMaxLength(20).IsRequired();
            e.Property(c => c.DisplayName).HasMaxLength(200);
            e.Property(c => c.Email).HasMaxLength(320);
            e.Property(c => c.Notes).HasMaxLength(2000);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.TenantId, c.Status, c.LastMessageAt });
            e.HasOne(c => c.Tenant)
             .WithMany(t => t.Conversations)
             .HasForeignKey(c => c.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Contact)
             .WithMany(ct => ct.Conversations)
             .HasForeignKey(c => c.ContactId)
             .OnDelete(DeleteBehavior.Restrict);
            e.Property(c => c.LastMessagePreview).HasMaxLength(500);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.WhatsAppMessageId);
            e.HasIndex(m => new { m.DeliveryStatus, m.NextRetryAt });
            e.HasOne(m => m.Conversation)
             .WithMany(c => c.Messages)
             .HasForeignKey(m => m.ConversationId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(m => m.Body).HasMaxLength(4000).IsRequired();
            e.Property(m => m.WhatsAppMessageId).HasMaxLength(128);
            e.Property(m => m.ErrorMessage).HasMaxLength(1000);
        });

        modelBuilder.Entity<WebhookEventLog>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasIndex(w => w.CreatedAt);
            e.Property(w => w.EventType).HasMaxLength(100).IsRequired();
            e.Property(w => w.ErrorMessage).HasMaxLength(2000);
        });
    }
}
