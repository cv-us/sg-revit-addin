using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using SgRevitAddin.Commands.ViewsAndSheets.Models;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.ViewsAndSheets.ViewModels
{
    /// <summary>
    /// View model for the Legend Transfer dialog. Holds the list of open
    /// documents, the current source/target selection, the legend list with
    /// per-item selection state, the search filter, and progress data.
    /// </summary>
    public class LegendTransferViewModel : INotifyPropertyChanged
    {
        // ── Documents ──
        public ObservableCollection<DocumentInfo> AvailableDocuments { get; }

        private DocumentInfo _selectedSourceDoc;
        public DocumentInfo SelectedSourceDoc
        {
            get => _selectedSourceDoc;
            set
            {
                if (!ReferenceEquals(_selectedSourceDoc, value))
                {
                    _selectedSourceDoc = value;
                    OnPropertyChanged();
                    ReloadLegends();
                }
            }
        }

        private DocumentInfo _selectedTargetDoc;
        public DocumentInfo SelectedTargetDoc
        {
            get => _selectedTargetDoc;
            set
            {
                if (!ReferenceEquals(_selectedTargetDoc, value))
                {
                    _selectedTargetDoc = value;
                    OnPropertyChanged();
                }
            }
        }

        // ── Legends ──
        public ObservableCollection<LegendInfo> AllLegends { get; }
        public ICollectionView FilteredLegends { get; }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != (value ?? ""))
                {
                    _searchText = value ?? "";
                    OnPropertyChanged();
                    FilteredLegends.Refresh();
                    OnPropertyChanged(nameof(StatusLine));
                }
            }
        }

        public string StatusLine
        {
            get
            {
                int total = AllLegends.Count;
                int shown = FilteredLegends.Cast<object>().Count();
                int selected = AllLegends.Count(l => l.IsSelected);
                if (string.IsNullOrEmpty(SearchText))
                    return $"{shown} legends   ·   {selected} selected";
                return $"{shown} of {total} match filter   ·   {selected} selected";
            }
        }

        // ── Progress ──
        private bool _isTransferring;
        public bool IsTransferring
        {
            get => _isTransferring;
            set { _isTransferring = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotTransferring)); }
        }
        public bool IsNotTransferring => !_isTransferring;

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private double _progressMaximum = 1;
        public double ProgressMaximum
        {
            get => _progressMaximum;
            set { _progressMaximum = value; OnPropertyChanged(); }
        }

        private string _progressLabel = "";
        public string ProgressLabel
        {
            get => _progressLabel;
            set { _progressLabel = value; OnPropertyChanged(); }
        }

        // ── Commands ──
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        /// <summary>
        /// Set by the Window: invoked when user clicks Transfer. Returns the
        /// selected legends in their original order. The Command/Service uses
        /// this to know what to transfer; the Window closes the dialog when
        /// the service finishes.
        /// </summary>
        public event Action<IList<LegendInfo>> TransferRequested;
        public ICommand TransferCommand { get; }
        public event Action CancelRequested;
        public ICommand CancelCommand { get; }

        public LegendTransferViewModel(IList<DocumentInfo> openDocs, DocumentInfo activeDoc)
        {
            AvailableDocuments = new ObservableCollection<DocumentInfo>(openDocs ?? new List<DocumentInfo>());
            AllLegends = new ObservableCollection<LegendInfo>();
            FilteredLegends = CollectionViewSource.GetDefaultView(AllLegends);
            FilteredLegends.Filter = LegendFilter;

            // Default: Source = first doc that isn't the active one (if any),
            // Target = the active doc.
            _selectedTargetDoc = activeDoc ?? AvailableDocuments.FirstOrDefault();
            _selectedSourceDoc = AvailableDocuments.FirstOrDefault(d => !ReferenceEquals(d, activeDoc))
                                ?? AvailableDocuments.FirstOrDefault();

            SelectAllCommand = new RelayCommand(_ => ToggleAll(true));
            DeselectAllCommand = new RelayCommand(_ => ToggleAll(false));
            TransferCommand = new RelayCommand(
                _ => TransferRequested?.Invoke(AllLegends.Where(l => l.IsSelected).ToList()),
                _ => CanTransfer());
            CancelCommand = new RelayCommand(_ => CancelRequested?.Invoke());

            ReloadLegends();
        }

        private bool CanTransfer()
        {
            if (IsTransferring) return false;
            if (SelectedSourceDoc == null || SelectedTargetDoc == null) return false;
            if (ReferenceEquals(SelectedSourceDoc, SelectedTargetDoc)) return false;
            return AllLegends.Any(l => l.IsSelected);
        }

        private bool LegendFilter(object item)
        {
            if (string.IsNullOrEmpty(SearchText)) return true;
            return ((LegendInfo)item).Name?
                .IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ToggleAll(bool checkedState)
        {
            // Toggle only currently-visible items (respect the search filter).
            foreach (LegendInfo item in FilteredLegends.Cast<LegendInfo>().ToList())
                item.IsSelected = checkedState;
            OnPropertyChanged(nameof(StatusLine));
        }

        private void ReloadLegends()
        {
            AllLegends.Clear();
            var doc = SelectedSourceDoc?.Document;
            if (doc == null) { OnPropertyChanged(nameof(StatusLine)); return; }

            var legends = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate)
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var v in legends)
            {
                int count = 0;
                try
                {
                    count = new FilteredElementCollector(doc, v.Id)
                        .WhereElementIsNotElementType()
                        .GetElementCount();
                }
                catch { /* per-legend count is best-effort */ }

                AllLegends.Add(new LegendInfo
                {
                    Legend = v,
                    Name = v.Name,
                    Scale = v.Scale,
                    ScaleDisplay = FormatScale(v.Scale),
                    ElementCount = count,
                    IsSelected = false
                });
            }

            FilteredLegends.Refresh();
            OnPropertyChanged(nameof(StatusLine));
        }

        private static string FormatScale(int scale)
        {
            // Revit's View.Scale is the denominator (e.g. 50 for 1:50). Some
            // views use special sentinels; clamp negatives to "—".
            return scale <= 0 ? "—" : $"1:{scale}";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
