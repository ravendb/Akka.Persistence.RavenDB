using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Akka.Persistence.RavenDB;

public static class ChangeVectorAnalyzer
{
    public static Regex Pattern = new Regex(@"\w{1,4}:(\d+)-(.{22})", RegexOptions.Compiled);
    public static List<ChangeVectorElement> ToList(string changeVector)
    {
        var list = new List<ChangeVectorElement>();
        var matches = Pattern.Matches(changeVector);
        foreach (Match match in matches)
        {
            var element = new ChangeVectorElement
            {
                DatabaseId = match.Groups[2].Value,
                Etag = long.Parse(match.Groups[1].Value)
            };
            list.Add(element);
        }

        return list;
    }

    public struct ChangeVectorElement
    {
        public string DatabaseId;
        public long Etag;
    }
}