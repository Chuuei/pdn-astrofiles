using PaintDotNet.FileTypes;
using PdnAstroFiles.Imaging;

namespace PdnAstroFiles;

internal sealed class FitsFileTypeLoader : FileTypeLoader
{
    public FitsFileTypeLoader(IFileType fileType)
        : base(fileType)
    {
    }

    protected override IFileTypeDocument OnLoad(IFileTypeLoadContext context)
    {
        return AstronomyDocument.Create(context, Services.ImagingFactory, ScientificImageLoader.LoadFits(context.Input));
    }
}
