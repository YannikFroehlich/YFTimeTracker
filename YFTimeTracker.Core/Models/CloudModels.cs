namespace YFTimeTracker.Core.Models;

/// <summary>
/// Verbindungsdaten zum Supabase-Projekt. Der Publishable Key ist kein Geheimnis
/// im klassischen Sinn - er ist fuer Clients gedacht und wird durch Row Level
/// Security abgesichert -, gehoert laut AGENTS.md aber trotzdem nicht ins
/// Repository und kommt deshalb aus einer nicht eingecheckten Konfigurationsdatei.
/// </summary>
public sealed record CloudConnectionSettings(string ProjectUrl, string AnonKey)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(ProjectUrl) && !string.IsNullOrWhiteSpace(AnonKey);

    /// <summary>Basis-URL ohne abschliessenden Schraegstrich.</summary>
    public string NormalizedUrl => ProjectUrl.TrimEnd('/');
}

/// <summary>
/// Angemeldete Sitzung. Der Access-Token lebt nur im Arbeitsspeicher; dauerhaft
/// gespeichert wird ausschliesslich der Refresh-Token, und zwar im Windows-
/// Anmeldeinformationsspeicher, nie in der SQLite-Datenbank.
/// </summary>
public sealed record CloudSession(
    string UserId,
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc)
{
    /// <summary>
    /// Eine Minute Sicherheitsabstand: ein Token, das waehrend des Abgleichs
    /// ablaeuft, wuerde den Lauf mitten in den Batches abbrechen.
    /// </summary>
    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc.AddMinutes(-1);
}

public enum CloudAuthStatus
{
    Success,
    InvalidCredentials,
    EmailConfirmationRequired,
    NotConfigured,
    NetworkError,
    UnexpectedError
}

public sealed record CloudAuthResult(CloudAuthStatus Status, string Message, CloudSession? Session = null)
{
    public bool IsSuccess => Status == CloudAuthStatus.Success;

    public static CloudAuthResult Success(CloudSession session) =>
        new(CloudAuthStatus.Success, "Angemeldet.", session);
}

/// <summary>Fortschrittsmeldung fuer die Oberflaeche.</summary>
public sealed record CloudProgress(string Step, int Completed, int Total)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1);
}
