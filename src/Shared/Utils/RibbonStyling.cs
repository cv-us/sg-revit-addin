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
    ///   2. Walk UP from the TextBlock to locate the "chip container" —
    ///      the topmost ancestor that's still small enough to be a single
    ///      tab chip (≤ ChipMaxWidth). One level above that is the tab
    ///      strip, which we never touch.
    ///   3. Recursively paint EVERY element under the chip container:
    ///        - Border.Background, Panel.Background, Control.Background,
    ///          TextBlock.Background = SG blue
    ///        - TextBlock.Foreground = white
    ///      This catches Revit's selection-state overlay (a translucent
    ///      element that appears on top of the chip when the tab is
    ///      active, otherwise blends our blue with white and looks lighter).
    ///   4. Re-apply on every LayoutUpdated so Revit's re-styling on
    ///      activation/deactivation doesn't strip the paint.
    ///
    /// TIMING:
    ///   • OnStartup → TryApply once
    ///   • ComponentManager.ItemInitialized → re-apply
    ///   • TextBlock.LayoutUpdated → re-apply (with re-entry guard)
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

        /// <summary>Max walk-up depth from the title TextBlock when searching for the chip container.</summary>
        private const int MaxWalkDepth = 6;

        /// <summary>Width threshold (DIPs) above which an ancestor is the tab strip, not the chip.</summary>
        private const double ChipMaxWidth = 200.0;

        private static EventHandler<Adn.RibbonItemEventArgs> _itemInitializedHandler;
        private static EventHandler _layoutUpdatedHandler;
        private static TextBlock _hookedTitleBlock;
        private static string _targetTabTitle;
        private static SolidColorBrush _bgBrush;
        private static SolidColorBrush _fgBrush;
        private static bool _isApplying; // re-entry guard for LayoutUpdated loops

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
            if (_isApplying) return false; // re-entry guard

            _isApplying = true;
            try
            {
                var ribbon = Adn.ComponentManager.Ribbon;
                if (ribbon == null) return false;

                var titleBlock = FindTextBlockByText(ribbon, _targetTabTitle);
                if (titleBlock == null) return false;

                // Hook LayoutUpdated once. WPF fires this after every layout
                // pass — including when Revit applies its active-state
                // brushes to the tab on selection. We re-paint after each
                // one so our colors stick.
                if (!ReferenceEquals(_hookedTitleBlock, titleBlock))
                {
                    if (_hookedTitleBlock != null && _layoutUpdatedHandler != null)
                        _hookedTitleBlock.LayoutUpdated -= _layoutUpdatedHandler;
                    _layoutUpdatedHandler = (_, __) => TryApply();
                    titleBlock.LayoutUpdated += _layoutUpdatedHandler;
                    _hookedTitleBlock = titleBlock;
                }

                // Walk up to find the chip container — the topmost ancestor
                // whose width is still chip-sized. Its parent is the tab
                // strip; we never touch that.
                DependencyObject chipRoot = FindChipContainer(titleBlock);

                // Paint the chip container's entire subtree. This catches
                // active-state overlay elements that sit above our painted
                // ancestors in z-order.
                PaintSubtree(chipRoot);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SgRevitAddin] RibbonStyling.TryApply: {ex.Message}");
                return false;
            }
            finally
            {
                _isApplying = false;
            }
        }

        /// <summary>
        /// Walks up from the title TextBlock and returns the topmost ancestor
        /// whose ActualWidth is still ≤ ChipMaxWidth. That's the chip
        /// container; one level higher is the tab strip.
        ///
        /// Falls back to the TextBlock itself if no ancestor qualifies (e.g.
        /// during initial layout when widths are still zero).
        /// </summary>
        private static DependencyObject FindChipContainer(TextBlock titleBlock)
        {
            DependencyObject candidate = titleBlock;
            DependencyObject current = titleBlock;

            for (int depth = 0; depth < MaxWalkDepth; depth++)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (parent == null) break;

                if (parent is FrameworkElement fe
                    && fe.ActualWidth > 0
                    && fe.ActualWidth > ChipMaxWidth)
                {
                    // parent is the tab strip — current is the chip root.
                    break;
                }

                candidate = parent;
                current = parent;
            }

            return candidate;
        }

        /// <summary>
        /// Paints the given element and every descendant with our brushes.
        /// Scope is the chip subtree, so this safely catches selection-state
        /// overlays without bleeding to other tabs.
        /// </summary>
        private static void PaintSubtree(DependencyObject element)
        {
            TrySetBackground(element, _bgBrush);
            if (element is TextBlock tb && !ReferenceEquals(tb.Foreground, _fgBrush))
                tb.Foreground = _fgBrush;

            int count;
            try { count = VisualTreeHelper.GetChildrenCount(element); }
            catch { return; }

            for (int i = 0; i < count; i++)
            {
                PaintSubtree(VisualTreeHelper.GetChild(element, i));
            }
        }

        /// <summary>
        /// Sets Background on the element if it exposes one. Skips redundant
        /// writes via ReferenceEquals so the LayoutUpdated re-apply loop
        /// stays cheap.
        /// </summary>
        private static void TrySetBackground(DependencyObject element, Brush brush)
        {
            switch (element)
            {
                case Border b:
                    if (!ReferenceEquals(b.Background, brush)) b.Background = brush;
                    break;
                case Panel p:
                    if (!ReferenceEquals(p.Background, brush)) p.Background = brush;
                    break;
                case Control c:
                    if (!ReferenceEquals(c.Background, brush)) c.Background = brush;
                    break;
                case TextBlock tb:
                    if (!ReferenceEquals(tb.Background, brush)) tb.Background = brush;
                    break;
                case System.Windows.Shapes.Rectangle r:
                    if (!ReferenceEquals(r.Fill, brush)) r.Fill = brush;
                    break;
            }
        }

        /// <summary>
        /// Depth-first walk of the WPF visual tree looking for a TextBlock
        /// whose Text equals <paramref name="text"/>. Returns null if no match.
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
