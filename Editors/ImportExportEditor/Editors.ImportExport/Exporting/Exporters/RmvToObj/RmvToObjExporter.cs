using System.Globalization;
using System.IO;
using System.Text;
using Editors.ImportExport.Exporting.Exporters.DdsToMaterialPng;
using Editors.ImportExport.Exporting.Exporters.DdsToNormalPng;
using Editors.ImportExport.Misc;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Types;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Models;
using System.Drawing;
using System.Drawing.Imaging;

namespace Editors.ImportExport.Exporting.Exporters
{
    public class RmvToObjExporter
    {
        private readonly ILogger _logger = Logging.Create<RmvToObjExporter>();
        private readonly IDdsToNormalPngExporter _ddsToNormalPngExporter;
        private readonly IDdsToMaterialPngExporter _ddsToMaterialPngExporter;

        public RmvToObjExporter(IDdsToNormalPngExporter ddsToNormalPngExporter, IDdsToMaterialPngExporter ddsToMaterialPngExporter)
        {
            _ddsToNormalPngExporter = ddsToNormalPngExporter;
            _ddsToMaterialPngExporter = ddsToMaterialPngExporter;
        }

        public ExportSupportEnum CanExportFile(PackFile file)
        {
            var name = file.Name.ToLower();
            if (name.EndsWith(".rigidmodel") || name.EndsWith(".rmv2") || name.EndsWith(".rigid_model_v2"))
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
                var outputDir = Path.GetDirectoryName(settings.OutputPath)!;
                var baseName = Path.GetFileNameWithoutExtension(settings.OutputPath);

                var sb = new StringBuilder();
                sb.AppendLine("# OBJ file exported from Total War RigidModel V2");
                sb.AppendLine($"# Model: {settings.InputModelFile.Name}");
                sb.AppendLine();

                int globalVertexOffset = 1; // OBJ uses 1-based indexing
                int meshIndex = 0;

                foreach (var rmvModel in lodLevel)
                {
                    var meshName = rmvModel.Material.ModelName;
                    sb.AppendLine($"# Mesh {meshName}");
                    sb.AppendLine($"o {meshName}");
                    sb.AppendLine($"usemtl {meshName}_Material");
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

                // Write OBJ file
                File.WriteAllText(settings.OutputPath, sb.ToString(), Encoding.UTF8);

                // Write MTL file with texture references
                var mtlPath = Path.Combine(outputDir, baseName + ".mtl");
                WriteMaterialFile(lodLevel, mtlPath, outputDir, baseName);

                _logger.Information($"Successfully exported to {settings.OutputPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting RMV to OBJ");
                throw;
            }
        }

