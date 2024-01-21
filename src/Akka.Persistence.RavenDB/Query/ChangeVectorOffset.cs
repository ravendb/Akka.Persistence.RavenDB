using Akka.Persistence.Query;
using Raven.Client.Documents.Session;

namespace Akka.Persistence.RavenDb.Query;

public class ChangeVectorOffset : Offset
{
    public static string Code = File.ReadAllText(@"C:\Work\Akka.Persistence.RavenDB\src\Akka.Persistence.RavenDB\ChangeVectorAnalyzer.cs");

    public string ChangeVector;
    public List<ChangeVectorAnalyzer.ChangeVectorElement> Elements;
    public ChangeVectorOffset(string changeVector)
    {
        ChangeVector = changeVector;
        Elements = ChangeVectorAnalyzer.ToList(changeVector);
    }
    public override int CompareTo(Offset other)
    {
        throw new NotSupportedException("you can't directly compare 2 change vectors");
    }

    public ChangeVectorOffset Clone()
    {
        return new ChangeVectorOffset(ChangeVector);
    }

    public override string ToString() => ChangeVector;

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
        for (var index = 0; index < Elements.Count; index++)
        {
            if (index == 0)
            {
                q = q.AndAlso().OpenSubclause();
            }
            else
            {
                q = q.OrElse();
            }

            var element = Elements[index];
            q.WhereGreaterThan(element.DatabaseId, element.Etag);

            if (index == Elements.Count - 1)
            {
                q = q.CloseSubclause();
            }
        }

        /*
        foreach (var changeVectorElement in Elements)
        {
            q = q.OrderBy(changeVectorElement.DatabaseId);
        }*/

        return q;
    }
}