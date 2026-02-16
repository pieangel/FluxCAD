using Bricscad.ApplicationServices;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class PluginEntry : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\n[FluxCAD] V26 (.NET 8) loaded.");
        }

        public void Terminate()
        {
        }
    }
}
