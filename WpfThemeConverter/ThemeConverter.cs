// Copyright (c) Automint Ltd. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Text.RegularExpressions;

namespace WpfThemeConverter;

/// <summary>
/// Converts UWP Fluent Theme Editor ResourceDictionary exports to WPF-compatible Theme.xaml format.
/// </summary>
public partial class ThemeConverter
{
    private readonly bool _generateBrushes;

    // Statistics
    public int ColoursConverted { get; private set; }
    public int BrushesGenerated { get; private set; }
    public int UwpElementsRemoved { get; private set; }

    // Known ColorPaletteResources attributes (system colour targets)
    private static readonly string[] ColourPaletteAttributes =
    [
        "Accent", "AltHigh", "AltLow", "AltMedium", "AltMediumHigh", "AltMediumLow",
        "BaseHigh", "BaseLow", "BaseMedium", "BaseMediumHigh", "BaseMediumLow",
        "ChromeAltLow", "ChromeBlackHigh", "ChromeBlackLow", "ChromeBlackMedium", "ChromeBlackMediumLow",
        "ChromeDisabledHigh", "ChromeDisabledLow", "ChromeGray", "ChromeHigh", "ChromeLow",
        "ChromeMedium", "ChromeMediumLow", "ChromeWhite", "ErrorText", "ListLow", "ListMedium"
    ];

    public ThemeConverter(bool generateBrushes = true)
    {
        _generateBrushes = generateBrushes;
    }

