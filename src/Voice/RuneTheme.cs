using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace Rune.Voice;

internal enum RuneButtonTone { Neutral, Selected, Primary, Danger, Navigation, NavigationSelected }

internal static class RuneTheme
{
    public static readonly Color Coal = Color.FromArgb(18, 18, 16);
    public static readonly Color Stone = Color.FromArgb(29, 29, 26);
    public static readonly Color Panel = Color.FromArgb(39, 37, 32);
    public static readonly Color PanelLight = Color.FromArgb(49, 46, 39);
    public static readonly Color Iron = Color.FromArgb(91, 82, 67);
    public static readonly Color Amber = Color.FromArgb(202, 143, 55);
    public static readonly Color AmberSoft = Color.FromArgb(235, 203, 144);
    public static readonly Color Moss = Color.FromArgb(104, 125, 70);
    public static readonly Color MossDark = Color.FromArgb(54, 67, 43);
    public static readonly Color Bone = Color.FromArgb(229, 218, 195);
    public static readonly Color Muted = Color.FromArgb(174, 164, 143);
    public static readonly Color Good = Color.FromArgb(145, 190, 91);
    private static Image? texture;
    private static Image? buttonTexture;
    private static Image? navigationTexture;
    private static readonly ConditionalWeakTable<Button, ButtonSkin> buttonSkins = new();
    private static Bitmap? renderedBackground;
    internal static int LiveButtonImages => buttonSkins.Count(pair => pair.Value.Image != null);
    private sealed class ButtonSkin
    {
        public Size Size;
        public RuneButtonTone Tone;
        public Image? Image;
    }

    public static Image LoadArtwork(string path)
    {
        using var source = Image.FromFile(path);
        return new Bitmap(source); // Detach the decoded bitmap so PNG files are not held open.
    }
    public static Image? Texture
    {
        get
        {
            if (texture != null) return texture; string path = Path.Combine(AppContext.BaseDirectory, "Assets", "ui-background.png");
            if (File.Exists(path)) texture = LoadArtwork(path); return texture;
        }
    }

