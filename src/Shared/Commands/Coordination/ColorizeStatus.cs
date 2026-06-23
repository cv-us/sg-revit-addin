using System;
using System.Drawing;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Construction-status buckets a workset can be mapped to. "Ignore"
    /// means the workset's elements are skipped entirely.
    /// </summary>
    public enum StatusBucket
    {
        Existing,
        Demo,
        Modify,
        New,
        Ignore
    }

    /// <summary>Where the colorize command applies.</summary>
    public enum ColorizeScope
    {
        EntireModel,
        ActiveView,
        Selection
    }

    /// <summary>
    /// Shared defaults + name-keyword auto-mapping for the Colorize by
    /// Workset/Status command.
    /// </summary>
    public static class ColorizeStatusInfo
    {
        /// <summary>The four real status buckets (excludes Ignore), in display order.</summary>
        public static readonly StatusBucket[] Buckets =
        {
            StatusBucket.Existing, StatusBucket.Demo, StatusBucket.Modify, StatusBucket.New
        };

        /// <summary>Default color per status (editable in the dialog).</summary>
        public static Color DefaultColor(StatusBucket s)
        {
            switch (s)
            {
                case StatusBucket.Existing: return Color.FromArgb(160, 160, 160); // gray
                case StatusBucket.Demo:     return Color.FromArgb(220, 40, 40);    // red
                case StatusBucket.Modify:   return Color.FromArgb(255, 170, 0);    // amber/orange
                case StatusBucket.New:      return Color.FromArgb(40, 180, 70);    // green
                default:                    return Color.FromArgb(128, 128, 128);
            }
        }

        /// <summary>Material name used for a status (e.g. "Status-New").</summary>
        public static string MaterialName(StatusBucket s) => "Status-" + s;

        /// <summary>
        /// Suffix appended to a pipe type name to make its colored per-status
        /// duplicate, e.g. "Welded" → "Welded - New". The suffix encodes the
        /// status so Clear can strip it and revert statelessly.
        /// </summary>
        public static string TypeSuffix(StatusBucket s) => " - " + s;

        /// <summary>
        /// If <paramref name="typeName"/> is one of our colored duplicates
        /// (ends with " - {Status}"), returns the original type name; else null.
        /// </summary>
        public static string OriginalTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            foreach (var s in Buckets)
            {
                string suf = TypeSuffix(s);
                if (typeName.EndsWith(suf, StringComparison.Ordinal) && typeName.Length > suf.Length)
                    return typeName.Substring(0, typeName.Length - suf.Length);
            }
            return null;
        }

        /// <summary>Display label.</summary>
        public static string Label(StatusBucket s)
        {
            switch (s)
            {
                case StatusBucket.Existing: return "Existing";
                case StatusBucket.Demo:     return "Demo";
                case StatusBucket.Modify:   return "Modify";
                case StatusBucket.New:      return "New";
                default:                    return "Ignore / skip";
            }
        }

        /// <summary>
        /// Suggests a status from a workset name by keyword. Order matters:
        /// "demo" before "modif" (both could contain substrings), and the
        /// most specific match wins. Returns Ignore when nothing matches.
        /// </summary>
        public static StatusBucket Suggest(string worksetName)
        {
            if (string.IsNullOrWhiteSpace(worksetName)) return StatusBucket.Ignore;
            string n = worksetName.ToLowerInvariant();

            // Demo first — "demo"/"demolish"/"remove".
            if (n.Contains("demo") || n.Contains("demolish") || n.Contains("remove"))
                return StatusBucket.Demo;
            // New.
            if (n.Contains("new")) return StatusBucket.New;
            // Modify / relocate / revise.
            if (n.Contains("modif") || n.Contains("reloc") || n.Contains("revis") || n.Contains("modify"))
                return StatusBucket.Modify;
            // Existing.
            if (n.Contains("exist") || n.Contains("(e)") || n.Contains("ex.") || n.Contains("e-"))
                return StatusBucket.Existing;

            return StatusBucket.Ignore;
        }
    }
}
