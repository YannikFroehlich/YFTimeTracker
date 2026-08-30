namespace YFTimeTracker.App.Services;

public interface ITrayService : IDisposable
{
    void Initialize(MainWindow mainWindow);
}
