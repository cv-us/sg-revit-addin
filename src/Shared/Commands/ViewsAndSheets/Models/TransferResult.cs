namespace SgRevitAddin.Commands.ViewsAndSheets.Models
{
    public enum TransferStatus
    {
        Success,
        Skipped,
        Failed
    }

    /// <summary>
    /// Result of one legend-transfer attempt. The Service produces one of
    /// these per legend the user requested.
    /// </summary>
    public class TransferResult
    {
        public string LegendName { get; set; }
        public TransferStatus Status { get; set; }
        /// <summary>Why the transfer was skipped or failed. Empty for Success.</summary>
        public string Reason { get; set; }
    }
}
