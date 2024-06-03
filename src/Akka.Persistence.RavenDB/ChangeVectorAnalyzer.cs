using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Akka.Persistence.RavenDb;

public static class ChangeVectorAnalyzer
{
    public static Regex Pattern = new Regex(@"\w{1,4}:(\d+)-(.{22})", RegexOptions.Compiled);
    public static Dictionary<string, long> ToDictionary(string changeVector)
    {
        var dic = new Dictionary<string, long>();
        var matches = Pattern.Matches(changeVector);
        foreach (Match match in matches)
        {
            var databaseId = match.Groups[2].Value;
            var etag = long.Parse(match.Groups[1].Value);
            dic.Add(databaseId, etag);
        }

        return dic;
    }
}