using System;
using System.IO;
using System.Linq;
using System.Text;
using GlamImport.Parsing;

// Snapshot tests for the paste parser.
// Every Samples/<name>.txt is a copied Eorzea Collection page. The parser's reading of it is compared with
// Samples/<name>.expected.txt. A new sample gets its .expected.txt written on the first run: open it and check it once.
// After an intended parser change, run with --update to accept the new readings (and review the diff).
var update = args.Contains("--update");
var dir = Path.Combine(AppContext.BaseDirectory, "Samples");
var sourceDir = FindSourceSamples() ?? dir;
var failed = 0;
var samples = Directory.GetFiles(sourceDir, "*.txt").Where(f => !f.EndsWith(".expected.txt")).OrderBy(f => f).ToList();

foreach (var sample in samples)
{
    var name = Path.GetFileNameWithoutExtension(sample);
    var expectedPath = Path.Combine(sourceDir, name + ".expected.txt");
    var actual = Describe(GlamParser.Parse(File.ReadAllText(sample)));

    if (update || !File.Exists(expectedPath))
    {
        File.WriteAllText(expectedPath, actual);
        Console.WriteLine($"{(update ? "UPDATED" : "NEW    ")} {name}  (check {name}.expected.txt)");
        continue;
    }

    var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");
    if (expected == actual)
    {
        Console.WriteLine($"ok      {name}");
        continue;
    }

    failed++;
    Console.WriteLine($"FAILED  {name}");
    var e = expected.Split('\n');
    var a = actual.Split('\n');
    for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
    {
        var el = i < e.Length ? e[i] : "(missing)";
        var al = i < a.Length ? a[i] : "(missing)";
        if (el != al)
        {
            Console.WriteLine($"        line {i + 1}: expected \"{el}\"");
            Console.WriteLine($"                 got      \"{al}\"");
            break;
        }
    }
}

Console.WriteLine(failed == 0 ? $"\n{samples.Count} samples, all ok." : $"\n{failed} of {samples.Count} samples failed.");
return failed == 0 ? 0 : 1;

static string Describe(ParsedGlam g)
{
    var sb = new StringBuilder();
    sb.Append("Title: ").Append(g.Title ?? "-").Append('\n');
    sb.Append("Creator: ").Append(g.Creator ?? "-").Append('\n');
    sb.Append("Complete glam: ").Append(g.IsCompleteGlam ? "yes" : "no").Append('\n');
    sb.Append("Intended for: ").Append(g.IntendedFor ?? "-").Append('\n');
    sb.Append("Fits: ").Append(g.Fits ?? "-").Append('\n');
    sb.Append("Model: ").Append(g.Model ?? "-").Append('\n');
    sb.Append("Tags: ").Append(g.Tags is { Count: > 0 } t ? string.Join(" | ", t) : "-").Append('\n');
    sb.Append("Description: ").Append(g.Description == null ? "-" : "\n  " + g.Description.Replace("\n", "\n  ")).Append('\n');
    sb.Append("Pieces:\n");
    foreach (var p in g.Pieces)
    {
        var dyes = p.Dyes.Count == 0 ? string.Empty : $" [{string.Join(", ", p.Dyes.Select(d => d ?? "Undyed"))}]";
        sb.Append($"  {p.Slot} ({p.Label}): {p.ItemName}{dyes}\n");
    }

    return sb.ToString();
}

// When run from the project folder (dotnet run), use the Samples folder next to the sources so new expectations land there.
static string? FindSourceSamples()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        var candidate = Path.Combine(d.FullName, "Samples");
        if (File.Exists(Path.Combine(d.FullName, "GlamImport.Tests.csproj")) && Directory.Exists(candidate))
            return candidate;
    }

    return null;
}
