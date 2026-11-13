using System;
using System.IO;
using DocumentProcessor.WebForms.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace DocumentProcessor.WebForms.Services
{
    /// <summary>
    /// Stores uploads on the local file system under a date-partitioned path below
    /// App_Data, which is outside wwwroot and therefore never served directly.
    /// </summary>
    public class FileSystemDocumentStorage : IDocumentStorage
    {
        private readonly string _rootPath;

        public FileSystemDocumentStorage(IWebHostEnvironment environment)
        {
            _rootPath = ResolveRootPath(environment.ContentRootPath, AppSettings.StorageRootPath);
        }

        /// <summary>
        /// Replaces HostingEnvironment.MapPath: a "~/..." (or relative) Storage.RootPath is resolved
        /// against the content root; an absolute path is used as-is.
        /// </summary>
        public static string ResolveRootPath(string contentRootPath, string configuredPath)
        {
            var path = configuredPath.Trim();

            if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            {
                path = path.Substring(2);
            }
            else if (path == "~")
            {
                path = string.Empty;
            }

            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            path = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(contentRootPath, path));
        }

        public string Save(Stream content, string fileName)
        {
            var now = DateTimeOffset.UtcNow;

            var uniqueName = Path.GetFileNameWithoutExtension(fileName)
                + "_" + now.ToString("yyyyMMddHHmmssfff")
                + Path.GetExtension(fileName);

            var relativePath = Path.Combine(
                now.Year.ToString("D4"),
                now.Month.ToString("D2"),
                now.Day.ToString("D2"),
                uniqueName);

            var fullPath = Path.Combine(_rootPath, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            using (var file = File.Create(fullPath))
            {
                content.CopyTo(file);
            }

            return relativePath;
        }

        public Stream OpenRead(string storagePath)
        {
            // Paths stored by the .NET Framework build use '\' separators; normalise for Linux hosts.
            var normalised = storagePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            return File.OpenRead(Path.Combine(_rootPath, normalised));
        }
    }
}
