using System.Text;
using FlaUI.Core.AutomationElements;

namespace AutomationDiagnostic;

/// <summary>
/// Utility methods for dumping the FlaUI automation tree of a window or element.
/// Used by the diagnostic runner to produce actionable output when an element
/// cannot be found at a given step.
/// </summary>
internal static class TreeDumper
{
    /// <summary>
    /// Dumps the UI automation subtree of <paramref name="root"/> as an indented
    /// text block. Elements with an AutomationId are highlighted with ★.
    /// </summary>
    public static string Dump(AutomationElement root, int maxDepth = 5)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"╔══ ROOT: \"{SafeGet(() => root.Name)}\" ══╗");
        sb.AppendLine($"║  ClassName: {SafeGet(() => root.ClassName)}");
        sb.AppendLine($"║  AutomationId: {SafeGet(() => root.AutomationId)}");
        sb.AppendLine($"║  ControlType: {SafeGet(() => root.ControlType.ToString())}");
        sb.AppendLine($"║  BoundingRectangle: {SafeGet(() => root.BoundingRectangle.ToString())}");
        sb.AppendLine("╚" + new string('═', 80) + "╝");

        DumpChildren(root, sb, maxDepth, 1);
        return sb.ToString();
    }

    private static void DumpChildren(AutomationElement parent, StringBuilder sb, int maxDepth, int depth)
    {
        if (depth > maxDepth) return;

        AutomationElement[] children;
        try { children = parent.FindAllChildren(); }
        catch { return; }

        var indent = new string(' ', depth * 2);

        foreach (var child in children)
        {
            var automationId = SafeGet(() => child.AutomationId);
            var name = SafeGet(() => child.Name);
            var className = SafeGet(() => child.ClassName);
            var controlType = SafeGet(() => child.ControlType.ToString());
            var helpText = SafeGet(() => child.HelpText);

            var hasId = !string.IsNullOrWhiteSpace(automationId);
            var hasName = !string.IsNullOrWhiteSpace(name);
            var hasHelpText = !string.IsNullOrWhiteSpace(helpText);
            var marker = hasId ? "★" : "·";

            sb.Append($"{indent}{marker} [{controlType}]");
            if (hasId) sb.Append($"  AutomationId=\"{automationId}\"");
            if (hasName) sb.Append($"  Name=\"{Truncate(name!, 80)}\"");
            sb.Append($"  ClassName=\"{className}\"");
            if (hasHelpText) sb.Append($"  HelpText=\"{Truncate(helpText!, 60)}\"");
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
}
