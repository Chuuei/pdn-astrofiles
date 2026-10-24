using PaintDotNet.FileTypes;
using PdnAstroFiles.Imaging;

namespace PdnAstroFiles;

internal sealed class XisfFileTypeLoader : FileTypeLoader
{
    public XisfFileTypeLoader(IFileType fileType)
        : base(fileType)
    {
    }

    protected override IFileTypeDocument OnLoad(IFileTypeLoadContext context)
    {
        return AstronomyDocument.Create(context, Services.ImagingFactory, ScientificImageLoader.LoadXisf(context.Input));
    }
}
