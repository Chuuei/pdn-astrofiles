using System.Collections;

namespace PdnAstroFiles.Imaging;

/// <summary>Display images together with a pixel-free copy of their original file metadata.</summary>
public sealed class ScientificImageSet(IReadOnlyList<ScientificImageDisplay> images,
    AstronomyMetadata? metadata, string? metadataError = null) : IReadOnlyList<ScientificImageDisplay>
{
    public AstronomyMetadata? Metadata { get; } = metadata;
    public string? MetadataError { get; } = metadataError;
    public int Count => images.Count;
    public ScientificImageDisplay this[int index] => images[index];
    public IEnumerator<ScientificImageDisplay> GetEnumerator() => images.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
