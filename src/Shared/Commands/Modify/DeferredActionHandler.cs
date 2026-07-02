using System;
using Autodesk.Revit.UI;

namespace SgRevitAddin.Commands.Modify
{
    /// <summary>
    /// Generic ExternalEvent dispatcher for the Modify-tab SG buttons.
    ///
    /// Those buttons are injected via AdWindows and fire on the UI thread OUTSIDE
    /// Revit's API context, where document modifications (transactions) are not
    /// allowed. This handler bridges the gap: a button calls <see cref="Run"/>; the
    /// action then executes inside <see cref="Execute"/> — a valid API context —
    /// where it may read the model, show a dialog, and open a transaction.
    /// </summary>
    public class DeferredActionHandler : IExternalEventHandler
    {
        private static DeferredActionHandler _instance;
        private static ExternalEvent _event;
        private Action<UIApplication> _pending;

        /// <summary>Create the ExternalEvent once, from App OnStartup (main thread).</summary>
        public static void Initialize()
        {
            if (_event != null) return;
            _instance = new DeferredActionHandler();
            _event = ExternalEvent.Create(_instance);
        }

        /// <summary>Queue an action to run in a valid Revit API context on the next idle.</summary>
        public static void Run(Action<UIApplication> action)
        {
            if (_event == null || _instance == null) Initialize();
            _instance._pending = action;
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            var action = _pending;
            _pending = null;
            if (action == null) return;
            try { action(app); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex)
            {
                TaskDialog.Show("SG", "Command failed:\n" + ex.Message);
            }
        }

        public string GetName() => "SG Deferred Action";
    }
}
