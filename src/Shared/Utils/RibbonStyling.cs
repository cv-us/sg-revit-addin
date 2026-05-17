using System;
using System.Linq;
using System.Windows;
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
    ///   RibbonTab. We walk the visual tree of ComponentManager.Ribbon to
    ///   find that widget, then apply BorderBrush + BorderThickness on its
    ///   bottom edge — yielding the colored underline strip seen on the
    ///   HydraCAD/CALC/LIST tabs.
    ///
    /// TIMING:
    ///   ComponentManager.Ribbon exists by OnStartup, but the visual tree
    ///   for our tab isn't fully wired until ItemInitialized fires (and
    ///   often only after the user clicks the tab once). We hook the event
    ///   and retry on every fire until the apply succeeds, then unhook.
    /// </summary>
    public static class RibbonStyling
    {
        /// <summary>SG brand color — applied as the accent underline. Theme-independent.</summary>
        public static readonly Color AccentColor = Color.FromRgb(0x08, 0x59, 0x90);

        /// <summary>Thickness of the colored underline, in device-independent pixels.</summary>
        private const double AccentBarThickness = 3.0;

        private static EventHandler<Adn.RibbonItemEventArgs> _itemInitializedHandler;
        private static string _targetTabTitle;
        private static bool _applied;

        /// <summary>
        /// Schedules the accent to be applied to the named tab as soon as
        /// its WPF widget is in the visual tree. Safe to call multiple times.
        /// </summary>
        public static void ApplyTabAccent(string tabTitle)
        {
            try
            {
                _targetTabTitle = tabTitle;
                _applied = false;

                // Try once now — sometimes the tab is already wired up if
                // panels were created earlier in the same OnStartup.
                if (TryApply()) return;

                // Otherwise, hook ItemInitialized and retry on every fire
                // until success.
                if (_itemInitializedHandler == null)
                {
                    _itemInitializedHandler = (_, __) =>
                    {
                        if (_applied) return;
                        if (TryApply())
                        {
                            Adn.ComponentManager.ItemInitialized -= _itemInitializedHandler;
                            _itemInitializedHandler = null;
                        }
                    };
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

                var brush = new SolidColorBrush(AccentColor);
                brush.Freeze(); // immutable + cheap

                widget.BorderBrush = brush;
                widget.BorderThickness = new Thickness(0, 0, 0, AccentBarThickness);

                _applied = true;
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
        /// looking for a Control whose DataContext is the given RibbonTab.
        /// Returns null if the tab's widget isn't in the tree yet.
        /// </summary>
        private static System.Windows.Controls.Control FindVisualFor(
            DependencyObject root, Adn.RibbonTab tab)
        {
            if (root == null) return null;

            int count;
            try { count = VisualTreeHelper.GetChildrenCount(root); }
            catch { return null; }

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.Control ctl
                    && ReferenceEquals(ctl.DataContext, tab))
                {
                    return ctl;
                }
                var deeper = FindVisualFor(child, tab);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
