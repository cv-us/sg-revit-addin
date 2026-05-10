using Autodesk.Revit.DB;

namespace SgRevitAddin.Models
{
    public class PipeSegmentData
    {
        public ElementId ElementId { get; set; }
        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public double Diameter { get; set; }
        public double Length { get; set; }
        public string SystemType { get; set; }
        public string Material { get; set; }
    }
}
