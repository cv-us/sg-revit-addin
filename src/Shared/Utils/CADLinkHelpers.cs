using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Utils
{
    /// <summary>
    /// Helpers for extracting geometry from linked CAD files (DWG/DXF).
    ///
    /// Linked CAD files in Revit are ImportInstance elements. Their geometry
    /// is organized by layers (represented as GraphicsStyle objects).
    ///
    /// USAGE:
    ///   // Get all CAD links in the document
    ///   var links = CADLinkHelpers.GetCADLinks(doc);
    ///
    ///   // Get layer names and their line counts
    ///   var layers = CADLinkHelpers.GetLayersWithCurves(cadLink);
    ///
    ///   // Extract lines from specific layers
    ///   var lines = CADLinkHelpers.GetCurvesFromLayers(cadLink, selectedLayerNames);
    /// </summary>
    public static class CADLinkHelpers
    {
        /// <summary>
        /// Get all linked CAD (ImportInstance) elements in the document.
        /// </summary>
        public static IList<ImportInstance> GetCADLinks(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Where(i => !i.IsLinked || i.IsLinked) // all imports
                .ToList();
        }

        /// <summary>
        /// Get all layer names from a CAD link that contain line/curve geometry,
        /// along with the count of curves per layer.
        /// </summary>
        public static Dictionary<string, int> GetLayersWithCurves(ImportInstance cadLink)
        {
            var layerCounts = new Dictionary<string, int>();
            GeometryElement geoElem = cadLink.get_Geometry(new Options());
            if (geoElem == null) return layerCounts;

            foreach (GeometryObject geoObj in geoElem)
            {
                if (geoObj is GeometryInstance geoInst)
                {
                    GeometryElement instGeo = geoInst.GetInstanceGeometry();
                    foreach (GeometryObject obj in instGeo)
                    {
                        if (obj is Curve || obj is PolyLine)
                        {
                            string layerName = GetLayerName(cadLink.Document, obj);
                            if (string.IsNullOrEmpty(layerName)) continue;

                            if (layerCounts.ContainsKey(layerName))
                                layerCounts[layerName]++;
                            else
                                layerCounts[layerName] = 1;
                        }
                    }
                }
            }

            return layerCounts;
        }

        /// <summary>
        /// Extract all curves from specified layers of a CAD link.
        /// Curves are returned in the Revit model's coordinate system
        /// (the CAD link's transform is already applied by GetInstanceGeometry).
        /// </summary>
        /// <param name="cadLink">The linked CAD ImportInstance</param>
        /// <param name="layerNames">Which layers to extract curves from</param>
        /// <param name="minLength">Minimum curve length in feet (skip short lines)</param>
        public static IList<Line> GetLinesFromLayers(
            ImportInstance cadLink,
            HashSet<string> layerNames,
            double minLength = 0)
        {
            var lines = new List<Line>();
            GeometryElement geoElem = cadLink.get_Geometry(new Options());
            if (geoElem == null) return lines;

            foreach (GeometryObject geoObj in geoElem)
            {
                if (geoObj is GeometryInstance geoInst)
                {
                    GeometryElement instGeo = geoInst.GetInstanceGeometry();
                    foreach (GeometryObject obj in instGeo)
                    {
                        string layerName = GetLayerName(cadLink.Document, obj);
                        if (!layerNames.Contains(layerName)) continue;

                        if (obj is Line line)
                        {
                            if (minLength > 0 && line.Length < minLength) continue;
                            lines.Add(line);
                        }
                        else if (obj is PolyLine polyLine)
                        {
                            // Convert polyline segments to individual lines
                            IList<XYZ> coords = polyLine.GetCoordinates();
                            for (int i = 0; i < coords.Count - 1; i++)
                            {
                                if (coords[i].DistanceTo(coords[i + 1]) < 0.001) continue;
                                Line seg = Line.CreateBound(coords[i], coords[i + 1]);
                                if (minLength > 0 && seg.Length < minLength) continue;
                                lines.Add(seg);
                            }
                        }
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// Get the CAD layer name for a geometry object by reading its GraphicsStyle.
        /// </summary>
        private static string GetLayerName(Document doc, GeometryObject geoObj)
        {
            ElementId styleId = geoObj.GraphicsStyleId;
            if (styleId == null || styleId == ElementId.InvalidElementId) return null;

            GraphicsStyle style = doc.GetElement(styleId) as GraphicsStyle;
            return style?.GraphicsStyleCategory?.Name;
        }
    }
}
