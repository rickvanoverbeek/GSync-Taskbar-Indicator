namespace GSyncIndicator;

internal static class Program
{
    // Unique per-user mutex name so only one indicator runs at a time.
    private const string MutexName = "GSyncTaskbarIndicator_{6E2A9C7F-2C1B-4D0E-9C9A-4B0C3D5E6F70}";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
            return; // another instance is already running

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
