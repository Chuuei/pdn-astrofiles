using PaintDotNet;

namespace PdnAstroFiles;

public sealed class PluginSupportInfo : IPluginSupportInfo
{
    private static readonly Uri ProjectWebsite = new("https://github.com/Chuuei/pdn-astrofiles");

    public string DisplayName => "AstroFiles";

    public string Author => "Karst";

    public string Copyright => "Copyright 2026 Karsten Schlender and contributors";

    public Version Version => typeof(PluginSupportInfo).Assembly.GetName().Version!;

    public Uri? WebsiteUri => ProjectWebsite;
}
