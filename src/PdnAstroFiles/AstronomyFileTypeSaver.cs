using System.IO;
using PaintDotNet.FileTypes;
using PdnAstroFiles.Imaging;

namespace PdnAstroFiles;

internal sealed class AstronomyFileTypeSaver(IFileType fileType, AstronomyFormat format) : FileTypeSaver(fileType)
{
    protected override IFileTypeSaveOptions OnCreateDefaultSaveOptions() => new AstronomySaveOptions();
    protected override IFileTypeSaveOptionsControl OnCreateSaveOptionsControl() => new AstronomySaveOptionsControl();

    protected override IFileTypeSaveOptions OnCreateSaveOptionsForDocument(ICreateSaveOptionsForDocumentContext context)
    {
        var options = new AstronomySaveOptions();
        try
        {
            var metadata = AstronomyDocumentSaver.GetMetadata(context.Document);
            options.CanPreserveGeometry = AstronomyImageWriter.GeometryMatches(metadata, context.Document.Size.Width, context.Document.Size.Height);
            if (!options.CanPreserveGeometry)
                options.Status += "\n\nThe dimensions differ from the original image. Plate-solving metadata will be removed.";
            if (metadata.Images.Length > 1)
                options.Status += "\n\nEach original image layer is saved separately, including hidden layers.";
            if (metadata.Format != format)
            {
                options.Status += "\n\nFITS/XISF metadata will be converted where possible. Unsupported fields may be omitted; omissions are recorded in the output history.";
                if (format == AstronomyFormat.Fits && metadata.Images.Any(i => i.Orientation != "0"))
                {
                    options.CanPreserveGeometry = false;
                    options.Status += "\n\nXISF orientation will be baked into the FITS pixels. The original coordinate solution will be removed.";
                }
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or System.Text.Json.JsonException)
        {
            options.CanPreserveGeometry = false;
            options.Status = "Original astronomy metadata is unavailable. The edited image will be saved with its supported canvas information.";
        }
        return options;
    }

    protected override void OnSave(IFileTypeSaveContext context) =>
        AstronomyDocumentSaver.Save(context.Document, Services.ImagingFactory, context.Output, format,
            context.Options is AstronomySaveOptions { GeometryUnchanged: true });
}
