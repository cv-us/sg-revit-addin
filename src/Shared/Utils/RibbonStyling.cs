using System;
using System.Collections.Generic;
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
        private static string _targetPanelTitle;
        private static SolidColorBrush _bgBrush;
        private static SolidColorBrush _fgBrush;
        private static bool _isApplying; // re-entry guard for LayoutUpdated loops
        private static bool _isApplyingPanel; // re-entry guard for panel-title pass

        private static string _modifyPanelTitle;
        private static IList<ModifyButton> _modifyPanelButtons;
        private static bool _modifyPanelInjected;

        /// <summary>Max ancestor height (DIPs) when walking up from a panel-title TextBlock to its title-bar container.</summary>
        private const double PanelTitleBarMaxHeight = 32.0;

        /// <summary>
        /// Schedules the accent to be applied to the named tab as soon as
        /// its WPF widget is in the visual tree. Safe to call multiple times.
        /// </summary>
        public static void ApplyTabAccent(string tabTitle)
        {
            try
            {
                _targetTabTitle = tabTitle;
                EnsureBrushes();
                TryApply();
                HookItemInitialized();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SgRevitAddin] RibbonStyling.ApplyTabAccent: {ex.Message}");
            }
        }

        /// <summary>
        /// Schedules the accent to be applied to the title bar of every
        /// RibbonPanel whose title equals <paramref name="panelTitle"/>.
        /// Works across all tabs, including the contextual Modify tab —
        /// the painted state survives panel collapse/expand because
        /// ItemInitialized fires again when the panel rehydrates.
        /// </summary>
        public static void ApplyPanelTitleAccent(string panelTitle)
        {
            try
            {
                _targetPanelTitle = panelTitle;
                EnsureBrushes();
                TryApplyPanelTitle();
                HookItemInitialized();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SgRevitAddin] RibbonStyling.ApplyPanelTitleAccent: {ex.Message}");
            }
        }

        /// <summary>
        /// Injects a custom panel onto Revit's built-in <b>Modify</b> tab.
        /// The Revit <c>Tab</c> enum doesn't expose Modify, so the only
        /// route is through AdWindows — find the tab by Id, mint a
        /// <c>RibbonPanelSource</c> + <c>RibbonPanel</c>, and add buttons
        /// whose CommandHandler is a plain WPF ICommand (we use
        /// <see cref="RelayCommand"/>). Click handlers run on the UI
        /// thread, so they can show dialogs directly — no ExternalEvent
        /// needed unless a button needs to touch the Revit document.
        ///
        /// Idempotent: if a panel with the same title already exists on
        /// the Modify tab, nothing is added. Safe to call repeatedly via
        /// the ItemInitialized retry loop, which is what handles the
        /// "Modify tab not built yet at OnStartup" case.
        /// </summary>
        public static void InjectModifyPanel(string panelTitle, IList<ModifyButton> buttons)
        {
            try
            {
                _modifyPanelTitle = panelTitle;
                _modifyPanelButtons = buttons;
                TryInjectModifyPanel();
                HookItemInitialized();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SgRevitAddin] RibbonStyling.InjectModifyPanel: {ex.Message}");
            }
        }

        private static bool TryInjectModifyPanel()
        {
            if (_modifyPanelInjected) return true;
            if (string.IsNullOrEmpty(_modifyPanelTitle) || _modifyPanelButtons == null)
                return false;

            try
            {
                var ribbon = Adn.ComponentManager.Ribbon;
                if (ribbon == null) return false;

                Adn.RibbonTab modifyTab = null;
                foreach (var tab in ribbon.Tabs)
                {
                    if (string.Equals(tab.Id, "Modify", StringComparison.OrdinalIgnoreCase))
                    {
                        modifyTab = tab;
                        break;
                    }
                }
                if (modifyTab == null) return false;

                // Skip if our panel already exists (e.g. on a re-init).
                foreach (var existing in modifyTab.Panels)
                {
                    if (existing?.Source != null
                        && string.Equals(existing.Source.Title, _modifyPanelTitle, StringComparison.Ordinal))
                    {
                        _modifyPanelInjected = true;
                        return true;
                    }
                }

                var panelSource = new Adn.RibbonPanelSource
                {
                    Title = _modifyPanelTitle,
                    Id = "SgRevitAddin.Modify." + _modifyPanelTitle.Replace(" ", "_")
                };
                var panel = new Adn.RibbonPanel { Source = panelSource };

                foreach (var bd in _modifyPanelButtons)
                {
                    var btn = new Adn.RibbonButton
                    {
                        Id = bd.Id,
                        Text = bd.Label,
                        ToolTip = bd.Tooltip,
                        Description = bd.Tooltip,
                        Size = Adn.RibbonItemSize.Large,
                        ShowText = true,
                        ShowImage = true,
                        Orientation = Orientation.Vertical,
                        LargeImage = bd.LargeImage,
                        Image = bd.SmallImage,
                        CommandHandler = new RelayCommand(_ => bd.OnClick?.Invoke())
                    };
                    panelSource.Items.Add(btn);
                }

                modifyTab.Panels.Add(panel);
                _modifyPanelInjected = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SgRevitAddin] RibbonStyling.TryInjectModifyPanel: {ex.Message}");
                return false;
            }
        }

        private static void EnsureBrushes()
        {
            if (_bgBrush != null) return;
            _bgBrush = new SolidColorBrush(AccentColor);
            _bgBrush.Freeze();
            _fgBrush = new SolidColorBrush(TextColor);
            _fgBrush.Freeze();
        }

        private static void HookItemInitialized()
        {
            if (_itemInitializedHandler != null) return;
            _itemInitializedHandler = (_, __) =>
            {
                if (!string.IsNullOrEmpty(_targetTabTitle)) TryApply();
                if (!string.IsNullOrEmpty(_targetPanelTitle)) TryApplyPanelTitle();
                if (!_modifyPanelInjected) TryInjectModifyPanel();
            };
            Adn.ComponentManager.ItemInitialized += _itemInitializedHandler;
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
                    _layoutUpdatedHandler = (_, __) =>
                    {
                        TryApply();
                        if (!string.IsNullOrEmpty(_targetPanelTitle)) TryApplyPanelTitle();
                    };
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
        /// Finds every TextBlock in the ribbon whose Text matches
        /// <see cref="_targetPanelTitle"/>, walks up to the small title-bar
        /// container, and paints that subtree. There can be more than one
        /// match (e.g. a panel of the same name on a different tab), so we
        /// paint all of them.
        /// </summary>
        private static bool TryApplyPanelTitle()
        {
            if (_isApplyingPanel) return false;

            _isApplyingPanel = true;
            try
            {
                var ribbon = Adn.ComponentManager.Ribbon;
                if (ribbon == null) return false;

                var matches = new List<TextBlock>();
                FindAllTextBlocksByText(ribbon, _targetPanelTitle, matches);
                if (matches.Count == 0) return false;

                foreach (var tb in matches)
                {
                    DependencyObject titleBar = FindShortAncestor(tb, PanelTitleBarMaxHeight);
                    PaintSubtree(titleBar);
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SgRevitAddin] RibbonStyling.TryApplyPanelTitle: {ex.Message}");
                return false;
            }
            finally
            {
                _isApplyingPanel = false;
            }
        }

        /// <summary>
        /// Walks up from <paramref name="element"/> and returns the topmost
        /// ancestor whose ActualHeight is still ≤ <paramref name="maxHeight"/>.
        /// That's the panel title bar; one level higher is the full panel.
        ///
        /// Falls back to the original element if no ancestor qualifies (e.g.
        /// during initial layout when heights are still zero — the next
        /// LayoutUpdated will retry).
        /// </summary>
        private static DependencyObject FindShortAncestor(DependencyObject element, double maxHeight)
        {
            DependencyObject candidate = element;
            DependencyObject current = element;

            for (int depth = 0; depth < MaxWalkDepth; depth++)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (parent == null) break;

                if (parent is FrameworkElement fe
                    && fe.ActualHeight > 0
                    && fe.ActualHeight > maxHeight)
                {
                    // parent is the panel body — current is the title bar.
                    break;
                }

                candidate = parent;
                current = parent;
            }

            return candidate;
        }

        /// <summary>
        /// Depth-first walk that collects every TextBlock whose Text equals
        /// <paramref name="text"/>. Used for panel-title lookup since there
        /// can be multiple panels with the same name across tabs.
        /// </summary>
        private static void FindAllTextBlocksByText(DependencyObject root, string text, List<TextBlock> result)
        {
            if (root == null) return;

            int count;
            try { count = VisualTreeHelper.GetChildrenCount(root); }
            catch { return; }

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb && string.Equals(tb.Text, text, StringComparison.Ordinal))
                    result.Add(tb);

                FindAllTextBlocksByText(child, text, result);
            }
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

    /// <summary>
    /// Lightweight description of a button to inject onto an AdWindows-built
    /// panel (e.g. the Modify-tab SG panel). <see cref="OnClick"/> runs on
    /// the WPF UI thread when the button is clicked, so it can show dialogs
    /// directly. To touch the Revit document, raise an ExternalEvent from
    /// the handler instead.
    /// </summary>
    public class ModifyButton
    {
        public string Id;
        public string Label;
        public string Tooltip;
        public ImageSource LargeImage;
        public ImageSource SmallImage;
        public Action OnClick;
    }
}
