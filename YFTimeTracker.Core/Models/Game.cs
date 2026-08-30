namespace YFTimeTracker.Core.Models;

using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;

public sealed class Game
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public GameSource Source { get; set; } = GameSource.Manual;

    public string? ExternalGameId { get; set; }

    public string? InstallDirectory { get; set; }

    public string? InstallDirectoryKey { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }

    [JsonIgnore]
    public string LegacyExecutablePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string LegacyExecutablePathKey { get; set; } = string.Empty;

    [JsonIgnore]
    public string LegacyExecutableName { get; set; } = string.Empty;

    public List<GameExecutable> Executables { get; set; } = [];

    [NotMapped, JsonIgnore]
    public string ExecutablePath
    {
        get => PrimaryExecutable?.ExecutablePath ?? string.Empty;
        set => EnsurePrimaryExecutable().ExecutablePath = value;
    }

    [NotMapped, JsonIgnore]
    public string ExecutablePathKey
    {
        get => PrimaryExecutable?.ExecutablePathKey ?? string.Empty;
        set => EnsurePrimaryExecutable().ExecutablePathKey = value;
    }

    [NotMapped, JsonIgnore]
    public string ExecutableName
    {
        get => PrimaryExecutable?.ExecutableName ?? string.Empty;
        set => EnsurePrimaryExecutable().ExecutableName = value;
    }

    [JsonIgnore]
    public GameExecutable? PrimaryExecutable => Executables.FirstOrDefault(executable => executable.IsPrimary)
        ?? Executables.FirstOrDefault();

    private GameExecutable EnsurePrimaryExecutable()
    {
        if (PrimaryExecutable is { } executable)
        {
            executable.IsPrimary = true;
            return executable;
        }

        executable = new GameExecutable { IsPrimary = true, AddedAtUtc = AddedAtUtc };
        Executables.Add(executable);
        return executable;
    }
}