        private void WriteMaterialFile(RmvModel[] lodLevel, string mtlPath, string outputDir, string baseName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# MTL file for OBJ export");
            sb.AppendLine();

            int meshIndex = 0;
            foreach (var rmvModel in lodLevel)
            {
                var materialName = rmvModel.Material.ModelName + "_Material";
                sb.AppendLine($"newmtl {materialName}");
                sb.AppendLine("Ka 1.0 1.0 1.0");
                sb.AppendLine("Kd 0.8 0.8 0.8");
                sb.AppendLine("Ks 0.5 0.5 0.5");
                sb.AppendLine("Ns 32.0");
                sb.AppendLine("illum 2");

                // Export textures and add references to MTL
                var normalTexture = rmvModel.Material.GetTexture(TextureType.Normal);
                if (normalTexture != null)
                {
                    try
                    {
                        var normalMapPath = ExportNormalMapAndCreateDisplacementMap(normalTexture, outputDir, rmvModel.Material.ModelName, meshIndex);
                        if (!string.IsNullOrEmpty(normalMapPath))
                        {
                            var normalFileName = Path.GetFileName(normalMapPath);
                            var displacementFileName = Path.Combine(outputDir,
                                Path.GetFileNameWithoutExtension(normalMapPath) + "_displacement.png");
                            var displacementFileNameOnly = Path.GetFileName(displacementFileName);

                            sb.AppendLine($"map_bump {normalFileName}");
                            sb.AppendLine($"disp {displacementFileNameOnly}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to export normal map for mesh {MeshIndex}", meshIndex);
                    }
                }
                var diffuseTexture = rmvModel.Material.GetTexture(TextureType.Diffuse) ??
                                     rmvModel.Material.GetTexture(TextureType.BaseColour);
                if (diffuseTexture != null)
                {
                    try
                    {
                        var diffuseMapPath = ExportDiffuseMap(diffuseTexture, outputDir, rmvModel.Material.ModelName, meshIndex);
                        if (!string.IsNullOrEmpty(diffuseMapPath))
                        {
                            var diffuseFileName = Path.GetFileName(diffuseMapPath);
                            sb.AppendLine($"map_Kd {diffuseFileName}");
                            // Also set Kd to use the diffuse texture color
                            sb.AppendLine($"Kd 1.0 1.0 1.0");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to export diffuse map for mesh {MeshIndex}", meshIndex);
                    }
                }

                sb.AppendLine();
                meshIndex++;
            }

            File.WriteAllText(mtlPath, sb.ToString(), Encoding.UTF8);
        }

        private string ExportNormalMapAndCreateDisplacementMap(RmvTexture? texture, string outputDir, string meshName, int meshIndex)
        {
            if (!texture.HasValue)
                return null;

            try
            {
                // Create temporary path for PNG export
                var normalMapFileName = $"{meshName}_normal.png";
                var normalMapPath = Path.Combine(outputDir, normalMapFileName);

                // Export DDS to PNG using existing exporter
                var exportedPath = _ddsToNormalPngExporter.Export(texture.Value.Path, normalMapPath, convertToBlueNormalMap: true);

                if (File.Exists(exportedPath))
                {
                    // Load the normal map and create displacement map
                    using (var normalImage = new Bitmap(exportedPath))
                    {
                        var displacementMap = ConvertNormalMapToHeightMap(normalImage);
                        var displacementPath = Path.Combine(outputDir, 
                            Path.GetFileNameWithoutExtension(normalMapPath) + "_displacement.png");
                        displacementMap.Save(displacementPath, ImageFormat.Png);
                        displacementMap.Dispose();
                    }

                    return exportedPath;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting normal map for mesh {MeshIndex}", meshIndex);
            }

            return null;
        }

        private string ExportDiffuseMap(RmvTexture? texture, string outputDir, string meshName, int meshIndex)
        {
            if (!texture.HasValue)
                return null;

            try
            {
                var diffuseMapFileName = $"{meshName}_diffuse.png";
                var diffuseMapPath = Path.Combine(outputDir, diffuseMapFileName);

                // Export DDS to PNG using existing exporter
                var exportedPath = _ddsToMaterialPngExporter.Export(texture.Value.Path, diffuseMapPath, convertToBlenderFormat: true);
                return exportedPath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting diffuse map for mesh {MeshIndex}", meshIndex);
            }

            return null;
        }

        /// <summary>
        /// Converts a normal map to a height/displacement map.
        /// Extracts the Z component (blue channel) from the normal map to create height data.
        /// </summary>
        private Bitmap ConvertNormalMapToHeightMap(Bitmap normalMap, float strength = 0.5f, float contrast = 0.0f, int blurRadius = 0)
        {
            // Strength controls how strongly the normal's Z axis affects height (0..1)
            // Contrast adjusts the final curve (-1..1)
            var heightMap = new Bitmap(normalMap.Width, normalMap.Height);

            for (int y = 0; y < normalMap.Height; y++)
            {
                for (int x = 0; x < normalMap.Width; x++)
                {
                    var normal = normalMap.GetPixel(x, y);

                    // Use luminance of the normal as a simpler proxy for displacement
                    // (weights: Rec. 601) and remap around mid-gray with strength.
                    float r = normal.R / 255f;
                    float g = normal.G / 255f;
                    float b = normal.B / 255f;
                    float lum = 0.299f * r + 0.587f * g + 0.114f * b;

                    // Remap so that 0.5 -> mid-gray baseline, and apply strength
                    float h = (lum - 0.5f) * strength + 0.5f;

                    // Apply simple contrast tweak: contrast in [-1,1]
                    if (Math.Abs(contrast) > 0.0001f)
                    {
                        h = 0.5f + (h - 0.5f) * (1f + contrast);
                    }

                    // Clamp and convert
                    h = MathF.Min(1f, MathF.Max(0f, h));
                    byte heightValue = (byte)(h * 255f);

                    var heightColor = Color.FromArgb(normal.A, heightValue, heightValue, heightValue);
                    heightMap.SetPixel(x, y, heightColor);
                }
            }

            // Optional simple box blur (if requested)
            if (blurRadius > 0)
            {
                return BoxBlur(heightMap, blurRadius);
            }

            return heightMap;
        }

        // Very small and simple box blur implementation (separable) to avoid external deps
        private Bitmap BoxBlur(Bitmap src, int radius)
        {
            var w = src.Width;
            var h = src.Height;
            var tmp = new Bitmap(w, h);

            // horizontal pass
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0, count = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = x + k;
                        if (sx < 0 || sx >= w) continue;
                        var c = src.GetPixel(sx, y);
                        r += c.R; g += c.G; b += c.B; a += c.A; count++;
                    }
                    tmp.SetPixel(x, y, Color.FromArgb(a / Math.Max(1, count), r / Math.Max(1, count), g / Math.Max(1, count), b / Math.Max(1, count)));
                }
            }

            var dst = new Bitmap(w, h);
            // vertical pass
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int r = 0, g = 0, b = 0, a = 0, count = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = y + k;
                        if (sy < 0 || sy >= h) continue;
                        var c = tmp.GetPixel(x, sy);
                        r += c.R; g += c.G; b += c.B; a += c.A; count++;
                    }
                    dst.SetPixel(x, y, Color.FromArgb(a / Math.Max(1, count), r / Math.Max(1, count), g / Math.Max(1, count), b / Math.Max(1, count)));
                }
            }

            tmp.Dispose();
            return dst;
        }
    }

    public class RmvToObjExporterSettings
    {
        public PackFile InputModelFile { get; set; }
        public string OutputPath { get; set; }
    }
}
