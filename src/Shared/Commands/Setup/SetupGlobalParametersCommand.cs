using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Setup
{
    /// <summary>
    /// Creates and initializes the full set of "Dynamo Setting - " global parameters
    /// used as a configuration store by the SSG FP Suite commands (and legacy Dynamo
    /// scripts). Parameters that already exist are left untouched; only missing ones
    /// are created with their default values.
    ///
    /// This is a one-time (or run-anytime-safe) project setup step.
    ///
    /// Migrated from: "! Setup - Global Parameters.dyn" (V10)
    ///
    /// WORKFLOW:
    ///   1. Check which global parameters already exist
    ///   2. Create any missing parameters as String type with defaults
    ///   3. Fix any seismic parameters that were created as Int64 (legacy issue)
    ///   4. Report summary
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetupGlobalParametersCommand : IExternalCommand
    {
        private const string Prefix = "Dynamo Setting - ";

        /// <summary>
        /// All 86 global parameters: (base name, default value).
        /// Full parameter name = Prefix + base name.
        /// </summary>
        private static readonly (string Name, string Default)[] Parameters = new[]
        {
            // ── AutoSync ──
            ("AutoSync Categories - Floors", "1"),
            ("AutoSync Categories - Framing", "1"),
            ("AutoSync Categories - Roofs", "1"),
            ("AutoSync Categories - Stairs", "1"),
            ("AutoSync Clash Height Distance", "10"),
            ("AutoSync Framing Hangers Sync'd To", "Bottom"),
            ("AutoSync Framing Offset Distance", "1"),
            ("AutoSync Hanger Type - Floors", "02"),
            ("AutoSync Hanger Type - Framing", "03"),
            ("AutoSync Hanger Type - Roofs", "01"),
            ("AutoSync Hanger Type - Stairs", "04"),
            ("AutoSync Keep Hanger Types", "true"),

            // ── C-Channel / Z-Purlin Hangers ──
            ("C-Channel Hanger Type - >= 6 Inch", "11B"),
            ("C-Channel Hanger Type - <= 4 Inch", "11T"),

            // ── Flexible Drops ──
            ("Flexible Drop Standard Lengths", "60 Inches (Default)"),
            ("Flexible Drop Tag Orientation", "NE (Default)"),

            // ── Linked Models ──
            ("LINKED MODEL - Architectural", "Architectural Linked Model"),
            ("LINKED MODEL - Structural", "Structural Linked Model"),
            ("LINKED MODEL - Use All Links", "false"),
            ("LINKED MODEL ID's", ""),

            // ── Pipe Elevations ──
            ("Maximum Clash Height", "10"),
            ("Pipe Elevations - TOS Distance", "15"),
            ("Pipe Elevations - AFF Distance", "50"),
            ("Pipe Elevations - Skip Short Pipes", "false"),
            ("Pipe Elevations - Minimum Length", "0"),
            ("Pipe Elevations - TOS Method", ""),
            ("Pipe Elevations - TOS Reference Plane", ""),
            ("Pipe Elevations - TOS Z Elevation", ""),
            ("Pipe Elevations - TOS Level", ""),
            ("Pipe Elevations - AFF Method", ""),
            ("Pipe Elevations - AFF Reference Plane", ""),
            ("Pipe Elevations - AFF Z Elevation", ""),
            ("Pipe Elevations - AFF Level", ""),
            ("Pipe Elevations - Object Type To Elevate", ""),

            // ── Pipe Hangers ──
            ("Pipe Hanger C-Clamp Visibility", "Hide (Default)"),
            ("Pipe Hanger Family", ""),
            ("Pipe Hanger Height", ""),
            ("Pipe Hanger Length", ""),
            ("Pipe Hanger Maximum Hanger Spacing", "10-6 (Default)"),
            ("Pipe Hanger Pipe Selection Filter", "ALL Pipes (Default)"),
            ("Pipe Hanger Position", "Closest Side of Structural Elements (Default)"),
            ("Pipe Hanger Symbol", "Default"),
            ("Pipe Hanger Type", "01"),
            ("Pipe Hanger Widemouth Type", "01A"),
            ("Pipe Hanger User Specified Distance", ""),
            ("Pipe Hangers Attach To Framing", "BOTTOM where possible (Default)"),
            ("Pipe Hangers To Be Equally Spaced", "Along Length of Pipe Run"),
            ("Pipe Runs To Process", ""),

            // ── Pipe Sleeves ──
            ("Pipe Sleeves - Filters", "Interior, Exterior, Fire-Rated, Structural"),
            ("Pipe Sleeves - Seismic", "Non-Seismic"),
            ("Pipe Sleeves - Wall Types", "All"),

            // ── Reference Level ──
            ("Reference Level", "Default Level 1"),

            // ── Seismic Bracing ──
            ("Seismic Brace Types To Insert", "Lateral and Logitudinal Braces"),
            ("Seismic Lateral Brace Family", "-SeismicBrace-Lateral-Tolco1001-Tolco980-Deck"),
            ("Seismic Lateral Brace Max Distance From End Of Main", "6"),
            ("Seismic Lateral Brace Maximum Spacing", "40"),
            ("Seismic Lateral Brace Orientation", "Plan View:   \u2190 Left of Pipe     \u2191 Above Pipe"),
            ("Seismic Longitudinal Brace Family", "-SeismicBrace-Longitudinal-Tolco4LA-Tolco980-Deck"),
            ("Seismic Longitudinal Brace Maximum Spacing", "80"),
            ("Seismic Longitudinal Brace Orientation", "Plan View:   \u2192 Right of Pipe   \u2193 Below Pipe "),
            ("Seismic Pipe Selection Filter", "ALL Pipes (Default)"),

            // ── Threaded Lines ──
            ("Threaded Lines - Hanger Assembly - Floor Decks", "05"),
            ("Threaded Lines - Hanger Assembly - Roofs", "03A"),
            ("Threaded Lines - Hanger Assembly - Stairs", ""),
            ("Threaded Lines - Hanger Assembly - Structural Framing", "01"),
            ("Threaded Lines - Hanger Distance From End Of Pipe", "12"),
            ("Threaded Lines - Minimum Length Of Pipe To Hang", "18"),
            ("Threaded Lines - Structural Categories", "RDFS"),

            // ── Trapeze Hangers ──
            ("Trapeze DualPipe Hanger Family", "-Pipe Trapeze Hanger - Dual Pipe - Standard"),
            ("Trapeze DualPipe Hanger Type", ""),
            ("Trapeze DualPipe Pipe Elevation", ""),
            ("Trapeze Hanger Family", "-Pipe Trapeze Hanger - Single Pipe - Standard"),
            ("Trapeze Hanger Maximum Hanger Spacing", "10-6 (Default)"),
            ("Trapeze Hanger Minimum Distance From TOS (Inches)", "7"),
            ("Trapeze Hanger Pipe Selection Filter", "ALL Pipes (Default)"),
            ("Trapeze Hanger Position", "Closest Side of Structural Elements (Default)"),
            ("Trapeze Hanger Type", "19A"),
            ("Trapeze Hanger User Specified Distance", ""),
            ("Trapeze Hangers To Be Equally Spaced", "Between Grid Lines"),
            ("Trapeze Pipe Hanger Type", "R3R"),

            // ── Trimble / Unistrut ──
            ("Trimble Hanger Type", "01"),
            ("Two Bays Between Grids", "false"),
            ("Unistrut Extension Measured From", "F"),
            ("Unistrut Extension Distance", "1"),

            // ── Z-Purlin ──
            ("Z-Purlin Hanger Type - >= 6 Inch", "11B"),
            ("Z-Purlin Hanger Type - <= 4 Inch", "11T"),
        };

        /// <summary>
        /// Seismic parameters that may have been created as Int64 in older scripts.
        /// These need to be deleted and recreated as String type.
        /// </summary>
        private static readonly string[] SeismicIntFixNames = new[]
        {
            "Seismic Lateral Brace Max Distance From End Of Main",
            "Seismic Lateral Brace Maximum Spacing",
            "Seismic Longitudinal Brace Maximum Spacing"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                int created = 0;
                int existing = 0;
                int fixed_ = 0;

                using (var tw = new TransactionWrapper(doc, "Setup Global Parameters"))
                {
                    // ── Fix legacy seismic Int64 parameters ──
                    foreach (string baseName in SeismicIntFixNames)
                    {
                        string fullName = Prefix + baseName;
                        GlobalParameter gp = FindGlobalParameter(doc, fullName);
                        if (gp != null)
                        {
                            // Check if stored as integer (Int64) — legacy issue
                            // If so, delete it so it gets recreated as String below
                            try
                            {
                                var val = gp.GetValue();
                                if (val is IntegerParameterValue)
                                {
                                    doc.Delete(gp.Id);
                                    fixed_++;
                                }
                            }
                            catch { }
                        }
                    }

                    // Regenerate after deletions
                    if (fixed_ > 0)
                        doc.Regenerate();

                    // ── Create missing parameters ──
                    foreach (var (name, defaultValue) in Parameters)
                    {
                        string fullName = Prefix + name;

                        GlobalParameter existingGp = FindGlobalParameter(doc, fullName);
                        if (existingGp != null)
                        {
                            existing++;
                            continue;
                        }

                        try
                        {
                            // Create as String type
                            GlobalParameter newGp = GlobalParameter.Create(
                                doc, fullName, SpecTypeId.String.Text);

                            if (newGp != null && !string.IsNullOrEmpty(defaultValue))
                            {
                                newGp.SetValue(new StringParameterValue(defaultValue));
                            }
                            created++;
                        }
                        catch (Exception)
                        {
                            // Skip parameters that fail to create
                        }
                    }

                    tw.Commit();
                }

                // ── Summary ──
                string summary = $"Global Parameters Setup Complete:\n\n" +
                                 $"Created: {created} new parameter{(created != 1 ? "s" : "")}\n" +
                                 $"Already existed: {existing}\n" +
                                 $"Total expected: {Parameters.Length}";

                if (fixed_ > 0)
                    summary += $"\n\nFixed {fixed_} legacy seismic parameter(s) (Int64 → String)";

                TaskDialog.Show("Setup Global Parameters", summary);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Find a global parameter by name, or return null.
        /// </summary>
        private GlobalParameter FindGlobalParameter(Document doc, string name)
        {
            // GlobalParametersManager.FindByName returns ElementId.InvalidElementId if not found
            ElementId id = GlobalParametersManager.FindByName(doc, name);
            if (id == null || id == ElementId.InvalidElementId)
                return null;
            return doc.GetElement(id) as GlobalParameter;
        }
    }
}
