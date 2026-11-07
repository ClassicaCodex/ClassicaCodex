using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.About;

/// <summary>
/// The licences of what the iPad app is built from. A Mac app ships these as
/// files beside the program, which nobody on an iPad could open, and the MIT
/// licence asks for them to travel with every copy.
/// </summary>
internal sealed class NoticesWindow : FeatureWindow
{
    internal static readonly string[] FileNames =
    {
        "THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES-MAC.md", "THIRD-PARTY-NOTICES-DOTNET.txt"
    };

    public NoticesWindow()
    {
        Title = "Open-Source Notices";
        Content = new ScrollViewer
        {
            Content = new SelectableTextBlock
            {
                Text = Read(AppContext.BaseDirectory),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            }
        };
    }

    /// <summary>Every notice found in the folder, in order.</summary>
    internal static string Read(string folder)
    {
        var found = FileNames
            .Select(name => Path.Combine(folder, name))
            .Where(File.Exists)
            .Select(File.ReadAllText)
            .ToList();

        return found.Count > 0
            ? string.Join("\n\n", found)
            : $"The notices are at https://github.com/ClassicaCodex/ClassicaCodex/blob/main/{FileNames[0]}";
    }
}
