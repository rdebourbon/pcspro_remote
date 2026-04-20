using System.Runtime.InteropServices;

namespace AutomationDiagnostic;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    static int Main(string[] args)
    {
        SetProcessDPIAware();
        string? password = null;
        string? exePath = null;
        string? username = null;
        string? siteName = null;
        string? searchDate = null;
        string? outputDir = null;
        bool spinnerDiscovery = false;
        bool captureUiTree = false;

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
            if (args[i] is "-o" or "--output" && i + 1 < args.Length)
                outputDir = args[++i];
            if (args[i] is "--spinner-discovery")
                spinnerDiscovery = true;
            if (args[i] is "--capture-ui-tree")
                captureUiTree = true;
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

        username ??= "";
        siteName ??= "High Halstow CC";
        searchDate ??= DateTime.Today.ToString("dd/MM/yyyy");

        outputDir ??= Path.Combine(
            Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            "diagnosticlogs");

        using var runner = new DiagnosticRunner(password, username, exePath,
            siteName, searchDate, outputDir, captureUiTree);

        if (spinnerDiscovery)
            runner.RunSpinnerDiscovery();
        else
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
              -o, --output <dir>     Output directory for execution log (default: beside cricket.exe)
              --spinner-discovery    Run spinner discovery mode (dumps tree before/during/after filters)
              --capture-ui-tree      Capture happy-path UI tree dumps (off by default to reduce noise)
              -h, --help             Show this help

            The tool kills any existing cricket.exe, launches a fresh instance,
            then runs the full automation flow without pausing. All output and
            UI tree dumps are captured in a single execution log file.

            Example:
              AutomationDiagnostic -e "C:\PcsPro\cricket.exe" -u "myuser" -p "mypass"
              AutomationDiagnostic -e "C:\PcsPro\cricket.exe" -p "mypass" -s "High Halstow CC" -d "20/04/2026"
              AutomationDiagnostic -e "C:\PcsPro\cricket.exe" -p "mypass" -o "D:\logs"
            """);
    }
}
