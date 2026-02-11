using CommunityToolkit.Mvvm.ComponentModel;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Exporting.Presentation.RmvToObj
{
    internal partial class RmvToObjExporterViewModel : ObservableObject, IExporterViewModel
    {
        private readonly RmvToObjExporter _exporter;

        public string DisplayName => "Rmv_to_Obj";
        public string OutputExtension => ".obj";

        public RmvToObjExporterViewModel(RmvToObjExporter exporter)
        {
            _exporter = exporter;
        }

        public ExportSupportEnum CanExportFile(PackFile file)
        {
            var name = file.Name.ToLower();
            if (name.EndsWith(".rigidmodel") || name.EndsWith(".rmv2") || name.EndsWith(".rigid_model_v2"))
                return ExportSupportEnum.HighPriority;
            return ExportSupportEnum.NotSupported;
        }

        public void Execute(PackFile exportSource, string outputPath, bool generateImporter)
        {
            var settings = new RmvToObjExporterSettings 
            { 
                InputModelFile = exportSource, 
                OutputPath = outputPath 
            };
            _exporter.Export(settings);
        }
    }
}
