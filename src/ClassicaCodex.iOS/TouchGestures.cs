using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ClassicaCodex.iOS;

/// <summary>
/// The reader's line menu acts on the selected line. A right-click selects the
/// line it lands on, but a long-press on its own would leave the menu acting on
/// whichever line was selected before, so the line under the finger is selected
/// as the press is held.
/// </summary>
public static class TouchGestures
{
    public static void Install()
    {
        InputElement.HoldingEvent.AddClassHandler<ListBoxItem>(
            OnRowHeld, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void OnRowHeld(ListBoxItem row, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started) return;
        if (ItemsControl.ItemsControlFromItemContainer(row) is not ListBox list) return;

        list.SelectedIndex = list.IndexFromContainer(row);
    }
}
