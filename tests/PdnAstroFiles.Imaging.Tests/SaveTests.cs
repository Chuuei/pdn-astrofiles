using System.Xml.Linq;
using PdnAstroFiles.Imaging;
using XISFLib;

namespace PdnAstroFiles.Imaging.Tests;

public sealed class SaveTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EditedPixelsRetainAcquisitionAndCustomMetadataWithoutBeingStretchedAgain(bool fits, bool linear)
    {
        var source = linear ? TestImages.Linear() : TestImages.Developed();
        AddMetadata(source);
        var loaded = Load(source, fits);
        var metadata = AstronomyMetadata.Deserialize(loaded.Metadata!.Serialize());
        float[] pixels = TestImages.Pixels(loaded[0]);
        pixels[0] = 0.123f; pixels[1] = 0.456f; pixels[2] = 0.789f;
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, metadata.Format, metadata,
            [new(metadata.Images[0].Id, loaded[0].Width, loaded[0].Height, pixels)], true);
        Assert.True(saved.CanWrite);
        saved.Position = 0;
        var roundtrip = fits ? ScientificImageLoader.LoadFits(saved) : ScientificImageLoader.LoadXisf(saved);
        Assert.Equal(ImageStretch.None, roundtrip[0].Stretch);
        Assert.Equal(pixels, TestImages.Pixels(roundtrip[0]));
        saved.Position = 0;
        if (fits)
        {
            var document = FitsReader.Load(saved);
            var header = document.Hdus[0].Header;
            Assert.Equal("M42", header.GetRequiredValue<string>("OBJECT"));
            Assert.Equal(120, header.GetRequiredValue<double>("EXPTIME"));
            Assert.Equal("My telescope", header.GetRequiredValue<string>("TELESCOP"));
            Assert.Equal("2026-10-23T19:00:00", header.GetRequiredValue<string>("DATE-OBS"));
            Assert.Equal("Keep me", header.GetRequiredValue<string>("CUSTOM"));
            Assert.Equal(2.5, header.GetRequiredValue<double>("CRPIX1"));
            Assert.Null(header.FindFirst("DATAMAX"));
            Assert.Null(header.FindFirst("BUNIT"));
            Assert.True(document.Hdus[0].ChecksumValid);
            Assert.True(document.Hdus[0].DataChecksumValid);
        }
        else
        {
            var document = XisfReader.Load(saved);
            Assert.Equal("Global metadata", document.Properties["Custom:Global"].Value);
            Assert.Equal("Opaque content", document.Xml.Root!.Element(XName.Get("Extension", "urn:astrofiles-test"))!.Value);
            Assert.Equal("M42", document.Images[0].Properties["Observation:Object:Name"].Value);
            Assert.Equal("My telescope", document.Images[0].Properties["Instrument:Telescope:Name"].Value);
            Assert.Equal(new double[] { 2.5, 1.5 }, document.Images[0].Properties["AstrometricSolution:ReferenceImageCoordinates"].Value);
            Assert.Equal(new double[] { 11, 22, 33 }, document.Images[0].Properties["Custom:Vector"].Value);
            Assert.Null(document.Images[0].DisplayFunction);
            Assert.DoesNotContain(document.Images[0].FitsKeywords, card => card.Name is "BITPIX" or "NAXIS1" or "BSCALE");
            Assert.DoesNotContain("PCL:Median", document.Images[0].Properties.Keys);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void UnconfirmedOrChangedGeometryDropsAstrometryButKeepsTheTarget(bool fits, bool crop, bool confirmed)
    {
        var source = TestImages.Developed();
        AddMetadata(source);
        var loaded = Load(source, fits);
        var metadata = loaded.Metadata!;
        int height = crop ? 1 : loaded[0].Height;
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, metadata.Format, metadata,
            [new(metadata.Images[0].Id, loaded[0].Width, height, TestImages.Pixels(loaded[0])[..(loaded[0].Width * height * 4)])], confirmed);
        saved.Position = 0;
        if (fits)
        {
            var header = FitsReader.Load(saved).Hdus[0].Header;
            foreach (string keyword in new[] { "CRPIX1", "CRVAL1", "CTYPE1", "CD1_1", "CRPIX1A", "A_ORDER", "A_0_2" })
                Assert.Null(header.FindFirst(keyword));
            Assert.Equal("M42", header.GetRequiredValue<string>("OBJECT"));
            Assert.Equal(83.82, header.GetRequiredValue<double>("RA"));
        }
        else
        {
            var document = XisfReader.Load(saved);
            Assert.DoesNotContain(document.Images[0].Properties.Keys, key => key.StartsWith("AstrometricSolution:"));
            Assert.DoesNotContain(document.Images[0].FitsKeywords, card => card.Name.StartsWith("CRPIX"));
            Assert.Equal("M42", document.Images[0].Properties["Observation:Object:Name"].Value);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("flip")]
    [InlineData("90")]
    [InlineData("90;flip")]
    [InlineData("-90")]
    [InlineData("-90;flip")]
    [InlineData("180")]
    [InlineData("180;flip")]
    public void ConfirmedGeometryPreservesOriginalStoredCoordinatesAndDisplayOrientation(string orientation)
    {
        var source = TestImages.Developed();
        AddMetadata(source);
        source.Orientation = orientation;
        var loaded = Load(source, false);
        float[] pixels = TestImages.Pixels(loaded[0]);
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, AstronomyFormat.Xisf, loaded.Metadata!,
            [new(loaded.Metadata!.Images[0].Id, loaded[0].Width, loaded[0].Height, pixels)], true);
        saved.Position = 0;
        var document = XisfReader.Load(saved);
        Assert.Equal(orientation, document.Images[0].Orientation);
        Assert.Equal(source.Dimensions, document.Images[0].Dimensions);
        Assert.Equal(source.GetSamples<float>(), document.Images[0].GetSamples<float>());
        saved.Position = 0;
        Assert.Equal(pixels, TestImages.Pixels(ScientificImageLoader.LoadXisf(saved)[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DebayeredDataDoesNotCarryAnActiveBayerPatternOrOldDisplayFunction(bool fits)
    {
        var source = XisfImageData.FromSamples(new float[] { 0.8f, 0.4f, 0.4f, 0.2f }, [2, 2], 1);
        source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
        source.DisplayFunction = TestImages.Stored();
        var loaded = Load(source, fits);
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, loaded.Metadata!.Format, loaded.Metadata,
            [new(loaded.Metadata.Images[0].Id, 2, 2, TestImages.Pixels(loaded[0]))], true);
        saved.Position = 0;
        var roundtrip = fits ? ScientificImageLoader.LoadFits(saved) : ScientificImageLoader.LoadXisf(saved);
        Assert.Null(roundtrip[0].BayerPattern);
        Assert.Equal(TestImages.Pixels(loaded[0]), TestImages.Pixels(roundtrip[0]));
        saved.Position = 0;
        if (fits) Assert.Null(FitsReader.Load(saved).Hdus[0].Header.FindFirst("BAYERPAT"));
        else Assert.Null(XisfReader.Load(saved).Images[0].ColorFilterArray);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrayscaleAlphaRoundtrips(bool fits)
    {
        var source = XisfImageData.FromSamples(new float[] { 0.1f, 0.9f, 0.2f, 0.8f, 1f, 0.5f, 0f, 1f }, [2, 2], 2);
        // FITS has no grayscale-alpha convention; start with XISF and explicitly
        // create a FITS origin to exercise the writer's RGB-alpha representation.
        var loaded = Load(source, false);
        var metadata = loaded.Metadata!;
        if (fits)
        {
            var fitsSource = FitsConverter.FromXisf([TestImages.Developed()]);
            using var stream = new MemoryStream();
            FitsWriter.Write(stream, fitsSource); stream.Position = 0;
            metadata = ((ScientificImageSet)ScientificImageLoader.LoadFits(stream)).Metadata!;
        }
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, metadata.Format, metadata,
            [new(metadata.Images[0].Id, 2, 2, TestImages.Pixels(loaded[0]))], false);
        saved.Position = 0;
        Assert.Equal(TestImages.Pixels(loaded[0]), TestImages.Pixels((fits ? ScientificImageLoader.LoadFits(saved) : ScientificImageLoader.LoadXisf(saved))[0]));
    }

    [Fact]
    public void FitsNonImageExtensionsSurviveTheSave()
    {
        var original = FitsConverter.FromXisf([TestImages.Developed()]);
        var opaque = new FitsHdu(FitsHduType.Unknown, new FitsHeader(new FitsHeaderCard[]
        {
            new("XTENSION", "PRIVATE"), new("BITPIX", 8), new("NAXIS", 1), new("NAXIS1", 3),
            new("PCOUNT", 0), new("GCOUNT", 1), new("CUSTOM", "Original extension")
        }), [10, 20, 30]);
        using var input = new MemoryStream();
        FitsWriter.Write(input, new FitsDocument([original.Hdus[0], opaque])); input.Position = 0;
        var loaded = (ScientificImageSet)ScientificImageLoader.LoadFits(input);
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, AstronomyFormat.Fits, loaded.Metadata!,
            [new(loaded.Metadata!.Images[0].Id, 4, 2, TestImages.Pixels(loaded[0]))], false);
        saved.Position = 0;
        var document = FitsReader.Load(saved);
        Assert.Equal(2, document.Hdus.Count);
        Assert.Equal(new byte[] { 10, 20, 30 }, document.Hdus[1].Data.ToArray());
        Assert.Equal("Original extension", document.Hdus[1].Header.GetRequiredValue<string>("CUSTOM"));
    }

    [Fact]
    public void XisfOpaqueBinaryMetadataIsNotLostOrReplacedWithEditedPixels()
    {
        using var original = new MemoryStream();
        XisfWriter.Write(original, [TestImages.Developed()]); original.Position = 0;
        var document = XisfReader.Load(original);
        XNamespace ns = document.Xml.Root!.Name.Namespace;
        XNamespace custom = "urn:astrofiles-test";
        var extended = document.EditXml(xml => xml.Root!.Add(new XElement(custom + "OpaqueBlock",
            new XAttribute("location", (string)xml.Root.Element(ns + "Image")!.Attribute("location")!))));
        using var input = new MemoryStream();
        XisfWriter.Write(input, extended); input.Position = 0;
        var loaded = (ScientificImageSet)ScientificImageLoader.LoadXisf(input);
        var pixels = TestImages.Pixels(loaded[0]); pixels[0] = pixels[1] = pixels[2] = 0.42f;
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, AstronomyFormat.Xisf, loaded.Metadata!,
            [new(loaded.Metadata!.Images[0].Id, 4, 2, pixels)], false);
        saved.Position = 0;
        var result = XisfReader.Load(saved);
        string[] location = ((string)result.Xml.Root!.Element(custom + "OpaqueBlock")!.Attribute("location")!).Split(':');
        byte[] data = saved.ToArray().AsSpan(int.Parse(location[1]), int.Parse(location[2])).ToArray();
        Assert.Equal(TestImages.Developed().RawData.ToArray(), data);
        Assert.Equal(0.42f, result.Images[0].GetSamples<float>()[0]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CrossFormatSavingRetainsEditedPixelsAndCompatibleMetadata(bool fitsSource, bool confirmed)
    {
        var source = TestImages.Developed();
        AddMetadata(source);
        var loaded = Load(source, fitsSource);
        float[] pixels = TestImages.Pixels(loaded[0]);
        pixels[0] = 0.123f; pixels[1] = 0.456f; pixels[2] = 0.789f; pixels[3] = 0.5f;
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, fitsSource ? AstronomyFormat.Xisf : AstronomyFormat.Fits, loaded.Metadata!,
            [new(loaded.Metadata!.Images[0].Id, 4, 2, pixels)], confirmed);
        saved.Position = 0;
        var result = fitsSource ? ScientificImageLoader.LoadXisf(saved) : ScientificImageLoader.LoadFits(saved);
        Assert.Equal(ImageStretch.None, result[0].Stretch);
        Assert.Equal(pixels, TestImages.Pixels(result[0]));
        saved.Position = 0;
        if (fitsSource)
        {
            var image = XisfReader.Load(saved).Images[0];
            Assert.Equal("M42", image.Properties["Observation:Object:Name"].Value);
            Assert.Equal("My telescope", image.Properties["Instrument:Telescope:Name"].Value);
            Assert.Equal(120f, image.Properties["Instrument:ExposureTime"].Value);
            Assert.Equal(confirmed, image.FitsKeywords.Any(k => k.Name == "CRPIX1"));
            Assert.Equal("Keep me", image.FitsKeywords.Single(k => k.Name == "CUSTOM").Value.Trim('\''));
        }
        else
        {
            var header = FitsReader.Load(saved).Hdus[0].Header;
            Assert.Equal("M42", header.GetRequiredValue<string>("OBJECT"));
            Assert.Equal(confirmed, header.FindFirst("CRPIX1") is not null);
            Assert.Equal("Keep me", header.GetRequiredValue<string>("CUSTOM"));
            Assert.Contains(header, card => card.Keyword == "HISTORY" && card.Value?.ToString()?.Contains("conversion:") == true);
        }
    }

    [Fact]
    public void NativeXisfAcquisitionPropertiesAreMappedToFitsIncludingUnitsAndUtcTime()
    {
        var source = TestImages.Developed();
        source.Properties["Observation:Object:Name"] = "Native target";
        source.Properties.Set("Observation:Time:Start", new DateTimeOffset(2026, 10, 24, 20, 30, 0, TimeSpan.FromHours(2)));
        source.Properties.Set("Instrument:ExposureTime", 120f);
        source.Properties["Instrument:Telescope:Name"] = "Native telescope";
        source.Properties["Instrument:Camera:Name"] = "Native camera";
        source.Properties["Instrument:Filter:Name"] = "Ha";
        source.Properties.Set("Instrument:Telescope:FocalLength", 0.8f);
        source.Properties.Set("Instrument:Camera:XBinning", 2);
        source.Properties.Set("Instrument:Sensor:Temperature", -10f);
        var loaded = Load(source, false);
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, AstronomyFormat.Fits, loaded.Metadata!,
            [new(loaded.Metadata!.Images[0].Id, 4, 2, TestImages.Pixels(loaded[0]))], false);
        saved.Position = 0;
        var header = FitsReader.Load(saved).Hdus[0].Header;
        Assert.Equal("Native target", header.GetRequiredValue<string>("OBJECT"));
        Assert.Equal("2026-10-24T18:30:00.0000000", header.GetRequiredValue<string>("DATE-OBS"));
        Assert.Equal("Native telescope", header.GetRequiredValue<string>("TELESCOP"));
        Assert.Equal("Native camera", header.GetRequiredValue<string>("INSTRUME"));
        Assert.Equal("Ha", header.GetRequiredValue<string>("FILTER"));
        Assert.Equal(120d, header.GetRequiredValue<double>("EXPTIME"));
        Assert.Equal(800d, header.GetRequiredValue<double>("FOCALLEN"), 3);
        Assert.Equal(-10d, header.GetRequiredValue<double>("CCD-TEMP"));
        Assert.Equal(2, header.GetRequiredValue<int>("XBINNING"));
    }

    [Theory]
    [InlineData("flip")]
    [InlineData("90")]
    [InlineData("90;flip")]
    [InlineData("-90")]
    [InlineData("-90;flip")]
    [InlineData("180")]
    [InlineData("180;flip")]
    public void XisfOrientationIsBakedIntoFitsWithoutLeavingIncorrectAstrometry(string orientation)
    {
        var source = TestImages.Developed(); AddMetadata(source); source.Orientation = orientation;
        var loaded = Load(source, false);
        float[] pixels = TestImages.Pixels(loaded[0]);
        using var saved = new MemoryStream();
        AstronomyImageWriter.Save(saved, AstronomyFormat.Fits, loaded.Metadata!,
            [new(loaded.Metadata!.Images[0].Id, loaded[0].Width, loaded[0].Height, pixels)], true);
        saved.Position = 0;
        var display = ScientificImageLoader.LoadFits(saved)[0];
        Assert.Equal((loaded[0].Width, loaded[0].Height), (display.Width, display.Height));
        Assert.Equal(pixels, TestImages.Pixels(display));
        saved.Position = 0;
        Assert.Null(FitsReader.Load(saved).Hdus[0].Header.FindFirst("CRPIX1"));
    }

    private static ScientificImageSet Load(XisfImageData source, bool fits)
    {
        using var stream = new MemoryStream();
        if (fits) FitsWriter.Write(stream, FitsConverter.FromXisf([source], ConversionLossPolicy.Allow));
        else
        {
            var document = new XisfDocumentData();
            document.Images.Add(source);
            document.Properties["Custom:Global"] = "Global metadata";
            document.Extensions.Add(new XElement(XName.Get("Extension", "urn:astrofiles-test"), "Opaque content"));
            XisfWriter.Write(stream, document);
        }
        stream.Position = 0;
        var result = (ScientificImageSet)(fits ? ScientificImageLoader.LoadFits(stream) : ScientificImageLoader.LoadXisf(stream));
        Assert.Null(result.MetadataError);
        Assert.NotNull(result.Metadata);
        return result;
    }

    private static void AddMetadata(XisfImageData image)
    {
        image.Properties["Observation:Object:Name"] = "M42";
        image.Properties["Instrument:Telescope:Name"] = "My telescope";
        image.Properties["AstrometricSolution:ReferenceImageCoordinates"] = new double[] { 2.5, 1.5 };
        image.Properties["Custom:Vector"] = new double[] { 11, 22, 33 };
        image.Properties["PCL:Median"] = new double[] { 0.2 };
        foreach (var (name, value) in new (string, object)[]
        {
            ("OBJECT", "M42"), ("EXPTIME", 120d), ("TELESCOP", "My telescope"), ("DATE-OBS", "2026-10-23T19:00:00"),
            ("CUSTOM", "Keep me"), ("CRPIX1", 2.5), ("CRVAL1", 83.82), ("CTYPE1", "RA---TAN-SIP"),
            ("CD1_1", 0.001), ("CRPIX1A", 2.5), ("A_ORDER", 2), ("A_0_2", 0.001), ("RA", 83.82),
            ("DATAMAX", 65535), ("BUNIT", "ADU"), ("BITPIX", 16), ("NAXIS1", 100), ("BSCALE", 2)
        }) image.FitsKeywords.Add(XisfFitsKeyword.FromValue(name, value));
    }
}
