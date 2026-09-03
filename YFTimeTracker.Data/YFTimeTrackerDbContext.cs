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

    public DbSet<GameSession> GameSessions => Set<GameSession>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<NotificationLogEntry> NotificationLogEntries => Set<NotificationLogEntry>();

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
            entity.HasIndex(executable => executable.ExecutablePathKey).IsUnique();
            entity.HasIndex(executable => executable.GameId);
            entity.HasIndex(executable => executable.GameId)
                .IsUnique()
                .HasDatabaseName("IX_GameExecutables_GameId_Primary")
                .HasFilter("IsPrimary = 1");
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
            entity.HasIndex(session => session.GameId);
            entity.HasIndex(session => session.StartedAtUtc);
            entity.HasIndex(session => session.EndedAtUtc);
            entity.HasIndex(session => session.GameId)
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
        });

        modelBuilder.Entity<NotificationLogEntry>(entity =>
        {
            entity.ToTable("NotificationLogEntries");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Kind).IsRequired();
            entity.Property(notification => notification.Title).HasMaxLength(160).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(1024).IsRequired();
            entity.Property(notification => notification.CreatedAtUtc).HasConversion(DateTimeOffsetConverter).IsRequired();
            entity.HasIndex(notification => notification.CreatedAtUtc);
        });
    }
}
