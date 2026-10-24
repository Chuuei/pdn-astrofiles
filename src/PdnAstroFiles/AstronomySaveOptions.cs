using PaintDotNet.FileTypes;

namespace PdnAstroFiles;

internal sealed class AstronomySaveOptions : FileTypeSaveOptions
{
    public bool GeometryUnchanged { get; set; }
    public bool CanPreserveGeometry { get; set; } = true;
    public string Status { get; set; } = "Target, observation time and equipment information are retained.";

    protected override FileTypeSaveOptions OnClone() => new AstronomySaveOptions
    {
        GeometryUnchanged = GeometryUnchanged, CanPreserveGeometry = CanPreserveGeometry, Status = Status
    };

    // A geometry assertion belongs to this save operation, never to another document
    // or a later editing session. Do not persist it as the user's default preference.
    protected override void OnSerializeTo(IFileTypePropertyBag properties) { }
    protected override void OnDeserializeFrom(IReadOnlyFileTypePropertyBag properties) => GeometryUnchanged = false;
}
