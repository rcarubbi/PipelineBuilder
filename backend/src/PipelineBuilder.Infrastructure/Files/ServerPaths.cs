using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Infrastructure.Files;

public sealed class ServerPaths(IOptions<PipelineFileOptions> options, IHostEnvironment environment)
{
    public string Input(string path) => Resolve(options.Value.InputRoot, path, true);
    public string Output(string path) => Resolve(options.Value.OutputRoot, path, false);

    private string Resolve(string configuredRoot, string path, bool input)
    {
        try
        {
            var root = Path.GetFullPath(configuredRoot, environment.ContentRootPath);
            var full = Path.GetFullPath(path, root);
            var relative = Path.GetRelativePath(root, full);
            if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relative) || relative.Contains(':')) throw new PipelineDataException("File path must stay inside the configured data directory.");
            for (var entry = full; !string.IsNullOrEmpty(entry); entry = Path.GetDirectoryName(entry))
            {
                if ((File.Exists(entry) || Directory.Exists(entry)) && File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                    throw new PipelineDataException("Symbolic links and reparse points are not allowed in data paths.");
            }
            if (input && !File.Exists(full)) throw new PipelineDataException("Input file does not exist in the input directory.");
            if (!input && !Directory.Exists(Path.GetDirectoryName(full))) throw new PipelineDataException("Output directory does not exist.");
            if (!input && (File.Exists(full) || Directory.Exists(full))) throw new PipelineDataException("Output already exists; choose a different filename.");
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException)
        {
            throw new PipelineDataException("File path is invalid or inaccessible.", inner: ex);
        }
    }
}
