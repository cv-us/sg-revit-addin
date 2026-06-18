namespace SgRevitAddin.Commands.Hangers.PlaceHangers
{
    /// <summary>
    /// Placement methods exposed by the unified Place Hangers command.
    /// </summary>
    public enum PlacementMethod
    {
        TypicalSpacing,      // auto-spaced along runs, raybounce to decks
        ParallelStructural,  // auto-spaced, attached to parallel framing
        Downstream,          // at downstream ends of threaded branchlines
        AtStructural         // where pipes cross structural framing
    }

    /// <summary>
    /// Per-method configuration objects produced by either the original
    /// per-command dialog OR the unified <c>PlaceHangersDialog</c>, then
    /// handed to each command's <c>RunPlacement</c>. They decouple the
    /// placement algorithms from any specific dialog.
    /// </summary>
    public abstract class HangerConfigBase
    {
        /// <summary>Hanger family name (OST_PipeAccessory) to place.</summary>
        public string SelectedFamily { get; set; }
    }

    public class TypicalSpacingConfig : HangerConfigBase
    {
        public string PipeTypeFilter { get; set; } = "ALL Pipes";
        public bool EvenlyDistributed { get; set; } = true;
        public double MaxSpacingFeet { get; set; } = 10.5;
        public string HangerTypeCode { get; set; } = "01";
        public double MaxClashHeightFeet { get; set; } = 10.0;
    }

    public class ParallelStructuralConfig : HangerConfigBase
    {
        public string PipeTypeFilter { get; set; } = "ALL Pipes";
        public bool EvenlyDistributed { get; set; } = true;
        public double MaxSpacingFeet { get; set; } = 10.5;
        public string HangerTypeCode { get; set; } = "01";
        public string WidemouthTypeCode { get; set; } = "01A";
        public bool AttachToBottom { get; set; } = true;
        public bool ShowCClamp { get; set; } = false;
        public bool UseLocalFraming { get; set; } = true;
        public string SelectedLinkName { get; set; }
    }

    public class DownstreamConfig : HangerConfigBase
    {
        public string RoofTypeCode { get; set; } = "03A";
        public string FloorDeckTypeCode { get; set; } = "05";
        public string FramingTypeCode { get; set; } = "01";
        public string StairsTypeCode { get; set; } = "";
        public double DistanceFromEndInches { get; set; } = 12.0;
        public double MinPipeLengthInches { get; set; } = 18.0;
        public bool ShowCClamp { get; set; } = false;
    }

    public class AtStructuralConfig : HangerConfigBase
    {
        public string TypeCode { get; set; } = "01";
        public string WidemouthTypeCode { get; set; } = "01A";
        public bool AttachToBottom { get; set; } = true;
        public bool ShowCClamp { get; set; } = false;
        public bool UseLocalFraming { get; set; } = true;
        public string SelectedLinkName { get; set; }
        public double MaxClashHeightFeet { get; set; } = 10.0;
    }
}