    private static Image? ButtonTexture
    {
        get
        {
            if (buttonTexture != null) return buttonTexture;
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "button-background.png");
            if (File.Exists(path)) buttonTexture = LoadArtwork(path);
            return buttonTexture;
        }
    }

    private static Image? NavigationTexture
    {
        get
        {
            if (navigationTexture != null) return navigationTexture;
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "nav-button-background.png");
            if (File.Exists(path)) navigationTexture = LoadArtwork(path);
            return navigationTexture;
        }
    }

    public static void DrawSurface(Graphics graphics, Rectangle destination, bool sidebar = false, int shade = 38)
    {
        var image = Texture; if (image == null || destination.Width <= 0 || destination.Height <= 0) { graphics.Clear(sidebar ? Coal : Stone); return; }
        int split = (int)(image.Width * .24f); var region = sidebar ? new Rectangle(0, 0, split, image.Height) : new Rectangle(split, 0, image.Width - split, image.Height);
        var source = CoverSource(region, destination.Size);
        graphics.DrawImage(image, destination, source, GraphicsUnit.Pixel); using var veil = new SolidBrush(Color.FromArgb(shade, 0, 0, 0)); graphics.FillRectangle(veil, destination);
    }

    public static void DrawFullSurface(Graphics graphics, Rectangle destination)
    {
        var image = Texture;
        if (image == null || destination.Width <= 0 || destination.Height <= 0) { graphics.Clear(Coal); return; }
        if (renderedBackground == null || renderedBackground.Size != destination.Size)
        {
            var next = new Bitmap(destination.Width, destination.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var canvas = Graphics.FromImage(next))
            {
                var source = CoverSource(new Rectangle(0, 0, image.Width, image.Height), destination.Size);
                canvas.DrawImage(image, new Rectangle(Point.Empty, destination.Size), source, GraphicsUnit.Pixel);
            }
            var previous = renderedBackground; renderedBackground = next; previous?.Dispose();
        }
        graphics.DrawImageUnscaled(renderedBackground, destination.Location);
    }

    // Keep layout guides attached to the same artwork crop used by the renderer.
    public static Rectangle ArtworkBounds(Size window, Rectangle reference)
    {
        var image = Texture;
        if (image == null) return reference;
        var original = CoverSource(new Rectangle(0, 0, image.Width, image.Height), new Size(1360, 850));
        var current = CoverSource(new Rectangle(0, 0, image.Width, image.Height), window);
        float scaleX = window.Width / (float)current.Width, scaleY = window.Height / (float)current.Height;
        float x = original.X + reference.X * original.Width / 1360f;
        float y = original.Y + reference.Y * original.Height / 850f;
        return new Rectangle((int)Math.Round((x - current.X) * scaleX), (int)Math.Round((y - current.Y) * scaleY),
            (int)Math.Round(reference.Width * original.Width / 1360f * scaleX),
            (int)Math.Round(reference.Height * original.Height / 850f * scaleY));
    }

    public static void Shade(Graphics graphics, Rectangle destination, int opacity)
    {
        using var shade = new SolidBrush(Color.FromArgb(opacity, 0, 0, 0));
        graphics.FillRectangle(shade, destination);
    }

    private static Rectangle CoverSource(Rectangle region, Size destination)
    {
        float sourceRatio = region.Width / (float)region.Height;
        float destinationRatio = destination.Width / (float)destination.Height;
        if (destinationRatio > sourceRatio)
        {
            int height = Math.Max(1, (int)Math.Round(region.Width / destinationRatio));
            return new Rectangle(region.X, region.Y + (region.Height - height) / 2, region.Width, height);
        }

        int width = Math.Max(1, (int)Math.Round(region.Height * destinationRatio));
        return new Rectangle(region.X + (region.Width - width) / 2, region.Y, width, region.Height);
    }

    public static Button Button(string text, bool primary = false, bool danger = false)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 38, Padding = new Padding(14, 3, 14, 3), FlatStyle = FlatStyle.Flat, BackColor = primary ? MossDark : PanelLight, ForeColor = Bone, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
        var tone = danger ? RuneButtonTone.Danger : primary ? RuneButtonTone.Primary : RuneButtonTone.Neutral;
        button.FlatAppearance.BorderColor = danger ? Color.FromArgb(164, 62, 49) : primary ? Moss : Iron;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(67, 83, 50) : Color.FromArgb(60, 56, 47);
        SkinButton(button, tone);
        return button;
    }

    private static void SkinButton(Button button, RuneButtonTone tone)
    {
        SetButtonTone(button, tone);
    }

    public static void SetButtonTone(Button button, RuneButtonTone tone)
    {
        if (!buttonSkins.TryGetValue(button, out var skin))
        {
            skin = new ButtonSkin(); buttonSkins.Add(button, skin);
            button.SizeChanged += (_, _) => SetButtonTone(button, skin.Tone);
            button.Disposed += (_, _) => { skin.Image?.Dispose(); skin.Image = null; buttonSkins.Remove(button); };
        }
        if (skin.Image != null && skin.Size == button.ClientSize && skin.Tone == tone) return;
        var oldImage = skin.Image;
        skin.Size = button.ClientSize; skin.Tone = tone;
        skin.Image = ButtonBackground(button.ClientSize, tone);
        button.UseVisualStyleBackColor = false;
        button.BackgroundImageLayout = ImageLayout.None;
        button.BackgroundImage = skin.Image;
        oldImage?.Dispose();
        button.FlatAppearance.BorderColor = tone switch
        {
            RuneButtonTone.Selected or RuneButtonTone.NavigationSelected => Amber,
            RuneButtonTone.Primary => Moss,
            RuneButtonTone.Danger => Color.FromArgb(170, 67, 52),
            _ => Iron
        };
        button.FlatAppearance.BorderSize = tone switch
        {
            RuneButtonTone.Navigation => 1,
            RuneButtonTone.NavigationSelected => 1,
            RuneButtonTone.Selected => 2,
            _ => 1
        };
    }

    private static Image? ButtonBackground(Size size, RuneButtonTone tone)
    {
        bool navigation = tone is RuneButtonTone.Navigation or RuneButtonTone.NavigationSelected;
        var source = navigation ? NavigationTexture : ButtonTexture;
        if (source == null || size.Width < 2 || size.Height < 2) return null;

        var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        DrawNineSlice(graphics, source, new Rectangle(Point.Empty, size), navigation);
        Color tint = tone switch
        {
            RuneButtonTone.Selected => Color.FromArgb(38, 180, 111, 26),
            RuneButtonTone.NavigationSelected => Color.FromArgb(42, 157, 96, 21),
            RuneButtonTone.Primary => Color.FromArgb(72, 55, 96, 32),
            RuneButtonTone.Danger => Color.FromArgb(105, 125, 27, 20),
            RuneButtonTone.Navigation => Color.FromArgb(54, 7, 7, 6),
            _ => Color.FromArgb(48, 12, 12, 10)
        };
        using var veil = new SolidBrush(tint);
        graphics.FillRectangle(veil, bitmap.Width > 4 ? 2 : 0, bitmap.Height > 4 ? 2 : 0, Math.Max(1, bitmap.Width - 4), Math.Max(1, bitmap.Height - 4));
        return bitmap;
    }

    private static void DrawNineSlice(Graphics graphics, Image image, Rectangle target, bool navigation)
    {
        int sx = Math.Min(navigation ? 40 : 150, image.Width / 4), sy = Math.Min(navigation ? 28 : 105, image.Height / 4);
        int dx = Math.Min(navigation ? 6 : 16, target.Width / 3), dy = Math.Min(navigation ? 5 : 11, target.Height / 3);
        int[] sourceX = { 0, sx, image.Width - sx, image.Width };
        int[] sourceY = { 0, sy, image.Height - sy, image.Height };
        int[] targetX = { target.Left, target.Left + dx, target.Right - dx, target.Right };
        int[] targetY = { target.Top, target.Top + dy, target.Bottom - dy, target.Bottom };
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
                graphics.DrawImage(image,
                    Rectangle.FromLTRB(targetX[column], targetY[row], targetX[column + 1], targetY[row + 1]),
                    Rectangle.FromLTRB(sourceX[column], sourceY[row], sourceX[column + 1], sourceY[row + 1]),
                    GraphicsUnit.Pixel);
    }

    public static Label Label(string text, float size = 9, bool heading = false) => new()
    {
        Text = text, AutoSize = true, BackColor = Color.Transparent, ForeColor = heading ? AmberSoft : Bone,
        Font = new Font(heading ? "Georgia" : "Segoe UI", size, heading ? FontStyle.Bold : FontStyle.Regular)
    };

    public static void Field(Control control)
    {
        control.BackColor = Color.FromArgb(24, 24, 21);
        control.ForeColor = Bone;
        control.Font = new Font("Segoe UI", 10);
        if (control is TextBox text) text.BorderStyle = BorderStyle.FixedSingle;
        if (control is ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using var fill = new SolidBrush(selected ? PanelLight : Color.FromArgb(24, 24, 21));
                e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? "", combo.Font, e.Bounds, Bone,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }
    }
}

