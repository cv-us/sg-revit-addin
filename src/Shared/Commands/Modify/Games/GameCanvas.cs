using System.Windows.Forms;

namespace SgRevitAddin.Commands.Modify.Games
{
    /// <summary>
    /// A flicker-free owner-drawn panel for the Modify-tab mini-games. Double
    /// buffered so the per-tick <c>Invalidate()</c> of an animating board never
    /// tears. The games draw everything in the panel's Paint handler and derive
    /// their cell/tile sizes from the panel's live ClientSize, so they stay crisp
    /// at any display scaling.
    /// </summary>
    internal class GameCanvas : Panel
    {
        public GameCanvas()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }
    }
}
