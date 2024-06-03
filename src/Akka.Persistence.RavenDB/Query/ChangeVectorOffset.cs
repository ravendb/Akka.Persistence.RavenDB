using System.Text;
using Akka.Persistence.Query;
using Raven.Client.Documents.Session;

namespace Akka.Persistence.RavenDb.Query;

public class ChangeVectorOffset : Offset
{
    public static string Code;
    static ChangeVectorOffset()
    {
        using (var stream = typeof(ChangeVectorAnalyzer).Assembly.GetManifestResourceStream("Akka.Persistence.RavenDb.ChangeVectorAnalyzer.cs"))
        using (var sr = new StreamReader(stream))
        {
            Code = sr.ReadToEnd();
        }
    }

    public Dictionary<string, long> Elements;

    public ChangeVectorOffset(string changeVector)
    {
        Elements = ChangeVectorAnalyzer.ToDictionary(changeVector);
    }

    private ChangeVectorOffset(ChangeVectorOffset copy)
    {
        Elements = new Dictionary<string, long>(copy.Elements);
    }

    public override int CompareTo(Offset other)
    {
        throw new NotSupportedException("you can't directly compare 2 change vectors");
    }

    public ChangeVectorOffset Merge(string changeVector)
    {
        var elements = ChangeVectorAnalyzer.ToDictionary(changeVector);

        foreach (var element in elements)
        {
            var value = element.Value;
            if (Elements.TryGetValue(element.Key, out var etag))
            {
                value = Math.Max(value, etag);
            }

            Elements[element.Key] = value;
        }

        return new ChangeVectorOffset(this);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var element in Elements)
        {
            sb.AppendLine($"{element.Key}:{element.Value}");
        }

        return sb.ToString();
    }

    public static ChangeVectorOffset Convert(Offset offset) =>
        offset switch
        {
            null => new ChangeVectorOffset(string.Empty),
            NoOffset _ => new ChangeVectorOffset(string.Empty),
            Sequence { Value: 0 } => new ChangeVectorOffset(string.Empty), 
            ChangeVectorOffset cv => cv,
            _ => throw new ArgumentException($"ReadJournal does not support {offset.GetType().Name} offsets")
        };

    public IAsyncDocumentQuery<T> ApplyOffset<T>(IAsyncDocumentQuery<T> q)
    {
        var first = true;
        foreach (var element in Elements)
        {
            if (first)
            {
                q = q.AndAlso().OpenSubclause();
                first = false;
            }
            else
            {
                q = q.OrElse();
            }

            q.WhereGreaterThan(element.Key, element.Value);
        }

        if (Elements.Count > 0)
        {
            q = q.CloseSubclause();
        }

        /*
        foreach (var changeVectorElement in Elements)
        {
            q = q.OrderBy(changeVectorElement.DatabaseId);
        }*/

        return q;
    }
}