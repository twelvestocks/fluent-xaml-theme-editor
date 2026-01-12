// Copyright (c) Automint Ltd. All rights reserved.
// Licensed under the MIT License.

using System.CommandLine;
using WpfThemeConverter;

var inputOption = new Option<FileInfo?>(
    aliases: ["--input", "-i"],
    description: "The UWP ResourceDictionary XAML file to convert")
{
    IsRequired = true
};

var outputOption = new Option<FileInfo?>(
    aliases: ["--output", "-o"],
    description: "The output WPF Theme.xaml file path (defaults to input path with .wpf.xaml extension)");

var generateBrushesOption = new Option<bool>(
    aliases: ["--generate-brushes", "-b"],
    description: "Generate SolidColorBrush resources for each Color resource",
    getDefaultValue: () => true);

var rootCommand = new RootCommand("Converts Fluent XAML Theme Editor UWP exports to WPF-compatible Theme.xaml files")
{
    inputOption,
    outputOption,
    generateBrushesOption
};

rootCommand.SetHandler(async (input, output, generateBrushes) =>
{
    if (input == null || !input.Exists)
    {
        Console.Error.WriteLine($"Error: Input file '{input?.FullName}' does not exist.");
        Environment.Exit(1);
        return;
    }

    var outputPath = output?.FullName ?? Path.ChangeExtension(input.FullName, ".wpf.xaml");

    Console.WriteLine($"Converting: {input.FullName}");
    Console.WriteLine($"Output:     {outputPath}");
    Console.WriteLine($"Generate brushes: {generateBrushes}");
    Console.WriteLine();

    try
    {
        var converter = new ThemeConverter(generateBrushes);
        var inputXaml = await File.ReadAllTextAsync(input.FullName);
        var outputXaml = converter.Convert(inputXaml);

        await File.WriteAllTextAsync(outputPath, outputXaml);

        Console.WriteLine("Conversion complete!");
        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Colours converted: {converter.ColoursConverted}");
        Console.WriteLine($"  Brushes generated: {converter.BrushesGenerated}");
        Console.WriteLine($"  UWP elements removed: {converter.UwpElementsRemoved}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error during conversion: {ex.Message}");
        Environment.Exit(1);
    }
}, inputOption, outputOption, generateBrushesOption);

return await rootCommand.InvokeAsync(args);
