using PaintDotNet.FileTypes;

namespace PdnAstroFiles;

internal sealed class AstronomySaveOptionsControl : FileTypeSaveOptionsControl
{
    private readonly CheckBox geometry = new() { Text = "Image geometry unchanged", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(330, 0) };
    private bool updating;

    public AstronomySaveOptionsControl()
    {
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        var layout = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Dock = DockStyle.Top, Padding = new Padding(8), Width = 350
        };
        layout.Controls.Add(new Label
        {
            Text = "Astronomy metadata", AutoSize = true, Margin = new Padding(3, 3, 3, 12)
        });
        layout.Controls.Add(geometry);
        layout.Controls.Add(new Label
        {
            Text = "Keep plate-solving / WCS metadata. Select only if you have not cropped, resized, rotated, flipped or distorted the image.",
            AutoSize = true, MaximumSize = new(330, 0), Margin = new Padding(3, 6, 3, 12)
        });
        layout.Controls.Add(status);
        layout.Controls.Add(new Label
        {
            Text = "Saved pixels include your edits and any stretch or debayering applied on import.",
            AutoSize = true, MaximumSize = new(330, 0), Margin = new Padding(3, 12, 3, 3)
        });
        Controls.Add(layout);
        SizeChanged += (_, _) =>
        {
            foreach (var label in layout.Controls.OfType<Label>())
                label.MaximumSize = new(Math.Max(100, ClientSize.Width - 32), 0);
        };
        geometry.CheckedChanged += (_, _) =>
        {
            if (updating) return;
            var options = Options is AstronomySaveOptions current ? (AstronomySaveOptions)current.Clone() : new AstronomySaveOptions();
            options.GeometryUnchanged = geometry.Checked;
            Options = options;
        };
        Options = new AstronomySaveOptions();
    }

    protected override void OnOptionsChanged()
    {
        if (!updating && Options is AstronomySaveOptions options)
        {
            updating = true;
            geometry.Enabled = options.CanPreserveGeometry;
            geometry.Checked = options.CanPreserveGeometry && options.GeometryUnchanged;
            options.GeometryUnchanged = geometry.Checked;
            status.Text = options.Status;
            updating = false;
        }
        base.OnOptionsChanged();
    }
}
