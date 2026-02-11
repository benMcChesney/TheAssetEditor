using CommunityToolkit.Mvvm.ComponentModel;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Exporting.Presentation.RmvToObj
{
    internal partial class RmvToObjExporterViewModel : ObservableObject, Editors.ImportExport.Exporting.Exporters.IExporterViewModel
    {
        private readonly RmvToObjExporter _exporter;

        public string DisplayName => "Rmv_to_Obj";
        public string OutputExtension => ".obj";

        public RmvToObjExporterViewModel(RmvToObjExporter exporter)
        {
            _exporter = exporter;
        }

        public ExportSupportEnum CanExportFile(PackFile file) => _exporter.CanExportFile(file);

        public void Execute(PackFile exportSource, string outputPath, bool generateImporter)
        {
            var settings = new RmvToObjExporterSettings { InputModelFile = exportSource, OutputPath = outputPath };
            _exporter.Export(settings);
        }
    }
}
