using System.Text;

namespace CPCREDO.Infrastructure.Reports;

internal static class ReportCsv
{
    public static byte[] Render(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        WriteRow(sb, headers);
        foreach (var row in rows)
            WriteRow(sb, row);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void WriteRow(StringBuilder sb, IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
                sb.Append(';');
            sb.Append(Escape(cells[i]));
        }

        sb.Append("\r\n");
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(';') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
