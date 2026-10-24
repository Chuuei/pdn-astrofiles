using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using PaintDotNet.ComponentModel;
using PaintDotNet.FileTypes;
using PaintDotNet.Imaging;
using PdnAstroFiles.Imaging;
using XISFLib;
using AstronomyMetadata = PdnAstroFiles.Imaging.AstronomyMetadata;

namespace PdnAstroFiles.Plugin.Tests;

public sealed class SavingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedAstronomyDocumentCanBeSavedByPaintNetsOwnPngSaver(bool fits)
    {
        using var imported = new Imported(fits);
        string application = Path.Combine(AppContext.BaseDirectory, "paintdotnet.dll");
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(application);
        var host = (IFileTypeHost)Activator.CreateInstance(assembly.GetType("PaintDotNet.FileTypes.FileTypeHost", true)!, nonPublic: true)!;
        using var fileType = (IPngFileType)Activator.CreateInstance(assembly.GetType("PaintDotNet.FileTypes.PngFileType", true)!,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [host], null)!;
        using var saver = fileType.CreateSaver();
        using var output = new MemoryStream();
        using var metadataFromLoad = imported.Factory.CreateCustomMetadata();
        var options = (IPngFileTypeSaveOptions)saver.CreateSaveOptionsForDocument(imported.Document,
            saver.CreateDefaultSaveOptions(), metadataFromLoad);
        saver.Save(output, imported.Document, options, null);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, output.ToArray()[..8]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeAstronomyDocumentCanBeSavedInTheOtherAstronomyFormat(bool fitsSource)
    {
        using var imported = new Imported(fitsSource, multiple: true);
        using var saved = new MemoryStream();
        AstronomyDocumentSaver.Save(imported.Document, imported.Imaging, saved, fitsSource ? AstronomyFormat.Xisf : AstronomyFormat.Fits, true);
        saved.Position = 0;
        var images = fitsSource ? ScientificImageLoader.LoadXisf(saved) : ScientificImageLoader.LoadFits(saved);
        Assert.Equal(2, images.Count);
        Assert.Equal((2, 2), (images[1].Width, images[1].Height));
        saved.Position = 0;
        if (fitsSource) Assert.Equal("M31", XisfReader.Load(saved).Images[1].Properties["Observation:Object:Name"].Value);
        else Assert.Equal("M31", FitsReader.Load(saved).Hdus[1].Header.GetRequiredValue<string>("OBJECT"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AstronomyExportAlsoWorksWhenOriginalMetadataIsUnavailable(bool fits)
    {
        using var imported = new Imported(false);
        using (var metadata = imported.Document.Metadata.Custom.CreateTransaction()) metadata.Clear();
        using var saved = new MemoryStream();
        AstronomyDocumentSaver.Save(imported.Document, imported.Imaging, saved, fits ? AstronomyFormat.Fits : AstronomyFormat.Xisf, false);
        saved.Position = 0;
        var images = fits ? ScientificImageLoader.LoadFits(saved) : ScientificImageLoader.LoadXisf(saved);
        Assert.Single(images);
        float[] row = new float[16]; images[0].CopyRgbaRow(0, row);
        Assert.Equal(0.25f, row[4], 6);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeEditedDocumentSavesPixelsAndOriginalMetadata(bool fits, bool keepGeometry)
    {
        using var imported = new Imported(fits);
        using (var layer = imported.Document.Layers[0].CreateRef<IFileTypeBitmapLayer<ColorRgba128Float>>())
        using (var bitmap = layer.GetBitmap())
        using (var bitmapLock = bitmap.Lock(BitmapLockOptions.Write))
            bitmapLock.AsRegionPtr().Rows[0].Span[0] = new ColorRgba128Float(0.65f, 0.35f, 0.15f, 1f);
        using var saved = new MemoryStream();
        AstronomyDocumentSaver.Save(imported.Document, imported.Imaging, saved, fits ? AstronomyFormat.Fits : AstronomyFormat.Xisf, keepGeometry);
        saved.Position = 0;
        var images = fits ? ScientificImageLoader.LoadFits(saved) : ScientificImageLoader.LoadXisf(saved);
        float[] row = new float[16];
        images[0].CopyRgbaRow(0, row);
        Assert.Equal(0.65f, row[0], 6);
        Assert.Equal(0.35f, row[1], 6);
        Assert.Equal(0.15f, row[2], 6);
        Assert.Equal(ImageStretch.None, images[0].Stretch);
        saved.Position = 0;
        if (fits)
        {
            var header = FitsReader.Load(saved).Hdus[0].Header;
            Assert.Equal("M42", header.GetRequiredValue<string>("OBJECT"));
            Assert.Equal(keepGeometry, header.FindFirst("CRPIX1") is not null);
        }
        else
        {
            var image = XisfReader.Load(saved).Images[0];
            Assert.Equal("M42", image.Properties["Observation:Object:Name"].Value);
            Assert.Equal(keepGeometry, image.Properties.ContainsKey("AstrometricSolution:ReferenceImageCoordinates"));
        }
        // The metadata lives in the document, rather than the loader or source stream.
        using var cloned = imported.Document.Metadata.Clone();
        Assert.Equal(imported.Document.Metadata.Custom[AstronomyMetadata.DocumentKey], cloned.Custom[AstronomyMetadata.DocumentKey]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EightBitCanvasCanBeSavedWithoutChangingItsDisplayEncoding(bool fits)
    {
        using var imported = new Imported(fits);
        using var document = imported.Factory.CreateDocument<ColorBgra32>(4, 2);
        document.Metadata.ReplaceFrom(imported.Document.Metadata);
        using var layer = document.CreateBitmapLayer();
        using (var bitmap = layer.GetBitmap())
        using (var bitmapLock = bitmap.Lock(BitmapLockOptions.Write))
        {
            var pixels = bitmapLock.AsRegionPtr();
            for (int y = 0; y < 2; y++)
            foreach (ref var pixel in pixels.Rows[y].Span)
                pixel = new ColorBgra32 { R = 128, G = 64, B = 32, A = 255 };
        }
        document.Layers.Add(layer);
        using var saved = new MemoryStream();
        AstronomyDocumentSaver.Save(document, imported.Imaging, saved, fits ? AstronomyFormat.Fits : AstronomyFormat.Xisf, true);
        saved.Position = 0;
        var images = fits ? ScientificImageLoader.LoadFits(saved) : ScientificImageLoader.LoadXisf(saved);
        float[] row = new float[16]; images[0].CopyRgbaRow(0, row);
        Assert.Equal(128 / 255f, row[0], 5);
        Assert.Equal(64 / 255f, row[1], 5);
        Assert.Equal(32 / 255f, row[2], 5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenOriginalImageLayersKeepTheirOwnMetadataAndPixels(bool fits)
    {
        using var imported = new Imported(fits, multiple: true);
        Assert.False(imported.Document.Layers[1].Visible);
        using var saved = new MemoryStream();
        AstronomyDocumentSaver.Save(imported.Document, imported.Imaging, saved, fits ? AstronomyFormat.Fits : AstronomyFormat.Xisf, true);
        saved.Position = 0;
        var images = fits ? ScientificImageLoader.LoadFits(saved) : ScientificImageLoader.LoadXisf(saved);
        Assert.Equal(2, images.Count);
        Assert.Equal((2, 2), (images[1].Width, images[1].Height));
        float[] row = new float[16]; images[1].CopyRgbaRow(0, row);
        Assert.Equal(0.9f, row[0], 6);
        saved.Position = 0;
        if (fits) Assert.Equal("M31", FitsReader.Load(saved).Hdus[1].Header.GetRequiredValue<string>("OBJECT"));
        else Assert.Equal("M31", XisfReader.Load(saved).Images[1].Properties["Observation:Object:Name"].Value);
    }

    [Fact]
    public void GeometryConfirmationIsNeverRememberedAsASaveDefault()
    {
        using var imported = new Imported(false);
        using var transaction = imported.Document.Metadata.Custom.CreateTransaction();
        var options = new AstronomySaveOptions { GeometryUnchanged = true };
        options.SerializeTo(transaction);
        var restored = new AstronomySaveOptions();
        restored.DeserializeFrom(transaction);
        Assert.False(restored.GeometryUnchanged);
        Assert.True(((AstronomySaveOptions)options.Clone()).GeometryUnchanged);
    }

    [Fact]
    public void SaveOptionsShowTheGeometryChoiceAndDisableItWhenDimensionsChanged()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new System.Windows.Forms.Form { ClientSize = new(390, 320), Text = "AstroFiles save options",
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new(-30000, -30000), ShowInTaskbar = false };
                using var control = new AstronomySaveOptionsControl { Dock = System.Windows.Forms.DockStyle.Fill };
                form.Controls.Add(control);
                form.Show(); System.Windows.Forms.Application.DoEvents(); form.PerformLayout(); control.PerformLayout();
                var checkbox = control.Controls[0].Controls.OfType<System.Windows.Forms.CheckBox>().Single();
                Assert.False(checkbox.Checked);
                checkbox.Checked = true;
                Assert.True(((AstronomySaveOptions)control.Options!).GeometryUnchanged);
                checkbox.Checked = false;
                using var rendered = new Bitmap(control.Width, control.Height);
                control.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                string path = Path.GetFullPath("../../../../../artifacts/save-options.png", AppContext.BaseDirectory);
                rendered.Save(path, ImageFormat.Png);
                control.Options = new AstronomySaveOptions { CanPreserveGeometry = false, GeometryUnchanged = true,
                    Status = "The image dimensions changed. Plate-solving metadata will be removed. Target, time and equipment information are retained." };
                Assert.False(checkbox.Enabled);
                Assert.False(checkbox.Checked);
                Assert.False(((AstronomySaveOptions)control.Options!).GeometryUnchanged);
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class Imported : IDisposable
    {
        public IFileTypeDocumentFactory Factory { get; }
        public IImagingFactory Imaging { get; } = ImagingFactory.CreateRef();
        public IFileTypeDocument Document { get; }
        private readonly IDisposable context;
        private readonly MemoryStream stream = new();

        public Imported(bool fits, bool multiple = false)
        {
            var first = XisfImageData.FromSamples(new[] { 0f, 0.25f, 0.75f, 1f, 0.1f, 0.4f, 0.6f, 0.9f }, [4, 2], 1);
            first.Properties["Observation:Object:Name"] = "M42";
            first.Properties["AstrometricSolution:ReferenceImageCoordinates"] = new double[] { 2.5, 1.5 };
            first.FitsKeywords.Add(XisfFitsKeyword.FromValue("OBJECT", "M42"));
            first.FitsKeywords.Add(XisfFitsKeyword.FromValue("CRPIX1", 2.5));
            var second = XisfImageData.FromSamples(new[] { 0.9f, 0.3f, 0.6f, 1f }, [2, 2], 1);
            second.Properties["Observation:Object:Name"] = "M31";
            second.FitsKeywords.Add(XisfFitsKeyword.FromValue("OBJECT", "M31"));
            XisfImageData[] images = multiple ? [first, second] : [first];
            if (fits) FitsWriter.Write(stream, FitsConverter.FromXisf(images, ConversionLossPolicy.Allow));
            else XisfWriter.Write(stream, images);
            stream.Position = 0;
            var assembly = typeof(IFileTypeDocumentFactory).Assembly;
            Factory = (IFileTypeDocumentFactory)Activator.CreateInstance(
                assembly.GetType("PaintDotNet.FileTypes.FileTypeDocumentFactory", throwOnError: true)!, nonPublic: true)!;
            var loadContext = (IFileTypeLoadContext)Activator.CreateInstance(
                assembly.GetType("PaintDotNet.FileTypes.FileTypeLoadContext", throwOnError: true)!,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [Factory, stream], null)!;
            context = (IDisposable)loadContext;
            Document = AstronomyDocument.Create(loadContext, Imaging,
                fits ? ScientificImageLoader.LoadFits(stream) : ScientificImageLoader.LoadXisf(stream));
        }

        public void Dispose()
        {
            Document.Dispose(); context.Dispose(); Factory.Dispose(); Imaging.Dispose(); stream.Dispose();
        }
    }
}
