using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace SgRevitAddin.Commands.ViewsAndSheets.Models
{
    /// <summary>
    /// Display + selection state for one source-document legend view in the
    /// Legend Transfer dialog.
    /// </summary>
    public class LegendInfo : INotifyPropertyChanged
    {
        /// <summary>The source legend view itself.</summary>
        public View Legend { get; set; }

        public string Name { get; set; }

        /// <summary>Display string like "1:50" for the view scale.</summary>
        public string ScaleDisplay { get; set; }

        /// <summary>Raw scale integer (denominator). Used when re-applying to the target.</summary>
        public int Scale { get; set; }

        public int ElementCount { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
