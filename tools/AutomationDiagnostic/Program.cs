namespace AutomationDiagnostic;

internal static class Program
{
    static int Main(string[] args)
    {
        string? password = null;
        string? exePath = null;
        string? username = null;
        string? siteName = null;
        string? searchDate = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "-p" or "--password" && i + 1 < args.Length)
                password = args[++i];
            if (args[i] is "-e" or "--exe" && i + 1 < args.Length)
                exePath = args[++i];
            if (args[i] is "-u" or "--username" && i + 1 < args.Length)
                username = args[++i];
            if (args[i] is "-s" or "--site" && i + 1 < args.Length)
                siteName = args[++i];
            if (args[i] is "-d" or "--date" && i + 1 < args.Length)
                searchDate = args[++i];
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

        if (string.IsNullOrEmpty(username))
        {
            Console.Write("Enter expected PCS Pro username (or Enter to skip): ");
            username = Console.ReadLine() ?? "";
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

        if (string.IsNullOrEmpty(siteName))
        {
            Console.Write("Enter site/club name (default: High Halstow CC): ");
            var input = Console.ReadLine();
            siteName = string.IsNullOrEmpty(input) ? "High Halstow CC" : input;
        }

        if (string.IsNullOrEmpty(searchDate))
        {
            var today = DateTime.Today.ToString("dd/MM/yyyy");
            Console.Write($"Enter search date dd/MM/yyyy (default: {today}): ");
            var input = Console.ReadLine();
            searchDate = string.IsNullOrEmpty(input) ? today : input;
        }

        using var runner = new DiagnosticRunner(password, username, exePath, siteName, searchDate);
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
              -u, --username <user>  Expected PCS Pro username (validates pre-filled value)
              -p, --password <pwd>   PCS Pro login password
              -s, --site <name>      Site/club name for match filter (default: High Halstow CC)
              -d, --date <dd/MM/yyyy> Search date for match filter (default: today)
              -h, --help             Show this help

            The tool kills any existing cricket.exe, launches a fresh instance,
            then attempts each automation step in sequence. When a step fails,
            it dumps the UI tree for that screen to the Desktop.

            Example:
              AutomationDiagnostic -e "C:\PcsPro\cricket.exe" -u "myuser" -p "mypass"
              AutomationDiagnostic -e "C:\PcsPro\cricket.exe" -p "mypass" -s "High Halstow CC" -d "20/04/2026"

            Tree dumps are saved to the Desktop as pcs-diag-*.txt
            """);
    }
}
