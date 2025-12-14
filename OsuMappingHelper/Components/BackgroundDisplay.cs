using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace OsuMappingHelper.Components;

/// <summary>
/// Full-screen background display that shows the current beatmap's background image.
/// </summary>
public partial class BackgroundDisplay : CompositeDrawable
{
    private Sprite _backgroundSprite = null!;
    private Box _dimOverlay = null!;

    [Resolved]
    private IRenderer Renderer { get; set; } = null!;

    private string? _currentBackgroundPath;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren = new Drawable[]
        {
            // Dark fallback background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(20, 18, 25, 255)
            },
            // Beatmap background image (16:9 fill)
            _backgroundSprite = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fill,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Alpha = 0
            },
            // Dim overlay for UI readability
            _dimOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 200),
                Alpha = 0
            }
        };
    }

    public void LoadBackground(string? backgroundPath)
    {
        if (string.IsNullOrEmpty(backgroundPath) || !File.Exists(backgroundPath))
        {
            ClearBackground();
            return;
        }

        // Don't reload if same background
        if (_currentBackgroundPath == backgroundPath)
            return;

        _currentBackgroundPath = backgroundPath;

        try
        {
            // Load texture from file using ImageSharp
            // Note: Do NOT dispose the image - TextureUpload takes ownership and disposes it
            using var stream = File.OpenRead(backgroundPath);
            var image = Image.Load<Rgba32>(stream);
            
            var texture = Renderer.CreateTexture(image.Width, image.Height);
            // TextureUpload takes ownership of the image and will dispose it after upload
            texture.SetData(new TextureUpload(image));

            Schedule(() =>
            {
                _backgroundSprite.Texture = texture;
                _backgroundSprite.FadeTo(1, 300, Easing.OutQuint);
                _dimOverlay.FadeTo(1, 300, Easing.OutQuint);
            });
        }
        catch
        {
            // Failed to load background, use default
            ClearBackground();
        }
    }

    public void ClearBackground()
    {
        _currentBackgroundPath = null;
        _backgroundSprite.FadeTo(0, 300, Easing.OutQuint);
        _dimOverlay.FadeTo(0, 300, Easing.OutQuint);
    }
}
