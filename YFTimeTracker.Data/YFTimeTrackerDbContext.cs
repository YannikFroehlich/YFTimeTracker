using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data;

public sealed class YFTimeTrackerDbContext(DbContextOptions<YFTimeTrackerDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTimeOffset, long> DateTimeOffsetConverter = new(
        value => value.ToUniversalTime().UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableDateTimeOffsetConverter = new(
        value => value.HasValue ? value.Value.ToUniversalTime().UtcTicks : null,
        value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

    public DbSet<Game> Games => Set<Game>();

    public DbSet<GameExecutable> GameExecutables => Set<GameExecutable>();

    public DbSet<GameTag> GameTags => Set<GameTag>();

    public DbSet<GameArtwork> GameArtworks => Set<GameArtwork>();

    public DbSet<GameSession> GameSessions => Set<GameSession>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<NotificationLogEntry> NotificationLogEntries => Set<NotificationLogEntry>();

    public DbSet<TrackingExclusionRule> TrackingExclusionRules => Set<TrackingExclusionRule>();

    public DbSet<SyncTombstone> SyncTombstones => Set<SyncTombstone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("Games");
            entity.HasKey(game => game.Id);
            entity.Property(game => game.Name).HasMaxLength(160).IsRequired();
            entity.Property(game => game.Source).IsRequired();
            entity.Property(game => game.ExternalGameId).HasMaxLength(260);
            entity.Property(game => game.InstallDirectory).HasMaxLength(1024);
            entity.Property(game => game.InstallDirectoryKey).HasMaxLength(1024);
            entity.Property(game => game.LegacyExecutablePath).HasColumnName("ExecutablePath").HasMaxLength(1024).IsRequired();
            entity.Property(game => game.LegacyExecutablePathKey).HasColumnName("ExecutablePathKey").HasMaxLength(1024).IsRequired();
            entity.Property(game => game.LegacyExecutableName).HasColumnName("ExecutableName").HasMaxLength(260).IsRequired();
            entity.Property(game => game.AddedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.Ignore(game => game.PrimaryExecutable);
            entity.Property(game => game.CloudId).HasMaxLength(36);
            entity.Property(game => game.CloudIdentity).HasMaxLength(512);
            entity.Property(game => game.SyncedHash).HasMaxLength(32);
            entity.HasIndex(game => game.Name);
            entity.HasIndex(game => new { game.Source, game.ExternalGameId })
                .IsUnique()
                .HasFilter("ExternalGameId IS NOT NULL");
            entity.HasIndex(game => game.LegacyExecutablePathKey)
                .IsUnique()
                .HasDatabaseName("IX_Games_ExecutablePathKey");
        });

        modelBuilder.Entity<GameExecutable>(entity =>
        {
            entity.ToTable("GameExecutables");
            entity.HasKey(executable => executable.Id);
            entity.Property(executable => executable.ExecutablePath).HasMaxLength(1024).IsRequired();
            entity.Property(executable => executable.ExecutablePathKey).HasMaxLength(1024).IsRequired();
            entity.Property(executable => executable.ExecutableName).HasMaxLength(260).IsRequired();
            entity.Property(executable => executable.AddedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.HasOne(executable => executable.Game)
                .WithMany(game => game.Executables)
                .HasForeignKey(executable => executable.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(executable => executable.CloudId).HasMaxLength(36);
            entity.Property(executable => executable.CloudIdentity).HasMaxLength(512);
            entity.Property(executable => executable.SyncedHash).HasMaxLength(32);
            entity.HasIndex(executable => executable.ExecutablePathKey).IsUnique();
            entity.HasIndex(executable => executable.GameId);
            entity.HasIndex(executable => executable.GameId, "IX_GameExecutables_GameId_Primary")
                .IsUnique()
                .HasDatabaseName("IX_GameExecutables_GameId_Primary")
                .HasFilter("IsPrimary = 1");
        });

        modelBuilder.Entity<GameTag>(entity =>
        {
            entity.ToTable("GameTags");
            entity.HasKey(tag => tag.Id);
            entity.Property(tag => tag.Tag).HasMaxLength(60).IsRequired();
            entity.HasOne(tag => tag.Game)
                .WithMany(game => game.Tags)
                .HasForeignKey(tag => tag.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(tag => tag.CloudId).HasMaxLength(36);
            entity.Property(tag => tag.CloudIdentity).HasMaxLength(512);
            entity.Property(tag => tag.SyncedHash).HasMaxLength(32);
            entity.HasIndex(tag => tag.GameId);
            entity.HasIndex(tag => new { tag.GameId, tag.Tag }).IsUnique();
        });

        modelBuilder.Entity<GameArtwork>(entity =>
        {
            entity.ToTable("GameArtworks");
            entity.HasKey(artwork => artwork.GameId);
            entity.Property(artwork => artwork.ContentType).HasMaxLength(40).IsRequired();
            entity.Property(artwork => artwork.FileExtension).HasMaxLength(8).IsRequired();
            entity.Property(artwork => artwork.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(artwork => artwork.ImageData).IsRequired();
            entity.Property(artwork => artwork.UpdatedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.Property(artwork => artwork.CloudId).HasMaxLength(36);
            entity.Property(artwork => artwork.CloudIdentity).HasMaxLength(512);
            entity.Property(artwork => artwork.SyncedHash).HasMaxLength(32);
            entity.HasOne(artwork => artwork.Game)
                .WithOne()
                .HasForeignKey<GameArtwork>(artwork => artwork.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameSession>(entity =>
        {
            entity.ToTable("GameSessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.StartedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.Property(session => session.LastSeenAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.Property(session => session.EndedAtUtc).HasConversion(NullableDateTimeOffsetConverter);
            entity.Property(session => session.DurationSeconds);
            entity.Property(session => session.BootSessionId).HasMaxLength(128).IsRequired();
            entity.HasOne(session => session.Game)
                .WithMany()
                .HasForeignKey(session => session.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(session => session.CloudId).HasMaxLength(36);
            entity.Property(session => session.CloudIdentity).HasMaxLength(512);
            entity.Property(session => session.SyncedHash).HasMaxLength(32);
            entity.HasIndex(session => session.GameId);
            entity.HasIndex(session => session.StartedAtUtc);
            entity.HasIndex(session => session.EndedAtUtc);
            entity.HasIndex(session => session.GameId, "IX_GameSessions_GameId_Open")
                .IsUnique()
                .HasDatabaseName("IX_GameSessions_GameId_Open")
                .HasFilter("EndedAtUtc IS NULL");
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(setting => setting.Key);
            entity.Property(setting => setting.Key).HasMaxLength(160);
            entity.Property(setting => setting.Value).HasMaxLength(2048).IsRequired();
            entity.Property(setting => setting.UpdatedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.Property(setting => setting.SyncedHash).HasMaxLength(32);
        });

        modelBuilder.Entity<NotificationLogEntry>(entity =>
        {
            entity.ToTable("NotificationLogEntries");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Kind).IsRequired();
            entity.Property(notification => notification.Title).HasMaxLength(160).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(1024).IsRequired();
            entity.Property(notification => notification.CreatedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.Property(notification => notification.ReferenceKey).HasMaxLength(160);
            entity.HasIndex(notification => notification.CreatedAtUtc);
            entity.HasIndex(notification => new { notification.Kind, notification.ReferenceKey })
                .HasFilter("ReferenceKey IS NOT NULL");
        });

        modelBuilder.Entity<TrackingExclusionRule>(entity =>
        {
            entity.ToTable("TrackingExclusionRules");
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Kind).IsRequired();
            entity.Property(rule => rule.Value).HasMaxLength(1024).IsRequired();
            entity.Property(rule => rule.ValueKey).HasMaxLength(1024).IsRequired();
            entity.Property(rule => rule.AddedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.Property(rule => rule.CloudId).HasMaxLength(36);
            entity.Property(rule => rule.CloudIdentity).HasMaxLength(512);
            entity.Property(rule => rule.SyncedHash).HasMaxLength(32);
            entity.HasIndex(rule => new { rule.Kind, rule.ValueKey }).IsUnique();
        });

        modelBuilder.Entity<SyncTombstone>(entity =>
        {
            entity.ToTable("SyncTombstones");
            entity.HasKey(tombstone => tombstone.Id);
            entity.Property(tombstone => tombstone.Kind).IsRequired();
            entity.Property(tombstone => tombstone.Identity).HasMaxLength(512).IsRequired();
            entity.Property(tombstone => tombstone.CloudId).HasMaxLength(36);
            entity.Property(tombstone => tombstone.DeletedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.HasIndex(tombstone => new { tombstone.Kind, tombstone.Identity }).IsUnique();
        });
    }
}
