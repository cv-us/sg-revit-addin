using Autodesk.Revit.DB;
using System;

namespace SSG_FP_Suite.Utils
{
    /// <summary>
    /// Wraps a Revit Transaction in a safe, disposable pattern.
    /// If you forget to call Commit(), or if an exception is thrown,
    /// the transaction is automatically rolled back — preventing
    /// half-modified models.
    ///
    /// USAGE:
    ///   using (var tw = new TransactionWrapper(doc, "My Operation"))
    ///   {
    ///       // modify elements here...
    ///       doc.Delete(someElementId);
    ///       element.LookupParameter("Comments").Set("Updated");
    ///
    ///       tw.Commit();  // ← Call this when everything succeeded
    ///   }
    ///   // If Commit() was never called (exception, early return, etc.),
    ///   // Dispose() rolls back automatically. Model stays clean.
    ///
    /// WHY USE THIS:
    ///   - Revit REQUIRES all model changes inside a Transaction
    ///   - If a Transaction is started but not committed or rolled back,
    ///     Revit will throw an error or corrupt the model state
    ///   - This wrapper guarantees cleanup no matter what happens
    /// </summary>
    public class TransactionWrapper : IDisposable
    {
        private readonly Transaction _transaction;
        private bool _committed;

        /// <summary>
        /// Creates and immediately starts a new transaction.
        /// </summary>
        /// <param name="doc">The Revit document to modify</param>
        /// <param name="name">A descriptive name shown in Revit's Undo history
        /// (e.g., "Place Hangers", "Update Elevations")</param>
        public TransactionWrapper(Document doc, string name)
        {
            _transaction = new Transaction(doc, name);
            _transaction.Start();
        }

        /// <summary>
        /// Commits all changes made since the transaction started.
        /// Call this at the END of your using block, only if everything succeeded.
        /// </summary>
        public void Commit()
        {
            _transaction.Commit();
            _committed = true;
        }

        /// <summary>
        /// Called automatically at the end of the using block.
        /// If Commit() was never called, this rolls back all changes.
        /// </summary>
        public void Dispose()
        {
            if (!_committed && _transaction.HasStarted())
            {
                _transaction.RollBack();
            }

            _transaction.Dispose();
        }
    }
}
