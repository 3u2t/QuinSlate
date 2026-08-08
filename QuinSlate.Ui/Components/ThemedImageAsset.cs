using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Keeps one <see cref="Image"/> showing whichever of a light/dark artwork pair matches the
/// resolved theme.
/// </summary>
/// <remarks>
/// A pair is needed because monochrome artwork cannot be recoloured by the framework:
/// <see cref="Image"/> and <c>ImageIcon</c> both ignore <c>Foreground</c>, and <c>BitmapIcon</c> —
/// the one icon element that does tint with <c>Foreground</c> — cannot read SVG. So the colour is
/// baked into each file and the right file is chosen at runtime.
/// </remarks>
public sealed class ThemedImageAsset
{
    private const string SvgExtension = ".svg";

    private readonly Image target;
    private readonly Uri lightThemeSource;
    private readonly Uri darkThemeSource;

    /// <summary>
    /// Binds <paramref name="target"/> to an artwork pair addressed by <c>ms-appx:</c> URI.
    /// </summary>
    /// <param name="target">The image element whose source this instance owns.</param>
    /// <param name="lightThemeAsset">Absolute URI of the artwork shown under the light theme.</param>
    /// <param name="darkThemeAsset">Absolute URI of the artwork shown under the dark theme.</param>
    /// <exception cref="ArgumentNullException">Any argument is null or empty.</exception>
    public ThemedImageAsset(Image target, string lightThemeAsset, string darkThemeAsset)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (string.IsNullOrEmpty(lightThemeAsset))
        {
            throw new ArgumentNullException(nameof(lightThemeAsset));
        }

        if (string.IsNullOrEmpty(darkThemeAsset))
        {
            throw new ArgumentNullException(nameof(darkThemeAsset));
        }

        this.target = target;
        lightThemeSource = new Uri(lightThemeAsset);
        darkThemeSource = new Uri(darkThemeAsset);
    }

    /// <summary>
    /// Applies the variant matching <paramref name="theme"/> to the bound image. Callers pass a
    /// resolved theme (<c>ActualTheme</c>), never <see cref="ElementTheme.Default"/>, which is
    /// treated as light.
    /// </summary>
    /// <param name="theme">The theme the artwork must read against.</param>
    public void Apply(ElementTheme theme)
    {
        Uri asset = theme == ElementTheme.Dark ? darkThemeSource : lightThemeSource;
        target.Source = CreateSource(asset);
    }

    /// <summary>
    /// Returns the image source class the file format requires. SVG decodes through
    /// <see cref="SvgImageSource"/>, which rasterises to whatever size layout gives the image, so
    /// the mark stays sharp at every DPI; other formats decode through <see cref="BitmapImage"/>.
    /// </summary>
    private static ImageSource CreateSource(Uri asset)
    {
        if (asset.AbsolutePath.EndsWith(SvgExtension, StringComparison.OrdinalIgnoreCase))
        {
            return new SvgImageSource(asset);
        }

        return new BitmapImage(asset);
    }
}
