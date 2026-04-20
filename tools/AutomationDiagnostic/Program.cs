namespace AutomationDiagnostic;

internal static class Program
{
    static int Main(string[] args)
    {
        string? password = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "-p" or "--password" && i + 1 < args.Length)
                password = args[++i];
            if (args[i] is "-h" or "--help")
            {
                PrintUsage();
                return 0;
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

        using var runner = new DiagnosticRunner(password);
        runner.Run();
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            PCS Pro — Automation Diagnostic Runner

            Usage: AutomationDiagnostic [options]

            Options:
              -p, --password <pwd>   PCS Pro login password
              -h, --help             Show this help

            Workflow:
              1. Start PCS Pro on the garage PC
              2. Run this tool — it attempts each automation step in sequence
              3. When a step fails, it dumps the UI tree for that screen
              4. Report the tree dump back for element ID discovery
              5. Update KnownElements.cs with discovered values
              6. Run again — it will get further each time

            Tree dumps are saved to the Desktop as pcs-diag-*.txt
            """);
    }
}
