using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.PipeRouting
{
    /// <summary>
    /// Replaces selected flex pipes with new shortest-length flex pipes between
    /// the same endpoint elements. The original flex pipe may have excess length
    /// from manual routing; this command creates a direct (minimum-length)
    /// connection using the same flex pipe type.
    ///
    /// WORKFLOW:
    ///   1. User selects flex pipes (pre-selection or pick)
    ///   2. For each flex pipe, identify the two connected endpoint elements
    ///   3. Filter: must have exactly 2 connections, none to PipingSystem objects
    ///   4. Store the flex pipe type and endpoint connector info
    ///   5. Delete the original flex pipes
    ///   6. Create new shortest-length flex pipes between the same endpoints
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShortenFlexPipesCommand : IExternalCommand
    {
        /// <summary>
        /// Data captured from each valid flex pipe before deletion.
        /// </summary>
        private class FlexPipeRecord
        {
            public ElementId FlexPipeId { get; set; }
            public ElementId TypeId { get; set; }
            public ElementId SystemTypeId { get; set; }
            public ElementId LevelId { get; set; }
            public ElementId EndpointAId { get; set; }
            public int ConnectorAIndex { get; set; }
            public XYZ ConnectorAOrigin { get; set; }
            public ElementId EndpointBId { get; set; }
            public int ConnectorBIndex { get; set; }
            public XYZ ConnectorBOrigin { get; set; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Get selected flex pipes (pre-selection or pick) ──
            List<FlexPipe> flexPipes = GetSelectedFlexPipes(uidoc);
            if (flexPipes == null)
                return Result.Cancelled;

            if (flexPipes.Count == 0)
            {
                TaskDialog.Show("Shorten Flex Pipes",
                    "No flex pipes found in the current selection.\n\n" +
                    "Select one or more flex pipes and run the command again, " +
                    "or run the command with nothing selected to pick them.");
                return Result.Failed;
            }

            // ── Analyze connections and build records ──
            var records = new List<FlexPipeRecord>();
            int skippedNotTwo = 0;
            int skippedPipingSystem = 0;

            foreach (var fp in flexPipes)
            {
                var connectorInfo = GetEndpointConnectors(fp);
                if (connectorInfo == null)
                {
                    skippedNotTwo++;
                    continue;
                }

                if (connectorInfo.Value.hasPipingSystem)
                {
                    skippedPipingSystem++;
                    continue;
                }

                records.Add(new FlexPipeRecord
                {
                    FlexPipeId = fp.Id,
                    TypeId = fp.GetTypeId(),
                    SystemTypeId = fp.MEPSystem?.GetTypeId() ?? ElementId.InvalidElementId,
                    LevelId = fp.ReferenceLevel?.Id ?? ElementId.InvalidElementId,
                    EndpointAId = connectorInfo.Value.endpointAId,
                    ConnectorAIndex = connectorInfo.Value.connectorAIndex,
                    ConnectorAOrigin = new XYZ(connectorInfo.Value.connectorAOrigin.X,
                                               connectorInfo.Value.connectorAOrigin.Y,
                                               connectorInfo.Value.connectorAOrigin.Z),
                    EndpointBId = connectorInfo.Value.endpointBId,
                    ConnectorBIndex = connectorInfo.Value.connectorBIndex,
                    ConnectorBOrigin = new XYZ(connectorInfo.Value.connectorBOrigin.X,
                                               connectorInfo.Value.connectorBOrigin.Y,
                                               connectorInfo.Value.connectorBOrigin.Z)
                });
            }

            if (records.Count == 0)
            {
                string reason = "";
                if (skippedNotTwo > 0)
                    reason += $"\n• {skippedNotTwo} pipe(s) did not have exactly 2 connections";
                if (skippedPipingSystem > 0)
                    reason += $"\n• {skippedPipingSystem} pipe(s) were connected to system elements";

                TaskDialog.Show("Shorten Flex Pipes",
                    $"No valid flex pipes to process.{reason}");
                return Result.Failed;
            }

            // ── Confirm ──
            string confirmMsg = $"Replace {records.Count} flex pipe{(records.Count != 1 ? "s" : "")} " +
                                "with shortest-length connections?";
            if (skippedNotTwo + skippedPipingSystem > 0)
                confirmMsg += $"\n\n({skippedNotTwo + skippedPipingSystem} pipe(s) will be skipped due to invalid connections.)";

            TaskDialogResult confirm = TaskDialog.Show("Shorten Flex Pipes", confirmMsg,
                TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);
            if (confirm != TaskDialogResult.Ok)
                return Result.Cancelled;

            // ── Execute in transaction ──
            int replacedCount = 0;
            int failedCount = 0;

            using (var tw = new TransactionWrapper(doc, "Shorten Flex Pipes"))
            {
                try
                {
                    // Delete all original flex pipes first
                    foreach (var rec in records)
                    {
                        try
                        {
                            doc.Delete(rec.FlexPipeId);
                        }
                        catch
                        {
                            failedCount++;
                        }
                    }

                    // Regenerate so connectors on endpoint elements become available
                    doc.Regenerate();

                    // Create new shortest-length flex pipes
                    foreach (var rec in records)
                    {
                        try
                        {
                            Connector connA = FindConnector(doc, rec.EndpointAId, rec.ConnectorAIndex);
                            Connector connB = FindConnector(doc, rec.EndpointBId, rec.ConnectorBIndex);

                            if (connA == null || connB == null)
                            {
                                failedCount++;
                                continue;
                            }

                            // Ensure we have a valid level
                            ElementId levelId = rec.LevelId;
                            if (levelId == null || levelId == ElementId.InvalidElementId)
                                levelId = FindNearestLevel(doc, connA.Origin.Z);

                            // Create shortest-path flex pipe with just two points
                            var points = new List<XYZ> { connA.Origin, connB.Origin };

                            // Get system type — use stored if available, else find one
                            ElementId sysTypeId = rec.SystemTypeId;
                            if (sysTypeId == null || sysTypeId == ElementId.InvalidElementId)
                                sysTypeId = GetDefaultPipingSystemTypeId(doc);

                            FlexPipe newPipe = FlexPipe.Create(doc, sysTypeId, rec.TypeId, levelId, points);

                            // Connect the new flex pipe's ends to the endpoint elements
                            ConnectFlexPipeEnds(newPipe, connA, connB);
                            replacedCount++;
                        }
                        catch
                        {
                            failedCount++;
                        }
                    }

                    tw.Commit();
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                    return Result.Failed;
                }
            }

            // ── Summary ──
            string summary = $"Replaced {replacedCount} flex pipe{(replacedCount != 1 ? "s" : "")} " +
                             "with shortest-length connections.";
            if (failedCount > 0)
                summary += $"\n{failedCount} pipe(s) could not be replaced.";
            if (skippedNotTwo + skippedPipingSystem > 0)
                summary += $"\n{skippedNotTwo + skippedPipingSystem} pipe(s) skipped (invalid connections).";

            TaskDialog.Show("Shorten Flex Pipes", summary);
            return Result.Succeeded;
        }

        /// <summary>
        /// Gets flex pipes from pre-selection or prompts user to pick them.
        /// Returns null if user cancels picking.
        /// </summary>
        private List<FlexPipe> GetSelectedFlexPipes(UIDocument uidoc)
        {
            Document doc = uidoc.Document;

            // Check pre-selection
            var preSelected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<FlexPipe>()
                .ToList();

            if (preSelected.Count > 0)
                return preSelected;

            // Prompt user to pick
            try
            {
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new FlexPipeSelectionFilter(),
                    "Select flex pipes to shorten, then press Finish.");

                return refs
                    .Select(r => doc.GetElement(r.ElementId))
                    .OfType<FlexPipe>()
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// For a flex pipe, finds the two endpoint connectors on the connected elements.
        /// Returns null if the pipe doesn't have exactly 2 valid connections.
        /// </summary>
        private (ElementId endpointAId, int connectorAIndex, XYZ connectorAOrigin,
                 ElementId endpointBId, int connectorBIndex, XYZ connectorBOrigin,
                 bool hasPipingSystem)?
            GetEndpointConnectors(FlexPipe flexPipe)
        {
            ConnectorSet connectors = flexPipe.ConnectorManager?.Connectors;
            if (connectors == null)
                return null;

            // Collect connected endpoint connectors (on the OTHER elements)
            var endpointConnectors = new List<(ElementId elementId, int connectorIndex, XYZ origin, bool isPipingSystem)>();

            foreach (Connector conn in connectors)
            {
                if (!conn.IsConnected)
                    continue;

                foreach (Connector linked in conn.AllRefs)
                {
                    // Skip self-references
                    if (linked.Owner.Id == flexPipe.Id)
                        continue;

                    bool isPipingSystem = linked.Owner is Autodesk.Revit.DB.Plumbing.PipingSystem;
                    endpointConnectors.Add((linked.Owner.Id, linked.Id, linked.Origin, isPipingSystem));
                }
            }

            // Must have exactly 2 endpoint connections
            if (endpointConnectors.Count != 2)
                return null;

            bool hasPipingSystem = endpointConnectors.Any(e => e.isPipingSystem);

            return (endpointConnectors[0].elementId, endpointConnectors[0].connectorIndex, endpointConnectors[0].origin,
                    endpointConnectors[1].elementId, endpointConnectors[1].connectorIndex, endpointConnectors[1].origin,
                    hasPipingSystem);
        }

        /// <summary>
        /// Finds a specific connector on an element by connector index/Id.
        /// Falls back to finding the first unconnected connector if exact match fails.
        /// </summary>
        private Connector FindConnector(Document doc, ElementId elementId, int connectorIndex)
        {
            Element element = doc.GetElement(elementId);
            if (element == null)
                return null;

            ConnectorManager connMgr = GetConnectorManager(element);
            if (connMgr == null)
                return null;

            // Try to find by connector Id first
            foreach (Connector conn in connMgr.Connectors)
            {
                if (conn.Id == connectorIndex)
                    return conn;
            }

            // Fallback: find any unconnected connector (after flex pipe was deleted)
            foreach (Connector conn in connMgr.Connectors)
            {
                if (!conn.IsConnected)
                    return conn;
            }

            return null;
        }

        /// <summary>
        /// Gets the ConnectorManager from various element types.
        /// </summary>
        private ConnectorManager GetConnectorManager(Element element)
        {
            if (element is FamilyInstance fi)
                return fi.MEPModel?.ConnectorManager;

            if (element is MEPCurve curve)
                return curve.ConnectorManager;

            return null;
        }

        /// <summary>
        /// Finds the nearest level at or below a given elevation.
        /// </summary>
        private ElementId FindNearestLevel(Document doc, double elevation)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderByDescending(l => l.Elevation)
                .ToList();

            foreach (var level in levels)
            {
                if (level.Elevation <= elevation + 0.01)
                    return level.Id;
            }

            return levels.LastOrDefault()?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Gets a default piping system type ID from the document.
        /// </summary>
        private ElementId GetDefaultPipingSystemTypeId(Document doc)
        {
            var systemType = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipingSystemType))
                .FirstOrDefault();
            return systemType?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Connects the new flex pipe's endpoints to the original endpoint connectors.
        /// Matches by proximity to the stored connector origins.
        /// </summary>
        private void ConnectFlexPipeEnds(FlexPipe newPipe, Connector endpointA, Connector endpointB)
        {
            ConnectorSet newConnectors = newPipe.ConnectorManager?.Connectors;
            if (newConnectors == null)
                return;

            // Find the two end connectors on the new flex pipe (not logical connectors)
            var pipeEnds = new List<Connector>();
            foreach (Connector c in newConnectors)
            {
                if (c.ConnectorType == ConnectorType.End)
                    pipeEnds.Add(c);
            }

            if (pipeEnds.Count < 2)
                return;

            // Match pipe end connectors to endpoint connectors by proximity
            double distToA0 = pipeEnds[0].Origin.DistanceTo(endpointA.Origin);
            double distToA1 = pipeEnds[1].Origin.DistanceTo(endpointA.Origin);

            Connector pipeEndForA, pipeEndForB;
            if (distToA0 <= distToA1)
            {
                pipeEndForA = pipeEnds[0];
                pipeEndForB = pipeEnds[1];
            }
            else
            {
                pipeEndForA = pipeEnds[1];
                pipeEndForB = pipeEnds[0];
            }

            // Connect
            try { pipeEndForA.ConnectTo(endpointA); } catch { }
            try { pipeEndForB.ConnectTo(endpointB); } catch { }
        }

        /// <summary>
        /// Selection filter that only allows flex pipes.
        /// </summary>
        private class FlexPipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is FlexPipe;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
