using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.ReadModel;

/// <summary>
/// The Messenger's tables. The host owns the connection, the provider and the migrations-history table, and enlists this
/// context in its transaction; the constructor takes only the options, as the host requires.
/// </summary>
public sealed class MessengerReadModelContext(DbContextOptions options) : ModuleDbContext(options)
{
    public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();
    public DbSet<KeyEpoch> KeyEpochs => Set<KeyEpoch>();
    public DbSet<EpochKey> EpochKeys => Set<EpochKey>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<HiddenMessage> HiddenMessages => Set<HiddenMessage>();
    public DbSet<ReadReceipt> ReadReceipts => Set<ReadReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // msg_*: an operator reading the schema can attribute every table, and nothing can collide with the core or
        // another module. Fluent and provider-agnostic: PostgreSQL in production, SQLite in tests.
        var direct = modelBuilder.Entity<DirectConversation>().ToTable("msg_direct");
        direct.HasKey(d => d.ConversationId);
        direct.HasIndex(d => new { d.TenantId, d.UserA, d.UserB }).IsUnique();

        var epoch = modelBuilder.Entity<KeyEpoch>().ToTable("msg_epochs");
        epoch.HasKey(e => new { e.ConversationId, e.Epoch });
        epoch.HasIndex(e => e.TenantId);

        var key = modelBuilder.Entity<EpochKey>().ToTable("msg_epoch_keys");
        key.HasKey(k => new { k.ConversationId, k.Epoch, k.UserId });
        key.Property(k => k.WrappedKey).IsRequired();
        key.HasIndex(k => new { k.TenantId, k.UserId });

        var message = modelBuilder.Entity<Message>().ToTable("msg_messages");
        message.HasKey(m => m.Id);
        message.Property(m => m.Ciphertext).IsRequired();
        message.Property(m => m.ConcurrencyToken).IsConcurrencyToken();
        message.HasIndex(m => new { m.ConversationId, m.SentAt });
        message.HasIndex(m => m.TenantId);

        var hidden = modelBuilder.Entity<HiddenMessage>().ToTable("msg_hidden");
        hidden.HasKey(h => new { h.MessageId, h.UserId });
        hidden.HasIndex(h => h.TenantId);

        var receipt = modelBuilder.Entity<ReadReceipt>().ToTable("msg_receipts");
        receipt.HasKey(r => new { r.ConversationId, r.UserId });
        receipt.HasIndex(r => r.TenantId);
    }
}

/// <summary>Design-time only, for generating migrations; never used at runtime.</summary>
public sealed class MessengerReadModelContextFactory : IDesignTimeDbContextFactory<MessengerReadModelContext>
{
    public MessengerReadModelContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MessengerReadModelContext>();
        builder.UseNpgsql("Host=localhost;Database=design-time-only",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_messenger"));
        return new MessengerReadModelContext(builder.Options);
    }
}
