using System.Linq.Expressions;
using System.Text;
using Akka.Hosting;
using Raven.Client.Documents.Conventions;

namespace Akka.Persistence.RavenDb.Hosting;

public interface IRavenDbOptions
{
    /// <summary>
    /// Name of the database
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// Urls of the RavenDB server
    /// </summary>
    public string[] Urls { get; set; }
    /// <summary>
    /// Path to the certificate
    /// </summary>
    public string? CertificatePath { get; set; }
    /// <summary>
    /// Http version to use for communication between the RavenDB client and server
    /// </summary>
    public Version? HttpVersion { get; set; }

    /// <summary>
    /// Flag to disable tcp compression
    /// </summary>
    public bool? DisableTcpCompression { get; set; }

    /// <summary>
    /// Timeout for save changes operation
    /// </summary>
    public TimeSpan? SaveChangesTimeout { get; set; }

    /// <summary>
    /// Allow to configure Conventions for the RavenDB client.
    /// Currently only non-nested properties are supported.
    ///
    /// Conventions set here take precedence over other options in this class.
    /// </summary>
    public void AddConvention<T>(Expression<Func<DocumentConventions, T>> path, T value);

    internal RavenDbConventions Conventions { get; set; }
}

internal static class RavenDbOptions
{
    public static void Build(StringBuilder sb, IRavenDbOptions options)
    {
        options.Conventions?.BuildConvention(sb);

        if (options.Name is not null)
            sb.AppendLine($"name = {options.Name.ToHocon()}");

        if(options.Urls is not null && options.Urls.Length > 0)
            sb.AppendLine($"urls = [{string.Join(",", options.Urls.Select(x => x.ToHocon()))}]");

        if (options.CertificatePath is not null)
            sb.AppendLine($"certificate-path = {options.CertificatePath.ToHocon()}");

        if (options.HttpVersion is not null)
            sb.AppendLine($"http-version = {options.HttpVersion.ToString().ToHocon()}");

        if (options.DisableTcpCompression is not null)
            sb.AppendLine($"disable-tcp-compression = {options.DisableTcpCompression.ToHocon()}");

        if (options.SaveChangesTimeout is not null)
            sb.AppendLine($"save-changes-timeout = {options.SaveChangesTimeout.ToHocon(allowInfinite: true, zeroIsInfinite: true)}");
    }

    public static void AddConvention<T>(IRavenDbOptions options, Expression<Func<DocumentConventions, T>> path, T value)
    {
        options.Conventions ??= new RavenDbConventions();
        options.Conventions.AddConvention(path, value);
    }
}