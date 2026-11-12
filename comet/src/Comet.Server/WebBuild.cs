using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Comet.Server;

public static class WebBuild
{
    /// <summary>Unity's loader needs blob: and 'wasm-unsafe-eval'; connections only to the page's server and <paramref name="loginUrl"/>.</summary>
    public static string PagePolicy(string loginUrl)
    {
        var login = Uri.TryCreate(loginUrl, UriKind.Absolute, out var url) ? " " + url.GetLeftPart(UriPartial.Authority) : "";
        return "default-src 'self'; script-src 'self' blob: 'wasm-unsafe-eval'; style-src 'self' https://fonts.googleapis.com; "
            + "font-src https://fonts.gstatic.com; img-src 'self' data: blob:; worker-src 'self' blob:; "
            + $"connect-src 'self'{login}; object-src 'none'; base-uri 'none'";
    }

    /// <summary>.unityweb files are Brotli: br browsers decompress them; others (Firefox over plain HTTP) get raw bytes for Unity's loader.</summary>
    public static void Serve(WebApplication app, string webRoot)
    {
        var files = new PhysicalFileProvider(Path.GetFullPath(webRoot));
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".data"] = "application/octet-stream";
        types.Mappings[".bin"] = "application/octet-stream";
        types.Mappings[".unityweb"] = "application/octet-stream";
        types.Mappings[".webmanifest"] = "application/manifest+json";
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider = files,
                ContentTypeProvider = types,
                OnPrepareResponse = context =>
                {
                    if (context.File.Name == "content.bin")
                    {
                        context.Context.Response.Headers.CacheControl = "no-cache";
                        return;
                    }

                    if (!context.File.Name.EndsWith(".unityweb", StringComparison.Ordinal))
                    {
                        return;
                    }

                    var headers = context.Context.Response.Headers;
                    headers.Vary = "Accept-Encoding";
                    if (context.Context.Request.Headers.AcceptEncoding.ToString().Contains("br", StringComparison.Ordinal))
                    {
                        headers.ContentEncoding = "br";
                    }
                },
            }
        );
        app.Logger.LogInformation("Serving the web build in {WebRoot}", webRoot);
    }
}
