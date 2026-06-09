using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using SgRevitAddin.Commands.ViewsAndSheets.Models;
using SgRevitAddin.Commands.ViewsAndSheets.ViewModels;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// WPF dialog for the Legend Transfer command. The UI is defined
    /// programmatically (no XAML) so the linked-source glob shared by the
    /// SgRevit24 and SgRevit25 projects doesn't have to deal with Page
    /// build actions or generated InitializeComponent plumbing. It's still
    /// WPF with MVVM bindings; only the layout authoring differs.
    ///
    /// Layout (top → bottom):
    ///   • Description / instructions header
    ///   • "Copy From" + "Copy To" dropdowns side-by-side
    ///   • Search box
    ///   • Scrollable list of legends (checkbox + name + scale + element count)
    ///   • Select All / Deselect All buttons + status line
    ///   • Progress bar with current-legend label
    ///   • Transfer / Cancel buttons (right-aligned)
    /// </summary>
    public class LegendTransferWindow : Window
    {
        private readonly LegendTransferViewModel _vm;

        /// <summary>
        /// True if the user clicked Transfer (and the dialog was closed by
        /// the Command after the service finished). The Command sets this
        /// via <see cref="CloseAsTransferred"/>.
        /// </summary>
        public bool TransferRequested { get; private set; }

        /// <summary>
        /// The legends the user checked when they clicked Transfer. Empty
        /// when the dialog was cancelled.
        /// </summary>
        public IList<LegendInfo> SelectedLegends { get; private set; } = new List<LegendInfo>();

        public LegendTransferWindow(LegendTransferViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = vm;

            // Wire VM events to dialog-result behavior.
            vm.TransferRequested += picked =>
            {
                SelectedLegends = picked;
                TransferRequested = true;
                // The Command observes TransferRequested via DialogResult,
                // runs the service, then closes the dialog with
                // CloseAsTransferred(). We don't close here so the progress
                // bar can update during the run.
                DialogResult = true;
            };
            vm.CancelRequested += () =>
            {
                TransferRequested = false;
                DialogResult = false;
            };

            BuildWindow();
        }

        /// <summary>
        /// Re-parent the window to the Revit main window so it centers over
        /// Revit and stays on top. Must be called before <see cref="Window.ShowDialog"/>.
        /// </summary>
        public void OwnedByRevit()
        {
            try
            {
                IntPtr revit = Process.GetCurrentProcess().MainWindowHandle;
                if (revit != IntPtr.Zero)
                {
                    new WindowInteropHelper(this) { Owner = revit };
                }
            }
            catch { /* best-effort owner hookup */ }
        }

        public void CloseAsTransferred()
        {
            // Re-close in case showing finished progress kept it open.
            if (IsLoaded) Close();
        }

        // ── Layout ──

        private void BuildWindow()
        {
            Title = "Legend Transfer";
            Width = 720;
            Height = 680;
            MinWidth = 540;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new Grid { Margin = new Thickness(15) };
            for (int i = 0; i < 7; i++)
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star); // list row

            // ── Row 0: Header ──
            var header = new TextBlock
            {
                Text = "Transfer Legend views from one open document to another. " +
                       "Pick the source and target, choose which legends to copy, " +
                       "and click Transfer. Existing legends in the target with the " +
                       "same name are skipped (not overwritten).",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Row 1: Source + Target dropdowns ──
            var docGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            docGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            docGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            docGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            docGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            docGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lblFrom = new TextBlock
            {
                Text = "Copy From:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(lblFrom, 0);
            docGrid.Children.Add(lblFrom);

            var cbFrom = new ComboBox
            {
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            cbFrom.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(_vm.AvailableDocuments)));
            cbFrom.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedSourceDoc)) { Mode = BindingMode.TwoWay });
            Grid.SetColumn(cbFrom, 1);
            docGrid.Children.Add(cbFrom);

            var lblTo = new TextBlock
            {
                Text = "Copy To:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(lblTo, 3);
            docGrid.Children.Add(lblTo);

            var cbTo = new ComboBox
            {
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            cbTo.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(_vm.AvailableDocuments)));
            cbTo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedTargetDoc)) { Mode = BindingMode.TwoWay });
            Grid.SetColumn(cbTo, 4);
            docGrid.Children.Add(cbTo);

            Grid.SetRow(docGrid, 1);
            root.Children.Add(docGrid);

            // ── Row 2: Search ──
            var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lblSearch = new TextBlock
            {
                Text = "Search:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(lblSearch, 0);
            searchGrid.Children.Add(lblSearch);

            var txtSearch = new TextBox { Height = 26 };
            txtSearch.SetBinding(TextBox.TextProperty,
                new Binding(nameof(_vm.SearchText))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            Grid.SetColumn(txtSearch, 1);
            searchGrid.Children.Add(txtSearch);

            Grid.SetRow(searchGrid, 2);
            root.Children.Add(searchGrid);

            // ── Row 3: Legend list ──
            var listBorder = new Border
            {
                BorderBrush = SystemColors.ActiveBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var listBox = new ListBox { BorderThickness = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);
            listBox.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredLegends)));

            // Item template: CheckBox + 3 columns of text.
            var itemTemplate = new DataTemplate(typeof(LegendInfo));
            var rowFactory = new FrameworkElementFactory(typeof(Grid));
            rowFactory.SetValue(Grid.MarginProperty, new Thickness(2));
            for (int i = 0; i < 4; i++)
            {
                var col = new FrameworkElementFactory(typeof(ColumnDefinition));
                col.SetValue(ColumnDefinition.WidthProperty,
                    i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto);
                rowFactory.AppendChild(col);
            }

            var chk = new FrameworkElementFactory(typeof(CheckBox));
            chk.SetValue(Grid.ColumnProperty, 0);
            chk.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chk.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            chk.SetBinding(ToggleButton_IsCheckedBinding(), new Binding(nameof(LegendInfo.IsSelected))
            {
                Mode = BindingMode.TwoWay
            });
            rowFactory.AppendChild(chk);

            var nameText = new FrameworkElementFactory(typeof(TextBlock));
            nameText.SetValue(Grid.ColumnProperty, 1);
            nameText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            nameText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            nameText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            nameText.SetBinding(TextBlock.TextProperty, new Binding(nameof(LegendInfo.Name)));
            rowFactory.AppendChild(nameText);

            var scaleText = new FrameworkElementFactory(typeof(TextBlock));
            scaleText.SetValue(Grid.ColumnProperty, 2);
            scaleText.SetValue(TextBlock.TextProperty, "");
            scaleText.SetValue(FrameworkElement.MarginProperty, new Thickness(20, 0, 16, 0));
            scaleText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            scaleText.SetValue(TextBlock.ForegroundProperty, SystemColors.GrayTextBrush);
            scaleText.SetBinding(TextBlock.TextProperty, new Binding(nameof(LegendInfo.ScaleDisplay)));
            rowFactory.AppendChild(scaleText);

            var countText = new FrameworkElementFactory(typeof(TextBlock));
            countText.SetValue(Grid.ColumnProperty, 3);
            countText.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            countText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            countText.SetValue(TextBlock.ForegroundProperty, SystemColors.GrayTextBrush);
            countText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(LegendInfo.ElementCount)) { StringFormat = "{0} element{0:'s';'s';''}" });
            rowFactory.AppendChild(countText);

            itemTemplate.VisualTree = rowFactory;
            listBox.ItemTemplate = itemTemplate;

            // Clicking anywhere on the row toggles the checkbox.
            listBox.MouseDoubleClick += (s, e) =>
            {
                if (listBox.SelectedItem is LegendInfo li) li.IsSelected = !li.IsSelected;
            };

            listBorder.Child = listBox;
            Grid.SetRow(listBorder, 3);
            root.Children.Add(listBorder);

            // ── Row 4: Select All / Deselect All + status ──
            var actionRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnAll = new Button { Content = "Select All", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
            btnAll.SetBinding(ButtonBase_CommandBinding(),
                new Binding(nameof(_vm.SelectAllCommand)));
            Grid.SetColumn(btnAll, 0);
            actionRow.Children.Add(btnAll);

            var btnNone = new Button { Content = "Deselect All", Padding = new Thickness(10, 4, 10, 4) };
            btnNone.SetBinding(ButtonBase_CommandBinding(),
                new Binding(nameof(_vm.DeselectAllCommand)));
            Grid.SetColumn(btnNone, 1);
            actionRow.Children.Add(btnNone);

            var lblStatus = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = SystemColors.GrayTextBrush
            };
            lblStatus.SetBinding(TextBlock.TextProperty, new Binding(nameof(_vm.StatusLine)));
            Grid.SetColumn(lblStatus, 2);
            actionRow.Children.Add(lblStatus);

            Grid.SetRow(actionRow, 4);
            root.Children.Add(actionRow);

            // ── Row 5: Progress bar ──
            var progressRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            progressRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progressRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lblProgress = new TextBlock
            {
                Foreground = SystemColors.GrayTextBrush,
                Margin = new Thickness(0, 0, 0, 4),
                FontStyle = FontStyles.Italic
            };
            lblProgress.SetBinding(TextBlock.TextProperty, new Binding(nameof(_vm.ProgressLabel)));
            Grid.SetRow(lblProgress, 0);
            progressRow.Children.Add(lblProgress);

            var bar = new ProgressBar
            {
                Height = 14,
                Minimum = 0
            };
            bar.SetBinding(RangeBase_ValueBinding(),
                new Binding(nameof(_vm.ProgressValue)));
            bar.SetBinding(RangeBase_MaximumBinding(),
                new Binding(nameof(_vm.ProgressMaximum)));
            Grid.SetRow(bar, 1);
            progressRow.Children.Add(bar);

            Grid.SetRow(progressRow, 5);
            root.Children.Add(progressRow);

            // ── Row 6: Transfer / Cancel buttons (right-aligned) ──
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                IsCancel = true,
                Width = 90,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0)
            };
            btnCancel.SetBinding(ButtonBase_CommandBinding(),
                new Binding(nameof(_vm.CancelCommand)));

            var btnTransfer = new Button
            {
                Content = "Transfer",
                IsDefault = true,
                Width = 110,
                Height = 30
            };
            btnTransfer.SetBinding(ButtonBase_CommandBinding(),
                new Binding(nameof(_vm.TransferCommand)));

            btnRow.Children.Add(btnTransfer);
            btnRow.Children.Add(btnCancel);
            Grid.SetRow(btnRow, 6);
            root.Children.Add(btnRow);

            Content = root;
        }

        // ── Binding helpers (resolved at build time to avoid `using` clashes) ──
        private static DependencyProperty ToggleButton_IsCheckedBinding()
            => System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;
        private static DependencyProperty ButtonBase_CommandBinding()
            => System.Windows.Controls.Primitives.ButtonBase.CommandProperty;
        private static DependencyProperty RangeBase_ValueBinding()
            => System.Windows.Controls.Primitives.RangeBase.ValueProperty;
        private static DependencyProperty RangeBase_MaximumBinding()
            => System.Windows.Controls.Primitives.RangeBase.MaximumProperty;
    }
}
