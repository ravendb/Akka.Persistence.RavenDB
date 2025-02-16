using System.Linq.Expressions;
using System.Text;
using Akka.Hosting;
using Raven.Client.Documents.Conventions;

namespace Akka.Persistence.RavenDb.Hosting;

internal class RavenDbConventions
{
    private Dictionary<string, string> _conventions;
    public RavenDbConventions AddConvention<T>(Expression<Func<DocumentConventions, T>> path,T value)
    {
        var member = path.Body as MemberExpression ?? throw new ArgumentException("Path must be a member expression");
        if (member.Expression.NodeType != ExpressionType.Parameter)
            throw new NotSupportedException("Setting nested conventions currently not supported.");

        _conventions ??= new Dictionary<string, string>();
        _conventions.Add(member.Member.Name, value?.ToString() ?? "null");
        return this;
    }

    public StringBuilder BuildConvention(StringBuilder sb)
    {
        if (_conventions is { Count: > 0 })
        {
            sb.AppendLine("conventions {");
            foreach (var convention in _conventions)
            {
                sb.AppendLine($"{convention.Key} = {convention.Value.ToHocon()}");
            }
            sb.AppendLine("}");
        }

        return sb;
    }
}