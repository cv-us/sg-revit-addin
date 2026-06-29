using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin
{
    /// <summary>
    /// Base for all hand-coded WinForms dialogs. Makes them scale correctly on
    /// >100% display scaling, makes them resizable, and remembers their size.
    ///
    /// THREE things are needed and they interact:
    ///
    /// (1) PMv2 thread context at handle creation. A WinForms window snapshots its
    ///     DeviceDpi from the thread's DPI awareness context when its HWND is
    ///     created. Revit invokes IExternalCommand on a thread pinned to ~96 DPI,
    ///     so we wrap CreateHandle + ShowDialog in a Per-Monitor-Aware-V2 context
    ///     (SetThreadDpiAwarenessContext) so the window is born at the real DPI
    ///     (144 @150%). [Confirmed at runtime: DeviceDpi reads 144.]
    ///
    /// (2) Re-fire the auto-scale AFTER the handle exists. AutoScaleMode.Dpi runs a
    ///     ONE-SHOT layout pass whose factor = DeviceDpi / AutoScaleDimensions. The
    ///     AutoScaleDimensions setter has a side effect: with no SuspendLayout (our
    ///     hand-coded forms have none) it runs PerformAutoScale SYNCHRONOUSLY. So
    ///     setting it in the ctor fired that one shot handle-less at 96 DPI →
    ///     factor 96/96 = 1.0 → it scaled nothing and stamped itself "done". The
    ///     later 144-DPI handle then only enlarged the FONT (a separate paint-time
    ///     path), leaving the 96-px layout → cramped. Fix: do NOT set
    ///     AutoScaleDimensions in the ctor; re-stamp it in OnHandleCreated (handle
    ///     now reports 144) so the single pass fires at 144/96 = 1.5 and scales
    ///     ClientSize + every child rect + the font (the font-correct supported
    ///     path — unlike Control.Scale, which would double the already-144 font).
    ///
    /// (3) Resizable + AutoScroll + remembered size, applied in OnHandleCreated
    ///     (AFTER the derived ctor set FixedDialog, so we win) and AFTER the DPI
    ///     scale (so a remembered size isn't scaled twice). Size is persisted in
    ///     logical 96-px units so it's portable across machines at different DPI.
    ///
    /// We never touch process-level awareness (SetProcessDpiAwareness*,
    /// SetHighDpiMode) — Revit owns that. No manifest/app.config/runtimeconfig.
    /// Identical on .NET Framework 4.8 (Revit 2023/24) and .NET 8 (Revit 2025/26).
    /// </summary>
    public class DpiAwareForm : Form
    {
        private bool _dpiInit;
        private bool _layoutInit;

        /// <summary>Set false in a derived ctor to keep the dialog fixed-size.</summary>
        protected bool AllowResize { get; set; } = true;
        /// <summary>Set false in a derived ctor to skip remembering the size.</summary>
        protected bool RememberSize { get; set; } = true;

        public DpiAwareForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            // AutoScaleDimensions is set in OnHandleCreated, NOT here — setting it
            // now fires the one-shot scale handle-less at 96 DPI and disarms it.
        }

        /// <summary>The window's DeviceDpi is fixed here — create it under PMv2.</summary>
        protected override void CreateHandle()
        {
            using (DpiContext.PerMonitorV2())
                base.CreateHandle();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_dpiInit) return;     // guard against handle recreation
            _dpiInit = true;

            float factor = DeviceDpi / 96f;

            // (1) DPI scale FIRST — re-fire the swallowed pass now the handle
            //     reports the real DPI. Clear the stale 96 baseline (busts the
            //     cached CurrentAutoScaleDimensions), then re-stamp it: the setter
            //     recomputes from DeviceDpi (144) and runs PerformAutoScale at 1.5.
            if (DeviceDpi != 96)
            {
                AutoScaleDimensions = SizeF.Empty;
                AutoScaleDimensions = new SizeF(96f, 96f);
                PerformAutoScale();
            }

            // (2) Resizable + scrollbars (safety net: an undersized window gets
            //     scrollbars instead of clipping). Overrides the derived ctor's
            //     FixedDialog because this runs after it.
            if (AllowResize)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = true;
            }
            AutoScroll = true;

            // (3) MinimumSize floor (scaled px) so content can't be squeezed away.
            if (MinimumSize.Width <= 0 || MinimumSize.Height <= 0)
                MinimumSize = new Size((int)Math.Round(Width * 0.6f), (int)Math.Round(Height * 0.6f));

            // (4) Restore remembered size LAST, logical 96-px -> device px. A plain
            //     geometry set (autoscale already stamped its baseline), not re-scaled.
            if (RememberSize)
            {
                string key = GetType().Name;
                int w = DialogMemory.GetInt(key, "WinW", 0);
                int h = DialogMemory.GetInt(key, "WinH", 0);
                if (w > 0 && h > 0)
                {
                    Size = new Size(
                        Math.Max((int)Math.Round(w * factor), MinimumSize.Width),
                        Math.Max((int)Math.Round(h * factor), MinimumSize.Height));
                }
            }
            _layoutInit = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_layoutInit && RememberSize && WindowState == FormWindowState.Normal)
            {
                try
                {
                    float factor = DeviceDpi / 96f;
                    string key = GetType().Name;
                    DialogMemory.SetInt(key, "WinW", (int)Math.Round(Width / factor));
                    DialogMemory.SetInt(key, "WinH", (int)Math.Round(Height / factor));
                    DialogMemory.Flush();
                }
                catch { }
            }
            base.OnFormClosing(e);
        }

        // Wrap both ShowDialog overloads so the whole modal show runs PMv2 (belt-
        // and-suspenders with the CreateHandle override).
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
    /// previous context on Dispose. Safe no-op on OSes older than Windows 10 1703
    /// or if the user32 entry points are missing, so it can never crash a command.
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
