using System.Diagnostics;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace UiTreeDumper;

internal static class Program
{
    private const string DefaultProcessName = "cricket";
    private const int DefaultMaxDepth = 6;

    static int Main(string[] args)
    {
        var processName = DefaultProcessName;
        var maxDepth = DefaultMaxDepth;
        string? outputFile = null;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-p" or "--process":
                    if (i + 1 < args.Length) processName = args[++i];
                    break;
                case "-d" or "--depth":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var d)) maxDepth = d;
                    break;
                case "-o" or "--output":
                    if (i + 1 < args.Length) outputFile = args[++i];
                    break;
                case "-h" or "--help":
                    showHelp = true;
                    break;
            }
        }

        if (showHelp)
        {
            PrintUsage();
            return 0;
        }

        Console.WriteLine($"Looking for process: {processName}");
        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            Console.Error.WriteLine($"ERROR: No process named '{processName}' found.");
            Console.Error.WriteLine("Make sure PCS Pro is running, then try again.");
            Console.Error.WriteLine($"Tip: use -p <name> to specify a different process name (without .exe).");
            return 1;
        }

        Console.WriteLine($"Found {processes.Length} process(es). Using PID {processes[0].Id} ({processes[0].MainWindowTitle})");

        using var automation = new UIA3Automation();
        var app = FlaUI.Core.Application.Attach(processes[0]);
        var windows = app.GetAllTopLevelWindows(automation);

        Console.WriteLine($"Found {windows.Length} top-level window(s).");
        Console.WriteLine(new string('=', 100));

        var sb = new StringBuilder();

        for (int w = 0; w < windows.Length; w++)
        {
            sb.AppendLine($"╔══ WINDOW {w + 1}: \"{windows[w].Name}\" ══╗");
            sb.AppendLine($"║  ClassName: {windows[w].ClassName}");
            sb.AppendLine($"║  AutomationId: {windows[w].AutomationId}");
            sb.AppendLine($"║  ControlType: {windows[w].ControlType}");
            sb.AppendLine($"║  BoundingRectangle: {windows[w].BoundingRectangle}");
            sb.AppendLine($"║  IsEnabled: {windows[w].IsEnabled}");
            sb.AppendLine($"║  IsOffscreen: {windows[w].IsOffscreen}");
            sb.AppendLine($"║  ProcessId: {windows[w].Properties.ProcessId.ValueOrDefault}");
            sb.AppendLine($"║  HelpText: {SafeGet(() => windows[w].HelpText)}");
            sb.AppendLine("╚" + new string('═', 98) + "╝");

            DumpChildren(windows[w], sb, maxDepth, 1);
            sb.AppendLine();
        }

        var output = sb.ToString();
        Console.Write(output);

        if (outputFile != null)
        {
            File.WriteAllText(outputFile, output, Encoding.UTF8);
            Console.WriteLine($"\n>>> Tree saved to: {outputFile}");
        }
        else
        {
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"pcs-pro-ui-tree-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(defaultPath, output, Encoding.UTF8);
            Console.WriteLine($"\n>>> Tree saved to: {defaultPath}");
        }

        return 0;
    }

    private static void DumpChildren(AutomationElement parent, StringBuilder sb, int maxDepth, int depth)
    {
        if (depth > maxDepth) return;

        AutomationElement[] children;
        try
        {
            children = parent.FindAllChildren();
        }
        catch
        {
            return;
        }

        var indent = new string(' ', depth * 2);

        foreach (var child in children)
        {
            var automationId = SafeGet(() => child.AutomationId);
            var name = SafeGet(() => child.Name);
            var className = SafeGet(() => child.ClassName);
            var controlType = SafeGet(() => child.ControlType.ToString());
            var helpText = SafeGet(() => child.HelpText);
            var isEnabled = SafeGet(() => child.IsEnabled.ToString());
            var isOffscreen = SafeGet(() => child.IsOffscreen.ToString());
            var boundingRect = SafeGet(() => child.BoundingRectangle.ToString());

            var hasId = !string.IsNullOrWhiteSpace(automationId);
            var hasName = !string.IsNullOrWhiteSpace(name);
            var hasHelpText = !string.IsNullOrWhiteSpace(helpText);

            // Highlight elements with AutomationId (these are what we need)
            var marker = hasId ? "★" : "·";

            sb.Append($"{indent}{marker} [{controlType}]");
            if (hasId) sb.Append($"  AutomationId=\"{automationId}\"");
            if (hasName) sb.Append($"  Name=\"{Truncate(name!, 60)}\"");
            sb.Append($"  ClassName=\"{className}\"");
            if (hasHelpText) sb.Append($"  HelpText=\"{Truncate(helpText!, 60)}\"");
            sb.Append($"  Enabled={isEnabled}  Offscreen={isOffscreen}");
            sb.Append($"  Rect={boundingRect}");
            sb.AppendLine();

            DumpChildren(child, sb, maxDepth, depth + 1);
        }
    }

    private static string SafeGet(Func<string?> getter)
    {
        try { return getter() ?? ""; }
        catch { return "<error>"; }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static void PrintUsage()
    {
        Console.WriteLine("""
            PCS Pro UI Tree Dumper — FlaUI Automation Inspector

            Usage: UiTreeDumper [options]

            Options:
              -p, --process <name>   Process name without .exe (default: cricket)
              -d, --depth <n>        Max tree depth to traverse (default: 6)
              -o, --output <path>    Output file path (default: Desktop/pcs-pro-ui-tree-{timestamp}.txt)
              -h, --help             Show this help

            Examples:
              UiTreeDumper                          # Dump cricket.exe with default depth
              UiTreeDumper -d 10                    # Deeper tree traversal
              UiTreeDumper -p notepad               # Test with Notepad first
              UiTreeDumper -o C:\temp\tree.txt      # Custom output path

            Elements with AutomationId are marked with ★
            Elements without are marked with ·

            Workflow:
              1. Open PCS Pro and navigate to the screen you want to inspect
              2. Run this tool
              3. Review the output file for AutomationId, ClassName, Name values
              4. Report values back for implementation
            """);
    }
}
