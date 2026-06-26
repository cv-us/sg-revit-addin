using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin
{
    /// <summary>
    /// Base for all hand-coded WinForms dialogs in the add-in. Opts the form
    /// into WinForms automatic scaling so layouts authored in absolute pixels at
    /// 96 DPI (100% display scaling) scale correctly on machines running 125% /
    /// 150% scaling.
    ///
    /// WHY THIS IS NEEDED: Revit is a DPI-aware host (2023–2024 system-DPI-aware,
    /// 2025+ per-monitor-v2). Without an explicit AutoScaleMode the form's scale
    /// factor stays 1.0, so the default font is realized at the monitor's real
    /// DPI (larger text) while the fixed ClientSize and absolute child rectangles
    /// stay pinned at 96-DPI pixels — bigger glyphs in unchanged boxes = the
    /// "cramped / clipped" look seen only on >100% machines. The dev box at 100%
    /// never shows it (factor would be 1.0 anyway).
    ///
    /// AutoScaleMode.Dpi + AutoScaleDimensions(96,96) is chosen deliberately over
    /// Font mode: it is LINEAR and FONT-INDEPENDENT, so at 96 DPI the factor is
    /// exactly 96/96 = 1.0 (zero change to today's good layout) and at 144 DPI it
    /// is exactly 1.5 — regardless of which default font the runtime resolves.
    /// PerformAutoScale multiplies ClientSize and every child Location/Size by the
    /// factor at load, so all dialogs keep authoring in plain 96-DPI pixels.
    ///
    /// One base class fixes both builds: .NET Framework 4.8 (Revit 2023/2024) via
    /// classic PerformAutoScale, and .NET 8 (Revit 2025/2026) where, under PMv2,
    /// the top-level window now scales according to AutoScaleMode. No app.config,
    /// manifest, or Application.SetHighDpiMode is involved — DPI awareness is a
    /// process-level setting owned by Revit.exe, which it already establishes.
    /// </summary>
    public class DpiAwareForm : Form
    {
        public DpiAwareForm()
        {
            // Set the mode, then the 96-DPI design baseline. The base ctor runs
            // before any derived ctor body, so this is established before the
            // derived InitializeComponent sets ClientSize and adds controls.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
        }
    }
}
