using PaintDotNet;
using PaintDotNet.FileTypes;

namespace PdnAstroFiles;

[PluginSupportInfo(typeof(PluginSupportInfo))]
public sealed class XisfFileType : FileType
{
    public XisfFileType(IFileTypeHost host)
        : base(host, "Extensible Image Serialization Format", FileTypeOptions.Create() with
        {
            LoadExtensions = [".xisf"],
            SaveExtensions = [".xisf"],
            IsSavingConfigurable = true,
            SupportsSavingLayers = true
        })
    {
    }

    protected override IFileTypeLoader OnCreateLoader() => new XisfFileTypeLoader(this);
    protected override IFileTypeSaver OnCreateSaver() => new AstronomyFileTypeSaver(this, Imaging.AstronomyFormat.Xisf);
}
