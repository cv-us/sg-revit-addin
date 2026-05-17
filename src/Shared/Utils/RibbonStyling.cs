using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Adn = Autodesk.Windows;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Best-effort styling for our ribbon tab header chip. Revit's public
    /// API can't color tab UI, so we reach into Autodesk.Windows
    /// (AdWindows.dll, ships with every Revit install) to manipulate the
    /// underlying WPF visual tree.
    ///
    /// MECHANISM:
    ///   1. Find the TextBlock in the ribbon's visual tree whose .Text
    ///      equals our tab title (e.g. "SG ♈"). That TextBlock IS the
    ///      header label visible at the top of the ribbon.
    ///   2. Walk UP from the TextBlock to its nearest Border ancestor —
    ///      that's the chip background.
    ///   3. Paint: Border.Background = SG blue, TextBlock.Foreground = white.
    ///
    ///   The earlier approach (walk DOWN from the RibbonTab's DataContext
    ///   match) found the wrong element — it grabbed the panel content
    ///   container, which holds all panels under the tab, and painted
    ///   everything blue. Walking up from the TextBlock targets only the
    ///   chip.
    ///
    /// TIMING:
    ///   The tab header isn't in the visual tree until ItemInitialized
    ///   fires (and the tab is touched). We hook the event and re-apply
    ///   on every fire so Revit re-styling (theme switches, tab
    ///   re-creation) doesn't strip our paint.
    ///
    /// SAFETY:
    ///   Every operation is in try/catch. If Revit changes the visual
    ///   tree shape in a future version, the worst case is the styling
    ///   doesn't appear — never a crash.
    /// </summary>
    public static class RibbonStyling
    {
        /// <summary>SG brand color — applied as the tab chip background.</summary>
        public static readonly Color AccentColor = Color.FromRgb(0x08, 0x59, 0x90);

        /// <summary>Tab title text color when the chip is painted SG blue.</summary>
        public static readonly Color TextColor = Colors.White;

        private static EventHandler<Adn.RibbonItemEventArgs> _itemInitializedHandler;
        private static string _targetTabTitle;
        private static SolidColorBrush _bgBrush;
        private static SolidColorBrush _fgBrush;

        /// <summary>
        /// Schedules the accent to be applied to the named tab as soon as
        /// its WPF widget is in the visual tree. Safe to call multiple times.
        /// </summary>
        public static void ApplyTabAccent(string tabTitle)
        {
            try
            {
                _targetTabTitle = tabTitle;
                _bgBrush = new SolidColorBrush(AccentColor);
                _bgBrush.Freeze();
                _fgBrush = new SolidColorBrush(TextColor);
                _fgBrush.Freeze();

                // Try once now — sometimes the tab header is already wired
                // up if panels were created earlier in OnStartup.
                TryApply();

                // Re-apply on every ItemInitialized fire. Re-applying (vs.
                // unhooking after first success) survives Revit re-styling
                // the tab on theme switches or context changes.
                if (_itemInitializedHandler == null)
                {
                    _itemInitializedHandler = (_, __) => TryApply();
                    Adn.ComponentManager.ItemInitialized += _itemInitializedHandler;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SgRevitAddin] RibbonStyling.ApplyTabAccent: {ex.Message}");
            }
        }

        private static bool TryApply()
        {
            try
            {
                var ribbon = Adn.ComponentManager.Ribbon;
                if (ribbon == null) return false;

                // Find the visible label for our tab — a TextBlock whose
                // Text is exactly the tab title.
                var titleBlock = FindTextBlockByText(ribbon, _targetTabTitle);
                if (titleBlock == null) return false;

                // Paint the title text white.
                titleBlock.Foreground = _fgBrush;

                // Walk up to the chip's background element. We try Border
                // first (most common), then any Control that exposes a
                // Background property as a fallback.
                var border = FindAncestor<Border>(titleBlock);
                if (border != null)
                {
                    border.Background = _bgBrush;
                    return true;
                }

                var ctl = FindAncestor<Control>(titleBlock);
                if (ctl != null)
                {
                    ctl.Background = _bgBrush;
                    return true;
                }

                // Got the text block but couldn't find a paintable ancestor.
                // Text is white; chip background unchanged. Better than
                // painting the world.
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SgRevitAddin] RibbonStyling.TryApply: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Depth-first walk of the WPF visual tree under <paramref name="root"/>
        /// looking for a TextBlock whose Text equals <paramref name="text"/>.
        /// Returns null if no match.
        /// </summary>
        private static TextBlock FindTextBlockByText(DependencyObject root, string text)
        {
            if (root == null) return null;

            int count;
            try { count = VisualTreeHelper.GetChildrenCount(root); }
            catch { return null; }

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb && string.Equals(tb.Text, text, StringComparison.Ordinal))
                    return tb;

                var deeper = FindTextBlockByText(child, text);
                if (deeper != null) return deeper;
            }
            return null;
        }

        /// <summary>Walks up the visual tree from <paramref name="element"/> until it finds an ancestor of type T.</summary>
        private static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is T t) return t;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
