namespace YFTimeTracker.Core.Validation;

public sealed class YFTimeTrackerException : Exception
{
    public YFTimeTrackerException(string message)
        : base(message)
    {
    }

    public YFTimeTrackerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
