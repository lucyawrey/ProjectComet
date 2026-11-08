using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Comet.Server;

public static class WebBuild
{
    /// <summary>
    /// The page's Content Security Policy: scripts only from the page's own site (Unity's loader adds blob: scripts
    /// and needs 'wasm-unsafe-eval'), fonts from Google Fonts, and connections to the page's own server and the Login
    /// server at <paramref name="loginUrl"/>.
    /// </summary>
    public static string PagePolicy(string loginUrl)
    {
        var login = Uri.TryCreate(loginUrl, UriKind.Absolute, out var url) ? " " + url.GetLeftPart(UriPartial.Authority) : "";
        return "default-src 'self'; script-src 'self' blob: 'wasm-unsafe-eval'; style-src 'self' https://fonts.googleapis.com; "
            + "font-src https://fonts.gstatic.com; img-src 'self' data: blob:; worker-src 'self' blob:; "
            + $"connect-src 'self'{login}; object-src 'none'; base-uri 'none'";
    }

    /// <summary>
    /// Serves the Unity web build in <paramref name="webRoot"/> at the root, for development. The build is
    /// Brotli-compressed with the decompression fallback (files named .unityweb): browsers that accept br get
    /// Content-Encoding: br and decompress natively; the rest (Firefox over plain HTTP) get the bytes as they are,
    /// and Unity's loader unpacks them.
    /// </summary>
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
                        // Checked on every load, so a page reloaded for new content gets it.
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
