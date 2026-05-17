using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Adn = Autodesk.Windows;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Best-effort styling for our ribbon tab. Revit's public API can't color
    /// tab UI (Autodesk.Revit.UI.RibbonPanel has only IsEnabled / Title /
    /// Visible), so we reach into Autodesk.Windows (AdWindows.dll, ships with
    /// every Revit install) to manipulate the underlying WPF control.
    ///
    /// Every operation here is guarded by try/catch — if Revit's internal
    /// tree changes between versions, the worst case is the accent doesn't
    /// appear, never a crash.
    ///
    /// MECHANISM:
    ///   The Autodesk.Windows.RibbonTab is a logical object; the visible
    ///   widget is a WPF FrameworkElement whose DataContext points at that
    ///   RibbonTab. We walk the visual tree to find it, then recursively
    ///   paint:
    ///     • every Border.Background     → SG blue
    ///     • every Rectangle.Fill        → SG blue
    ///     • every TextBlock.Foreground  → white
    ///     • the wrapper Control.{Background,Foreground} too as a fallback
    ///
    ///   We need to walk the whole subtree because Revit's tab template uses
    ///   a Border (or similar) inside to draw the actual chip — setting
    ///   Background on the outer control wouldn't reach the painted pixels.
    ///
    /// TIMING:
    ///   ComponentManager.Ribbon exists by OnStartup, but the visual tree
    ///   for our tab isn't fully wired until ItemInitialized fires (and
    ///   often only after the user clicks the tab once). We hook the event
    ///   and retry on every fire until the apply succeeds, then unhook.
    ///
    ///   The Revit theme can also re-style tabs lazily (e.g. on theme
    ///   switch). We re-apply on every ItemInitialized fire rather than
    ///   only the first to survive a theme reset.
    /// </summary>
    public static class RibbonStyling
    {
        /// <summary>SG brand color — applied as the tab chip background. Theme-independent.</summary>
        public static readonly Color AccentColor = Color.FromRgb(0x08, 0x59, 0x90);

        /// <summary>Tab title color when the chip is painted SG blue.</summary>
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

                // Try once now — sometimes the tab is already wired up if
                // panels were created earlier in OnStartup.
                TryApply();

                // Hook ItemInitialized and re-apply on every fire. Re-applying
                // (vs. unhooking after first success) survives Revit re-styling
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

                var tab = ribbon.Tabs.FirstOrDefault(t =>
                    string.Equals(t.Title, _targetTabTitle, StringComparison.Ordinal));
                if (tab == null) return false;

                var widget = FindVisualFor(ribbon, tab);
                if (widget == null) return false;

                // Wrapper element: set its own Background/Foreground. Many tab
                // templates respect this as a TemplateBinding source.
                widget.Background = _bgBrush;
                widget.Foreground = _fgBrush;

                // Walk into the subtree and paint every visible element. This
                // overrides whatever the template was binding from the parent.
                PaintDescendants(widget);

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
        /// Recursively paints every Border/Rectangle background and every
        /// TextBlock foreground in the visual subtree. Brutal but effective —
        /// scope is the tab header chip, which is a tiny subtree (a few
        /// nested elements at most).
        /// </summary>
        private static void PaintDescendants(DependencyObject root)
        {
            int count;
            try { count = VisualTreeHelper.GetChildrenCount(root); }
            catch { return; }

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                switch (child)
                {
                    case Border b:
                        b.Background = _bgBrush;
                        break;
                    case System.Windows.Shapes.Rectangle r:
                        r.Fill = _bgBrush;
                        break;
                    case TextBlock tb:
                        tb.Foreground = _fgBrush;
                        break;
                    case Control c:
                        // ContentPresenter / Label / etc. — try fg/bg on
                        // anything that exposes them.
                        c.Background = _bgBrush;
                        c.Foreground = _fgBrush;
                        break;
                }
                PaintDescendants(child);
            }
        }

        /// <summary>
        /// Depth-first walk of the WPF visual tree under <paramref name="root"/>
        /// looking for a Control whose DataContext is the given RibbonTab.
        /// Returns null if the tab's widget isn't in the tree yet.
        /// </summary>
        private static Control FindVisualFor(DependencyObject root, Adn.RibbonTab tab)
        {
            if (root == null) return null;

            int count;
            try { count = VisualTreeHelper.GetChildrenCount(root); }
            catch { return null; }

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Control ctl && ReferenceEquals(ctl.DataContext, tab))
                    return ctl;
                var deeper = FindVisualFor(child, tab);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
