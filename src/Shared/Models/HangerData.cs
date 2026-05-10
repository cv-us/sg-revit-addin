using Autodesk.Revit.DB;

namespace SgRevitAddin.Models
{
    public class HangerData
    {
        public ElementId ElementId { get; set; }
        public XYZ Location { get; set; }
        public string FamilyName { get; set; }
        public string HangerType { get; set; } // SinglePipe, Trapeze, Seismic
        public double RodLength { get; set; }
        public double PipeSize { get; set; }
    }
}