    /// <summary>
    /// Converts UWP ResourceDictionary XAML to WPF-compatible format.
    /// </summary>
    public string Convert(string uwpXaml)
    {
        // Reset statistics
        ColoursConverted = 0;
        BrushesGenerated = 0;
        UwpElementsRemoved = 0;

        var result = new StringBuilder();

        // First pass: Fix the root ResourceDictionary by removing UWP namespaces
        var processedXaml = FixRootResourceDictionary(uwpXaml);

        // Second pass: Process line by line
        var lines = processedXaml.Split('\n');
        var inColorPaletteResources = false;
        var colourPaletteResourcesLine = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var processedLine = ProcessLine(line, ref inColorPaletteResources, ref colourPaletteResourcesLine);

            if (processedLine != null)
            {
                result.AppendLine(processedLine);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Fixes the root ResourceDictionary element by removing UWP-specific namespace declarations.
    /// </summary>
    private static string FixRootResourceDictionary(string xaml)
    {
        // Remove Windows10version1809 namespace declaration
        xaml = Regex.Replace(xaml,
            @"\s*xmlns:Windows10version1809=""[^""]*""\s*",
            "\n                    ",
            RegexOptions.Singleline);

        // Remove BelowWindows10version1809 namespace declaration
        xaml = Regex.Replace(xaml,
            @"\s*xmlns:BelowWindows10version1809=""[^""]*""\s*",
            "",
            RegexOptions.Singleline);

        return xaml;
    }

    private string? ProcessLine(string line, ref bool inColorPaletteResources, ref StringBuilder colourPaletteResourcesLine)
    {
        // Handle ColorPaletteResources (can span multiple lines)
        if (line.Contains("<Windows10version1809:ColorPaletteResources"))
        {
            inColorPaletteResources = true;
            colourPaletteResourcesLine.Clear();
            colourPaletteResourcesLine.Append(line);

            if (line.Contains("/>"))
            {
                // Single line ColorPaletteResources
                inColorPaletteResources = false;
                return ConvertColourPaletteResources(colourPaletteResourcesLine.ToString());
            }
            return null;
        }

        if (inColorPaletteResources)
        {
            colourPaletteResourcesLine.Append(" ").Append(line.Trim());
            if (line.Contains("/>"))
            {
                inColorPaletteResources = false;
                return ConvertColourPaletteResources(colourPaletteResourcesLine.ToString());
            }
            return null;
        }

        // Remove BelowWindows10version1809: prefix from Color elements
        if (line.Contains("<BelowWindows10version1809:Color"))
        {
            line = line.Replace("<BelowWindows10version1809:Color", "<Color");
            line = line.Replace("</BelowWindows10version1809:Color>", "</Color>");
        }

        // Convert AcrylicBrush to SolidColorBrush (WPF doesn't support AcrylicBrush)
        if (line.Contains("<AcrylicBrush"))
        {
            UwpElementsRemoved++;
            return ConvertAcrylicBrush(line);
        }

        // Convert RevealBackgroundBrush to SolidColorBrush (WPF doesn't support RevealBackgroundBrush)
        if (line.Contains("<RevealBackgroundBrush"))
        {
            UwpElementsRemoved++;
            return ConvertRevealBrush(line);
        }

        // Remove TargetTheme attribute (WPF doesn't use this)
        if (line.Contains("TargetTheme="))
        {
            line = TargetThemeRegex().Replace(line, "");
        }

        return line;
    }

    /// <summary>
    /// Converts a ColorPaletteResources element to individual Color (and optionally Brush) definitions wrapped in a ResourceDictionary.
    /// </summary>
    private string ConvertColourPaletteResources(string colourPaletteElement)
    {
        var result = new StringBuilder();
        var indent = GetIndent(colourPaletteElement);

        // Start a ResourceDictionary to contain the converted colours
        result.AppendLine($"{indent}<ResourceDictionary>");

        // Extract all colour attributes
        foreach (var attr in ColourPaletteAttributes)
        {
            var match = Regex.Match(colourPaletteElement, $@"{attr}=""([^""]+)""");
            if (match.Success)
            {
                var colourValue = match.Groups[1].Value;
                var colourKey = $"System{attr}Color";

                result.AppendLine($"{indent}    <Color x:Key=\"{colourKey}\">{colourValue}</Color>");
                ColoursConverted++;

                if (_generateBrushes)
                {
                    var brushKey = $"System{attr}Brush";
                    result.AppendLine($"{indent}    <SolidColorBrush x:Key=\"{brushKey}\" Color=\"{{StaticResource {colourKey}}}\" />");
                    BrushesGenerated++;
                }
            }
        }

        result.Append($"{indent}</ResourceDictionary>");

        return result.ToString();
    }

    /// <summary>
    /// Converts AcrylicBrush to SolidColorBrush using the FallbackColor.
    /// </summary>
    private string ConvertAcrylicBrush(string line)
    {
        // Extract key
        var keyMatch = Regex.Match(line, @"x:Key=""([^""]+)""");
        if (!keyMatch.Success)
        {
            return $"<!-- Removed unsupported AcrylicBrush -->";
        }

        var key = keyMatch.Groups[1].Value;
        var indent = GetIndent(line);

        // Try to get FallbackColor
        var fallbackMatch = Regex.Match(line, @"FallbackColor=""([^""]+)""");
        if (fallbackMatch.Success)
        {
            var fallback = fallbackMatch.Groups[1].Value;

            // If it's a ThemeResource reference, convert to StaticResource
            if (fallback.StartsWith("{ThemeResource"))
            {
                fallback = fallback.Replace("{ThemeResource", "{StaticResource");
            }

            return $"{indent}<SolidColorBrush x:Key=\"{key}\" Color=\"{fallback}\" />";
        }

        // Try TintColor as fallback
        var tintMatch = Regex.Match(line, @"TintColor=""([^""]+)""");
        if (tintMatch.Success)
        {
            var tint = tintMatch.Groups[1].Value;
            if (tint.StartsWith("{ThemeResource"))
            {
                tint = tint.Replace("{ThemeResource", "{StaticResource");
            }
            return $"{indent}<SolidColorBrush x:Key=\"{key}\" Color=\"{tint}\" />";
        }

        return $"{indent}<!-- Removed unsupported AcrylicBrush: {key} -->";
    }

    /// <summary>
    /// Converts RevealBackgroundBrush to SolidColorBrush.
    /// </summary>
    private string ConvertRevealBrush(string line)
    {
        // Extract key
        var keyMatch = Regex.Match(line, @"x:Key=""([^""]+)""");
        if (!keyMatch.Success)
        {
            return $"<!-- Removed unsupported RevealBackgroundBrush -->";
        }

        var key = keyMatch.Groups[1].Value;
        var indent = GetIndent(line);

        // Try FallbackColor first
        var fallbackMatch = Regex.Match(line, @"FallbackColor=""([^""]+)""");
        if (fallbackMatch.Success)
        {
            var fallback = fallbackMatch.Groups[1].Value;
            // Fix common formatting issues
            fallback = fallback.Replace("{ StaticResource", "{StaticResource");
            if (fallback.StartsWith("{ThemeResource"))
            {
                fallback = fallback.Replace("{ThemeResource", "{StaticResource");
            }
            return $"{indent}<SolidColorBrush x:Key=\"{key}\" Color=\"{fallback}\" />";
        }

        // Try Color attribute
        var colorMatch = Regex.Match(line, @"Color=""([^""]+)""");
        if (colorMatch.Success)
        {
            var color = colorMatch.Groups[1].Value;
            if (color.StartsWith("{ThemeResource"))
            {
                color = color.Replace("{ThemeResource", "{StaticResource");
            }
            return $"{indent}<SolidColorBrush x:Key=\"{key}\" Color=\"{color}\" />";
        }

        return $"{indent}<!-- Removed unsupported RevealBackgroundBrush: {key} -->";
    }

    /// <summary>
    /// Extracts the leading whitespace from a line for consistent indentation.
    /// </summary>
    private static string GetIndent(string line)
    {
        var match = Regex.Match(line, @"^(\s*)");
        return match.Success ? match.Groups[1].Value : "";
    }

    [GeneratedRegex(@"TargetTheme=""[^""]*""\s*")]
    private static partial Regex TargetThemeRegex();
}
