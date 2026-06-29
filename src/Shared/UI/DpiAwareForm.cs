using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SgRevitAddin
{
    /// <summary>
    /// Base for all hand-coded WinForms dialogs in the add-in. TWO things make
    /// scaling work on >100% machines, and BOTH are required:
    ///
    /// (1) AutoScaleMode.Dpi + AutoScaleDimensions(96,96) — the linear, font-
    ///     independent scaling rule. At 96 DPI the factor is 96/96 = 1.0 (today's
    ///     good layout, unchanged); at 144 DPI it's exactly 1.5. PerformAutoScale
    ///     multiplies ClientSize and every child Location/Size by that factor at
    ///     load, so dialogs keep authoring in plain 96-DPI pixels.
    ///
    /// (2) The THREAD's DPI awareness context must be Per-Monitor-Aware-V2 at the
    ///     instant the window HANDLE is created — because a WinForms top-level
    ///     window snapshots its DeviceDpi from the thread context at handle
    ///     creation. Revit invokes IExternalCommand on a thread it has forced to
    ///     System-aware / effectively-96-DPI, so WITHOUT this the window is born at
    ///     DeviceDpi=96, the AutoScaleMode.Dpi factor collapses to 1.0, the layout
    ///     never grows, and only the font is realized large => the "cramped /
    ///     clipped at 150%" bug. (The 100% dev box never shows it: there the real
    ///     DPI is 96 anyway, so the missing 1.5x is invisible.)
    ///
    /// We override CreateHandle (always invoked, whatever the call-site static
    /// type, even if the handle is forced early in a ctor) AND ShowDialog to set
    /// PMv2 for the duration and restore Revit's context in a finally. We do NOT
    /// touch process-level awareness (SetProcessDpiAwareness*, SetHighDpiMode):
    /// that's owned by Revit.exe and locked once its windows exist. No
    /// app.manifest / app.config / runtimeconfig is involved, and the code is
    /// identical on .NET Framework 4.8 (Revit 2023/24) and .NET 8 (Revit 2025/26).
    /// </summary>
    public class DpiAwareForm : Form
    {
        public DpiAwareForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
        }

        /// <summary>The window's DeviceDpi is fixed here — create it under PMv2.</summary>
        protected override void CreateHandle()
        {
            using (DpiContext.PerMonitorV2())
                base.CreateHandle();
        }

        // Wrap both ShowDialog overloads too (covers the whole modal show and any
        // call site, belt-and-suspenders with the CreateHandle override).
        public new DialogResult ShowDialog()
        {
            using (DpiContext.PerMonitorV2())
                return base.ShowDialog();
        }

        public new DialogResult ShowDialog(IWin32Window owner)
        {
            using (DpiContext.PerMonitorV2())
                return base.ShowDialog(owner);
        }
    }

    /// <summary>
    /// Per-thread DPI awareness override. Sets the calling thread to
    /// PER_MONITOR_AWARE_V2 for the lifetime of the scope and restores the
    /// previous context on Dispose. Degrades to a safe no-op on OSes older than
    /// Windows 10 1703 or if the user32 entry points are missing, so it can never
    /// crash a command.
    /// </summary>
    internal sealed class DpiContext : IDisposable
    {
        // DPI_AWARENESS_CONTEXT pseudo-handle for PER_MONITOR_AWARE_V2 is (-4).
        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        private readonly IntPtr _previous;
        private readonly bool _restore;

        private DpiContext()
        {
            _restore = false;
            _previous = IntPtr.Zero;
            try
            {
                if (!IsValidDpiAwarenessContext(PerMonitorAwareV2))
                    return; // OS too old / context unsupported -> no-op

                IntPtr prev = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
                if (prev != IntPtr.Zero)
                {
                    _previous = prev;
                    _restore = true;
                }
            }
            catch (EntryPointNotFoundException) { /* pre-1607 user32 -> no-op */ }
            catch (DllNotFoundException) { /* no user32 (shouldn't happen) -> no-op */ }
        }

        public static DpiContext PerMonitorV2() => new DpiContext();

        public void Dispose()
        {
            if (!_restore) return;
            try { SetThreadDpiAwarenessContext(_previous); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsValidDpiAwarenessContext(IntPtr dpiContext);
    }
}
