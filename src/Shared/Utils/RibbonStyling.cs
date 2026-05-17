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
    ///   1. Find the TextBlock whose .Text equals our tab title (e.g.
    ///      "SG ♈"). That's the visible label in the tab strip.
    ///   2. Paint the TextBlock's Foreground white, Background blue (a
    ///      guaranteed-correct floor in case the walk below fails).
    ///   3. Walk UP from the TextBlock for at most a few levels. At each
    ///      level: if the ancestor's bounds are still small enough to be
    ///      the chip (~&lt;= ChipMaxWidth), paint its Background blue too;
    ///      if it's wider than that, we've left the chip and are touching
    ///      the tab strip — stop.
    ///
    ///   The bounded walk-up is the fix for an earlier bug where a naive
    ///   FindAncestor&lt;Border&gt; jumped straight to the tab strip's outer
    ///   Border and ended up coloring every tab.
    ///
    /// TIMING:
    ///   The tab header isn't in the visual tree until ItemInitialized
    ///   fires. We re-apply on every fire so Revit re-styling (theme
    ///   switches, tab re-creation) doesn't strip our paint.
    ///
    /// SAFETY:
    ///   Every operation is in try/catch. Worst-case the chip just doesn't
    ///   get painted; we never crash the addin.
    /// </summary>
    public static class RibbonStyling
    {
        /// <summary>SG brand color — applied as the tab chip background.</summary>
        public static readonly Color AccentColor = Color.FromRgb(0x08, 0x59, 0x90);

        /// <summary>Tab title text color when the chip is painted SG blue.</summary>
        public static readonly Color TextColor = Colors.White;

        /// <summary>
        /// Max walk-up depth from the title TextBlock when searching for the
        /// chip background. The chip is typically 1-3 levels above the text.
        /// </summary>
        private const int MaxWalkDepth = 4;

        /// <summary>
        /// Width threshold (DIPs) above which an ancestor is assumed to be
        /// the tab strip container rather than the chip. Tab chips in Revit
        /// are ~60-120px wide; the tab strip is hundreds.
        /// </summary>
        private const double ChipMaxWidth = 200.0;

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

                TryApply();

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

                var titleBlock = FindTextBlockByText(ribbon, _targetTabTitle);
                if (titleBlock == null) return false;

                // Guaranteed minimum: paint the text itself.
                titleBlock.Foreground = _fgBrush;
                titleBlock.Background = _bgBrush;

                // Bounded walk-up: paint each ancestor's background until we
                // hit something too wide to be the chip. That keeps the
                // paint scoped to the SG tab and never touches the strip.
                DependencyObject current = titleBlock;
                for (int depth = 0; depth < MaxWalkDepth; depth++)
                {
                    var parent = VisualTreeHelper.GetParent(current);
                    if (parent == null) break;

                    if (parent is FrameworkElement fe)
                    {
                        // ActualWidth can be 0 during initial layout — treat
                        // unknown size as "still inside the chip" and keep
                        // walking, but cap at the depth limit.
                        if (fe.ActualWidth > 0 && fe.ActualWidth > ChipMaxWidth)
                        {
                            // Too wide — we've reached the strip. Stop.
                            break;
                        }

                        TrySetBackground(parent, _bgBrush);
                    }

                    current = parent;
                }

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
        /// Sets Background on the element if it exposes one. Different WPF
        /// types use different property names — Border, Panel, and Control
        /// all have a Background property but no common base class declares
        /// it (Border.Background is its own, etc.).
        /// </summary>
        private static void TrySetBackground(DependencyObject element, Brush brush)
        {
            switch (element)
            {
                case Border b:
                    b.Background = brush;
                    break;
                case Panel p:
                    p.Background = brush;
                    break;
                case Control c:
                    c.Background = brush;
                    break;
                case TextBlock tb:
                    tb.Background = brush;
                    break;
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
    }
}
