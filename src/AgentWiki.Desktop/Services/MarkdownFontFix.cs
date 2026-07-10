using System.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;
using Markdown.Avalonia;
using Markdown.Avalonia.StyleCollections;

namespace AgentWiki.Desktop.Services;

/// <summary>
/// Markdown.Avalonia FluentTheme embeds mono stacks that include Consolas and
/// FontWeight.DemiBold. Both can throw on macOS during glyph typeface creation.
/// We clone the theme and force safe mono + SemiBold weights.
/// </summary>
public static class MarkdownFontFix
{
    private static readonly FontFamily SafeMono = AppFonts.Mono;

    public static void Apply(MarkdownScrollViewer? viewer)
    {
        if (viewer is null)
        {
            return;
        }

        try
        {
            viewer.MarkdownStyle = CreateSanitizedFluentStyle();
            viewer.SetValue(CCode.MonospaceFontFamilyProperty, SafeMono);
            SanitizeVisualTree(viewer);
        }
        catch
        {
            // Best-effort; never take down the host.
        }
    }

    public static void SanitizeVisualTree(Control root)
    {
        try
        {
            foreach (var ctext in FlattenVisual(root).OfType<CTextBlock>())
            {
                SanitizeInlines(ctext.Content);
                // Table headers use DemiBold in the package theme.
                if (ctext.FontWeight is FontWeight.DemiBold or FontWeight.ExtraBold)
                {
                    ctext.FontWeight = FontWeight.SemiBold;
                }
            }

            foreach (var text in FlattenVisual(root).OfType<TextBlock>())
            {
                if (text.Classes.Contains("CodeBlock") || FontLooksUnsafe(text.FontFamily))
                {
                    text.FontFamily = SafeMono;
                }

                if (text.FontWeight is FontWeight.DemiBold)
                {
                    text.FontWeight = FontWeight.SemiBold;
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static Styles CreateSanitizedFluentStyle()
    {
        var source = new MarkdownStyleFluentTheme();
        var result = new Styles();

        foreach (var style in source)
        {
            if (style is not Style s)
            {
                result.Add(style);
                continue;
            }

            var clone = new Style { Selector = s.Selector };

            foreach (var setterBase in s.Setters)
            {
                if (setterBase is not Setter setter || setter.Property is null)
                {
                    clone.Setters.Add(setterBase);
                    continue;
                }

                // Mono faces
                if (setter.Property == TextBlock.FontFamilyProperty
                    || setter.Property == CTextBlock.FontFamilyProperty
                    || setter.Property == CInline.FontFamilyProperty
                    || setter.Property == CCode.MonospaceFontFamilyProperty)
                {
                    if (setter.Property == CCode.MonospaceFontFamilyProperty)
                    {
                        clone.Setters.Add(new Setter(CCode.MonospaceFontFamilyProperty, SafeMono));
                    }
                    else if (setter.Property == CTextBlock.FontFamilyProperty)
                    {
                        // Body text: drop custom family (platform default).
                        continue;
                    }
                    else if (IsCodeSelector(s))
                    {
                        clone.Setters.Add(new Setter(setter.Property, SafeMono));
                    }
                    else if (FontLooksUnsafe(AsFontFamily(setter.Value)))
                    {
                        clone.Setters.Add(new Setter(setter.Property, SafeMono));
                    }

                    continue;
                }

                // DemiBold is not always available; SemiBold is safer across faces.
                if (setter.Property == TextBlock.FontWeightProperty
                    || setter.Property == CTextBlock.FontWeightProperty
                    || setter.Property == CInline.FontWeightProperty)
                {
                    if (setter.Value is FontWeight.DemiBold)
                    {
                        clone.Setters.Add(new Setter(setter.Property, FontWeight.SemiBold));
                        continue;
                    }
                }

                clone.Setters.Add(setterBase);
            }

            if (IsCodeSelector(s))
            {
                clone.Setters.Add(new Setter(TextBlock.FontFamilyProperty, SafeMono));
                clone.Setters.Add(new Setter(CCode.MonospaceFontFamilyProperty, SafeMono));
            }

            result.Add(clone);
        }

        result.Add(new Style(x => x.OfType<CCode>())
        {
            Setters =
            {
                new Setter(CCode.MonospaceFontFamilyProperty, SafeMono),
                new Setter(CCode.FontFamilyProperty, SafeMono)
            }
        });
        result.Add(new Style(x => x.OfType<TextBlock>().Class("CodeBlock"))
        {
            Setters = { new Setter(TextBlock.FontFamilyProperty, SafeMono) }
        });

        return result;
    }

    private static bool IsCodeSelector(Style s)
    {
        var text = s.Selector?.ToString() ?? "";
        return text.Contains("CCode", StringComparison.Ordinal)
               || text.Contains("CodeBlock", StringComparison.Ordinal);
    }

    private static void SanitizeInlines(IEnumerable? inlines)
    {
        if (inlines is null)
        {
            return;
        }

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case CCode code:
                    code.MonospaceFontFamily = SafeMono;
                    code.FontFamily = SafeMono;
                    if (code.FontWeight is FontWeight.DemiBold)
                    {
                        code.FontWeight = FontWeight.SemiBold;
                    }

                    SanitizeInlines(code.Content);
                    break;
                case CSpan span:
                    if (FontLooksUnsafe(span.FontFamily))
                    {
                        span.FontFamily = SafeMono;
                    }

                    if (span.FontWeight is FontWeight.DemiBold)
                    {
                        span.FontWeight = FontWeight.SemiBold;
                    }

                    SanitizeInlines(span.Content);
                    break;
                case CInline other:
                    if (FontLooksUnsafe(other.FontFamily))
                    {
                        other.FontFamily = SafeMono;
                    }

                    if (other.FontWeight is FontWeight.DemiBold)
                    {
                        other.FontWeight = FontWeight.SemiBold;
                    }

                    break;
            }
        }
    }

    private static FontFamily? AsFontFamily(object? value) =>
        value switch
        {
            FontFamily ff => ff,
            string name when !string.IsNullOrWhiteSpace(name) => new FontFamily(name),
            _ => null
        };

    private static bool FontLooksUnsafe(FontFamily? family)
    {
        if (family is null)
        {
            return false;
        }

        var text = family.ToString();
        // Composite stacks and Windows-only Consolas are the usual crash sources on macOS.
        return text.Contains(',', StringComparison.Ordinal)
               || text.Contains("consolas", StringComparison.OrdinalIgnoreCase)
               || text.Contains("compositefont", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Control> FlattenVisual(Control root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            foreach (var d in FlattenVisual(child))
            {
                yield return d;
            }
        }
    }
}
