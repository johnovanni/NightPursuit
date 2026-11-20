using System.Windows;
using NightPursuit.App.Rendering;

namespace NightPursuit.App.Screens;

/// <summary>The screens' resources. The brushes are the HUD's own frozen <see cref="Neon"/> brushes, so the screens and the HUD always match.</summary>
public partial class NeonStyles : ResourceDictionary
{
    /// <summary>The keys of the four text sizes (doubles at 100% in the XAML); <see cref="ScreenHost"/> scales them by the text-size setting.</summary>
    public static readonly string[] TextSizeKeys = { "TextSmall", "TextBody", "TextHeading", "TextTitle" };

    public NeonStyles()
    {
        InitializeComponent();
        this["TextAmber"] = Neon.TextAmber;
        this["TextCyan"] = Neon.TextCyan;
        this["TextWhite"] = Neon.TextWhite;
        this["TextGray"] = Neon.TextGray;
        this["TextGreen"] = Neon.TextGreen;
        this["TextRed"] = Neon.TextRed;
    }
}
