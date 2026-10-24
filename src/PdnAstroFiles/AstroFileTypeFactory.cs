using PaintDotNet.FileTypes;

namespace PdnAstroFiles;

/// <summary>
/// Entry point used by Paint.NET to discover the astronomical file types.
/// </summary>
public sealed class AstroFileTypeFactory : IFileTypeFactory
{
    public IFileType[] CreateFileTypes(IFileTypeHost host) =>
    [
        new XisfFileType(host),
        new FitsFileType(host)
    ];
}
