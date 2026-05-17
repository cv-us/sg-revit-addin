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
    ///   2. Paint the TextBlock's Foreground white, Background blue.
    ///   3. Walk UP from the TextBlock for at most MaxWalkDepth (4) levels.
    ///      At each level: if the ancestor's bounds are still small enough
    ///      to be the chip (~&lt;= ChipMaxWidth), paint its Background blue
    ///      too; if it's wider than that, stop — we've reached the tab
    ///      strip.
    ///   4. Hook the TextBlock's LayoutUpdated event so we re-paint on
    ///      every layout pass. This is how the paint survives the tab
    ///      becoming active: Revit's active-state styling re-applies its
    ///      own brushes to some ancestors, and we need to re-paint them
    ///      back to SG blue right after.
    ///
    /// TIMING:
    ///   • OnStartup → TryApply once
    ///   • ComponentManager.ItemInitialized → re-apply
    ///   • TextBlock.LayoutUpdated (hooked once we find the widget) →
    ///     re-apply, with a re-entry guard so we don't loop
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

        /// <summary>Max walk-up depth from the title TextBlock.</summary>
        private const int MaxWalkDepth = 4;

        /// <summary>Width threshold (DIPs) above which an ancestor is assumed to be the tab strip, not the chip.</summary>
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

                // Hook LayoutUpdated once, on the first TextBlock we find.
                // LayoutUpdated fires after every WPF layout pass — including
                // when the tab is activated, when the user hovers it, and
                // when Revit re-applies its selection-state brushes. We
                // re-paint after each one so our colors stick.
                if (_hookedTitleBlock != titleBlock)
                {
                    if (_hookedTitleBlock != null && _layoutUpdatedHandler != null)
                    {
                        _hookedTitleBlock.LayoutUpdated -= _layoutUpdatedHandler;
                    }
                    _layoutUpdatedHandler = (_, __) => TryApply();
                    titleBlock.LayoutUpdated += _layoutUpdatedHandler;
                    _hookedTitleBlock = titleBlock;
                }

                Paint(titleBlock);
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
        /// Paints the title TextBlock and walks up, painting each ancestor
        /// whose bounds are still chip-sized. Idempotent — setting an
        /// already-set Brush is a no-op in WPF, so re-running on every
        /// LayoutUpdated tick is cheap.
        /// </summary>
        private static void Paint(TextBlock titleBlock)
        {
            titleBlock.Foreground = _fgBrush;
            titleBlock.Background = _bgBrush;

            DependencyObject current = titleBlock;
            for (int depth = 0; depth < MaxWalkDepth; depth++)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (parent == null) break;

                if (parent is FrameworkElement fe
                    && fe.ActualWidth > 0
                    && fe.ActualWidth > ChipMaxWidth)
                {
                    // Reached the tab strip — stop.
                    break;
                }

                TrySetBackground(parent, _bgBrush);
                current = parent;
            }
        }

        /// <summary>
        /// Sets Background on the element if it exposes one. Border, Panel,
        /// Control, and TextBlock each declare their own Background — no
        /// common base interface for it.
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
