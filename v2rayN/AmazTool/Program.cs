namespace AmazTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            // If no arguments are provided, display usage guidelines and exit
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            // Log all arguments for debugging purposes
            foreach (var arg in args)
            {
                Console.WriteLine(arg);
            }

            // Parse command based on first argument
            switch (args[0].ToLowerInvariant())
            {
                case "rebootas":
                    // Handle application restart
                    HandleRebootAsync();
                    break;

                // V2pn: "install <package> <pid of the V2pn that asked>".
                case "install" when args.Length >= 2:
                    InstallFlow.Run(args[1], args.Length > 2 && int.TryParse(args[2], out var pid) ? pid : null);
                    break;

                case "help":
                case "--help":
                case "-h":
                case "/?":
                    // Display help information
                    ShowHelp();
                    break;

                default:
                    // Default behavior: handle as upgrade data
                    // Maintain backward compatibility with existing usage pattern
                    var argData = Uri.UnescapeDataString(string.Join(" ", args));
                    // V2pn: the staged install with rollback replaces v2rayN's unpack-in-place.
                    InstallFlow.Run(argData, null);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Global exception handling
            // V2pn: started without a console, so there is no key to wait for.
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Display help information and usage guidelines
    /// </summary>
    private static void ShowHelp()
    {
        Console.WriteLine(Resx.Resource.Guidelines);
        Console.WriteLine("Available commands:");
        Console.WriteLine("  rebootas             - Restart the application");
        Console.WriteLine("  help                 - Display this help information");
        Thread.Sleep(5000);
    }

    /// <summary>
    /// Handle application restart
    /// </summary>
    private static void HandleRebootAsync()
    {
        Console.WriteLine("Restarting application...");
        Thread.Sleep(1000);
        Utils.StartV2RayN();
    }

    /// <summary>
    /// Handle application upgrade with the provided data
    /// </summary>
    /// <param name="upgradeData">Data for the upgrade process</param>
    private static void HandleUpgrade(string upgradeData)
    {
        Console.WriteLine("Upgrading application...");
        UpgradeApp.Upgrade(upgradeData);
    }
}
