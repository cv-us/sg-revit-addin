namespace SgRevitAddin.Models
{
    /// <summary>
    /// A single line item on a fabrication list, cut list, or BOM.
    ///
    /// This is a generic item that can represent pipes, fittings, sprinklers,
    /// hangers, or any other material. Used by Fabrication commands to collect
    /// items and export them to CSV, Excel, or print.
    ///
    /// USAGE:
    ///   var item = new FabricationItem
    ///   {
    ///       ItemType = "Pipe",
    ///       Description = "Black Steel SCH 40",
    ///       Size = "1\"",
    ///       Length = 10.5,   // feet
    ///       Quantity = 1,
    ///       Material = "Black Steel",
    ///       Mark = "B-12",
    ///       SpoolNumber = "SP-003"
    ///   };
    /// </summary>
    public class FabricationItem
    {
        /// <summary>Category: "Pipe", "Fitting", "Sprinkler", "Hanger", etc.</summary>
        public string ItemType { get; set; }

        /// <summary>Human-readable description (e.g., "1\" Tee", "Pendent K5.6")</summary>
        public string Description { get; set; }

        /// <summary>Nominal size (e.g., "1\"", "1-1/4\"", "2\"")</summary>
        public string Size { get; set; }

        /// <summary>Length in feet (for pipes). 0 for fittings/sprinklers.</summary>
        public double Length { get; set; }

        /// <summary>Count of this item</summary>
        public int Quantity { get; set; }

        /// <summary>Material type (e.g., "Black Steel", "CPVC", "Copper")</summary>
        public string Material { get; set; }

        /// <summary>Mark/tag identifier from the Revit model</summary>
        public string Mark { get; set; }

        /// <summary>Spool/bundle number for shop fabrication grouping</summary>
        public string SpoolNumber { get; set; }
    }
}
