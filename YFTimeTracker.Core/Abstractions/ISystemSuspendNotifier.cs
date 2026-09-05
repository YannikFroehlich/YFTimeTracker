namespace YFTimeTracker.Core.Abstractions;

/// <summary>
/// Meldet, wenn das Betriebssystem in den Energiesparmodus wechselt oder daraus zurückkehrt.
/// Damit lässt sich eine laufende Session am tatsächlichen Zeitpunkt beenden, statt die Lücke
/// erst nachträglich aus den ausgefallenen Scans zu schätzen.
/// </summary>
public interface ISystemSuspendNotifier
{
    /// <summary>
    /// Wird ausgelöst, bevor das System aussetzt. Behandlungen sollten kurz sein, weil Windows
    /// nur begrenzt auf sie wartet.
    /// </summary>
    event EventHandler? Suspending;

    /// <summary>
    /// Wird ausgelöst, nachdem das System fortgesetzt wurde.
    /// </summary>
    event EventHandler? Resumed;
}
