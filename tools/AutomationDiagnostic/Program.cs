namespace AutomationDiagnostic;

internal static class Program
{
    static int Main(string[] args)
    {
        string? password = null;
        string? exePath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "-p" or "--password" && i + 1 < args.Length)
                password = args[++i];
            if (args[i] is "-e" or "--exe" && i + 1 < args.Length)
                exePath = args[++i];
            if (args[i] is "-h" or "--help")
            {
                PrintUsage();
                return 0;
            }
        }

        if (string.IsNullOrEmpty(exePath))
        {
            Console.Write("Enter path to cricket.exe: ");
            exePath = Console.ReadLine();
            if (string.IsNullOrEmpty(exePath))
            {
                Console.Error.WriteLine("Executable path is required.");
                return 1;
            }
        }

        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Enter PCS Pro password: ");
            password = Console.ReadLine();
            if (string.IsNullOrEmpty(password))
            {
                Console.Error.WriteLine("Password is required.");
                return 1;
            }
        }

        using var runner = new DiagnosticRunner(password, exePath);
        runner.Run();
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            PCS Pro — Automation Diagnostic Runner

            Usage: AutomationDiagnostic [options]

            Options:
              -e, --exe <path>       Path to cricket.exe (required)
              -p, --password <pwd>   PCS Pro login password
              -h, --help             Show this help

            The tool kills any existing cricket.exe, launches a fresh instance,
            then attempts each automation step in sequence. When a step fails,
            it dumps the UI tree for that screen to the Desktop.

            Example:
              AutomationDiagnostic -e "C:\PcsPro\cricket.exe" -p mypassword

            Tree dumps are saved to the Desktop as pcs-diag-*.txt
            """);
    }
}
