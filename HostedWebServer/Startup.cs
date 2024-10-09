using Microsoft.Owin;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;
using Owin;
using System.IO;


[assembly: OwinStartup(typeof(HostedWebServer.Startup))]
namespace HostedWebServer
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {

            // Serve static files from the specified directory
            var fileSystem = new PhysicalFileSystem("C:\\Users\\matteo\\source\\repos\\react\\wow-board\\build");
            var options = new FileServerOptions
            {
                FileSystem = fileSystem,
                EnableDefaultFiles = true
            };
            options.StaticFileOptions.ServeUnknownFileTypes = true;
            options.DefaultFilesOptions.DefaultFileNames = new[] { "index.html" };

            app.UseFileServer(options);            
        }
    }

    public static class StaticFileExtensions
    {
        public static void UseStaticFiles(this IAppBuilder app, string rootFolder)
        {
            app.Use(async (context, next) =>
            {
                var filePath = context.Request.Path.Value.TrimStart('/');
                var fullPath = Path.Combine(rootFolder, filePath);

                if (File.Exists(fullPath))
                {
                    var fileBytes = File.ReadAllBytes(fullPath);
                    var contentType = GetContentType(fullPath);
                    context.Response.ContentType = contentType;
                    await context.Response.WriteAsync(fileBytes);
                }
                else
                {
                    await next();
                }
            });
        }

        private static string GetContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            switch (extension)
            {
                case ".html":
                    return "text/html";
                case ".js":
                    return "application/javascript";
                case ".css":
                    return "text/css";
                case ".svg":
                    return "image/svg+xml";
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                default:
                    return "application/octet-stream";
            }
        }
    }
}
