using System.Text.RegularExpressions;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace LazyOccultCrescent;

public static class LogMessageHelper
{
    public static string GetLogMessagePattern(uint id)
    {
        var pattern = Svc.Data.GetExcelSheet<LogMessage>().GetRow(id).Text.ToString();
        // Replace numeric args
        pattern = Regex.Replace(pattern, @"<num\((\w+)\)>", m => $"(?<{m.Groups[1].Value}>\\d+)");

        return pattern;
    }
}
