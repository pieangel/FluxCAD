using System;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Adapter26
{
    public static class DocLockTx
    {
        public static T RunRead<T>(Func<Transaction, Database, T> func)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                throw new InvalidOperationException("No active document. Open a DWG first.");

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                // read-only 작업이므로 Commit 안 해도 되지만, 습관적으로 유지해도 무방
                var result = func(tr, doc.Database);
                tr.Commit();
                return result;
            }
        }
    }
}
