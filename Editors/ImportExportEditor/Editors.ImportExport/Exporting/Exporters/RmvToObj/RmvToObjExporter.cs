using System.Globalization;
using System.IO;
using System.Text;
using Editors.ImportExport.Misc;
using Shared.GameFormats.RigidModel;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Exporting.Exporters
{
    public class RmvToObjExporter
    {
        private readonly ILogger _logger = Logging.Create<RmvToObjExporter>();

        public ExportSupportEnum CanExportFile(PackFile file)
        {
            var name = file.Name.ToLower();
            if (name.EndsWith(".rigidmodel") || name.EndsWith(".rmv2"))
                return ExportSupportEnum.HighPriority;
            return ExportSupportEnum.NotSupported;
        }

        public void Export(RmvToObjExporterSettings settings)
        {
            try
            {
                _logger.Information($"Exporting RMV to OBJ: {settings.OutputPath}");

                var rmv2 = new ModelFactory().Load(settings.InputModelFile.DataSource.ReadData());
                var lodLevel = rmv2.ModelList.First();

                var sb = new StringBuilder();
                sb.AppendLine("# OBJ file exported from Total War RigidModel V2");
                sb.AppendLine($"# Model: {settings.InputModelFile.Name}");
                sb.AppendLine();

                int globalVertexOffset = 1; // OBJ uses 1-based indexing
                int meshIndex = 0;

                foreach (var rmvModel in lodLevel)
                {
                    sb.AppendLine($"# Mesh {meshIndex}");
                    sb.AppendLine($"o Mesh_{meshIndex}");
                    sb.AppendLine($"usemtl {rmvModel.Material.ModelName}_Material");
                    sb.AppendLine();

                    var mesh = rmvModel.Mesh;
                    var vertices = mesh.VertexList;

                    // Write vertices
                    foreach (var vertex in vertices)
                    {
                        var pos = vertex.GetPosistionAsVec3();
                        sb.AppendLine($"v {pos.X.ToString(CultureInfo.InvariantCulture)} {pos.Y.ToString(CultureInfo.InvariantCulture)} {pos.Z.ToString(CultureInfo.InvariantCulture)}");
                    }

                    sb.AppendLine();

                    // Write normals
                    foreach (var vertex in vertices)
                    {
                        var normal = vertex.Normal;
                        sb.AppendLine($"vn {normal.X.ToString(CultureInfo.InvariantCulture)} {normal.Y.ToString(CultureInfo.InvariantCulture)} {normal.Z.ToString(CultureInfo.InvariantCulture)}");
                    }

                    sb.AppendLine();

                    // Write UVs
                    foreach (var vertex in vertices)
                    {
                        var uv = vertex.Uv;
                        sb.AppendLine($"vt {uv.X.ToString(CultureInfo.InvariantCulture)} {(1.0f - uv.Y).ToString(CultureInfo.InvariantCulture)}");
                    }

                    sb.AppendLine();

                    // Write faces (indices)
                    var indices = mesh.IndexList;
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int i0 = indices[i] + globalVertexOffset;
                        int i1 = indices[i + 1] + globalVertexOffset;
                        int i2 = indices[i + 2] + globalVertexOffset;

                        // Format: f v/vt/vn v/vt/vn v/vt/vn
                        sb.AppendLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
                    }

                    sb.AppendLine();
                    globalVertexOffset += vertices.Length;
                    meshIndex++;
                }

                // Write basic MTL file
                var mtlPath = Path.Combine(Path.GetDirectoryName(settings.OutputPath)!, 
                    Path.GetFileNameWithoutExtension(settings.OutputPath) + ".mtl");
                WriteMaterialFile(lodLevel, mtlPath);

                File.WriteAllText(settings.OutputPath, sb.ToString(), Encoding.UTF8);
                _logger.Information($"Successfully exported to {settings.OutputPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting RMV to OBJ");
                throw;
            }
        }

        private void WriteMaterialFile(RmvModel[] lodLevel, string mtlPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# MTL file for OBJ export");
            sb.AppendLine();

            foreach (var rmvModel in lodLevel)
            {
                var materialName = rmvModel.Material.ModelName + "_Material";
                sb.AppendLine($"newmtl {materialName}");
                sb.AppendLine("Ka 1.0 1.0 1.0");
                sb.AppendLine("Kd 0.8 0.8 0.8");
                sb.AppendLine("Ks 0.5 0.5 0.5");
                sb.AppendLine("Ns 32.0");
                sb.AppendLine("illum 2");
                sb.AppendLine();
            }

            File.WriteAllText(mtlPath, sb.ToString(), Encoding.UTF8);
        }
    }

    public class RmvToObjExporterSettings
    {
        public PackFile InputModelFile { get; set; }
        public string OutputPath { get; set; }
    }
}
