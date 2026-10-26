using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

/// <summary>
/// The client IP and scheme from the ingress (task 024): only from the networks in <c>ForwardedHeaders:KnownNetworks</c>
/// (CIDR, e.g. the Container Apps infrastructure subnet), one hop. Without that setting the headers are ignored, so a
/// client cannot spoof its address for the rate limits, the audit log or the session list.
/// </summary>
internal static class ForwardedHeaders
{
    public static IApplicationBuilder UseKnownForwardedHeaders(this WebApplication app)
    {
        var networks = app.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        if (networks.Length == 0)
        {
            return app;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        return app.UseForwardedHeaders(options);
    }
}