internal sealed class RuneCheckBox : CheckBox
{
    public RuneCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = RuneTheme.Bone;
        Cursor = Cursors.Hand;
        Padding = new Padding(1);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPrefix);
        return new Size(text.Width + 24 + Padding.Horizontal, Math.Max(19, text.Height) + Padding.Vertical);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int box = 14;
        int y = Math.Max(1, (Height - box) / 2);
        var square = new Rectangle(1, y, box, box);
        using var fill = new SolidBrush(Checked ? RuneTheme.MossDark : Color.FromArgb(23, 23, 20));
        using var border = new Pen(Checked ? RuneTheme.Amber : RuneTheme.Iron, 1.2f);
        e.Graphics.FillRectangle(fill, square);
        e.Graphics.DrawRectangle(border, square);
        if (Checked)
        {
            using var mark = new Pen(RuneTheme.AmberSoft, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLines(mark, new[] { new Point(4, y + 7), new Point(7, y + 10), new Point(13, y + 4) });
        }
        var textBounds = new Rectangle(21, 0, Math.Max(0, Width - 21), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : RuneTheme.Iron,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal enum RuneWindowGlyph { Minimize, Maximize, Close }

internal sealed class RuneWindowControl : Control
{
    private bool hovered;
    private bool pressed;
    public RuneWindowGlyph Glyph { get; }

    public RuneWindowControl(RuneWindowGlyph glyph)
    {
        Glyph = glyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(42, 29);
        Margin = Padding.Empty;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (hovered)
        {
            var bounds = new Rectangle(5, 3 + (pressed ? 1 : 0), Width - 10, Height - 7);
            using var path = RoundedRectangle(bounds, 6);
            Color top = Glyph == RuneWindowGlyph.Close ? Color.FromArgb(150, 112, 38, 28) : Color.FromArgb(145, 91, 70, 40);
            Color bottom = Glyph == RuneWindowGlyph.Close ? Color.FromArgb(185, 66, 22, 19) : Color.FromArgb(175, 44, 38, 29);
            if (pressed) { top = ControlPaint.Dark(top, .18f); bottom = ControlPaint.Dark(bottom, .18f); }
            using var hover = new LinearGradientBrush(bounds, top, bottom, LinearGradientMode.Vertical);
            using var outline = new Pen(Glyph == RuneWindowGlyph.Close ? Color.FromArgb(205, 172, 72, 55) : RuneTheme.Amber, 1f);
            e.Graphics.FillPath(hover, path);
            e.Graphics.DrawPath(outline, path);
        }
        using var pen = new Pen(hovered ? RuneTheme.Bone : RuneTheme.AmberSoft, hovered ? 1.55f : 1.15f);
        int cx = Width / 2, cy = Height / 2 + (pressed ? 1 : 0);
        if (Glyph == RuneWindowGlyph.Minimize) e.Graphics.DrawLine(pen, cx - 6, cy + 4, cx + 6, cy + 4);
        else if (Glyph == RuneWindowGlyph.Maximize) e.Graphics.DrawRectangle(pen, cx - 5, cy - 5, 10, 10);
        else
        {
            e.Graphics.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            e.Graphics.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RunePanel : Panel
{
    public bool Accent { get; set; }
    public RunePanel() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; Padding = new Padding(1); }
    protected override void OnPaintBackground(PaintEventArgs e) { base.OnPaintBackground(e); RuneTheme.Shade(e.Graphics, ClientRectangle, 70); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!Accent) return;
        using var pen = new Pen(RuneTheme.Amber, 2);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class RuneBackdrop : Panel
{
    public RuneBackdrop() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e); RuneTheme.Shade(e.Graphics, ClientRectangle, 18);
    }
}

internal sealed class RuneSurfacePanel : Panel
{
    public RuneSurfacePanel() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaintBackground(PaintEventArgs e) { base.OnPaintBackground(e); RuneTheme.Shade(e.Graphics, ClientRectangle, 38); }
}

internal sealed class RuneSurfaceTable : TableLayoutPanel
{
    public RuneSurfaceTable() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaintBackground(PaintEventArgs e) { base.OnPaintBackground(e); RuneTheme.Shade(e.Graphics, ClientRectangle, 48); }
}
