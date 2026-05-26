using Microsoft.EntityFrameworkCore;
using ChatbotApi.Models;

namespace ChatbotApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<QnAPair> QnAPairs => Set<QnAPair>();
 

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.WhatsAppPhoneNumber).IsUnique();
            e.HasIndex(t => t.Email).IsUnique();
            e.HasIndex(t => t.ApiKey).IsUnique();
            e.Property(t => t.TenantName).HasMaxLength(200).IsRequired();
            e.Property(t => t.Email).HasMaxLength(320).IsRequired();
            e.Property(t => t.WhatsAppPhoneNumber).HasMaxLength(20).IsRequired();
            e.Property(t => t.ApiKey).HasMaxLength(64).IsRequired();
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
    }
}
