using PaintDotNet;
using PaintDotNet.FileTypes;

namespace PdnAstroFiles;

[PluginSupportInfo(typeof(PluginSupportInfo))]
public sealed class FitsFileType : FileType
{
    public FitsFileType(IFileTypeHost host)
        : base(host, "Flexible Image Transport System", FileTypeOptions.Create() with
        {
            LoadExtensions = [".fits", ".fit", ".fts"],
            SaveExtensions = [".fits", ".fit", ".fts"],
            IsSavingConfigurable = true,
            SupportsSavingLayers = true
        })
    {
    }

    protected override IFileTypeLoader OnCreateLoader() => new FitsFileTypeLoader(this);
    protected override IFileTypeSaver OnCreateSaver() => new AstronomyFileTypeSaver(this, Imaging.AstronomyFormat.Fits);
}
