using System.Text.RegularExpressions;
using Akka.Persistence.Query;

namespace Akka.Persistence.RavenDB.Query;

public class ChangeVectorOffset : Offset
{
    private readonly string _changeVector;

    public ChangeVectorOffset(string changeVector)
    {
        _changeVector = changeVector;
    }

    public override int CompareTo(Offset other)
    {
        if (other == null)
            return 1;

        if (ReferenceEquals(this, other))
            return 0;

        if (other is NoOffset)
        {
            if (_changeVector == null)
                return 0;

            return 1;
        }

        if (other is Sequence { Value: 0 }) 
            return 1;

        if (other is ChangeVectorOffset changeVectorOffset == false)
            throw new InvalidOperationException($"Can't compare {other.GetType()} with {GetType()}");

        return (int)(Etag - changeVectorOffset.Etag);
    }

    private long? _etag;
    public long Etag => _etag ??= long.Parse(EtagMatcher.Match(_changeVector).Groups[1].Value);

    private static Regex EtagMatcher => new Regex(@"\w{1,4}:(\d+)-.{22}", RegexOptions.Compiled);
    public override string ToString() => _changeVector;
}