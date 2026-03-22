using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RoomAutoDimension;

[Transaction(TransactionMode.Manual)]
public class RoomAutoDimensionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uiDoc = commandData.Application.ActiveUIDocument;
        Document doc = uiDoc.Document;
        View activeView = doc.ActiveView;

        if (activeView.ViewType is not (ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan))
        {
            TaskDialog.Show("Auto Room Dimension", "Run this command in a plan view.");
            return Result.Cancelled;
        }

        var roomsToProcess = new List<(Room room, RevitLinkInstance? linkInstance)>();

        var hostRooms = new FilteredElementCollector(doc, activeView.Id)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<SpatialElement>()
            .OfType<Room>()
            .Where(room => room.Area > 0)
            .ToList();

        roomsToProcess.AddRange(hostRooms.Select(room => (room, (RevitLinkInstance?)null)));

        foreach (RevitLinkInstance linkInstance in new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>())
        {
            Document? linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null)
            {
                continue;
            }

            foreach (Room room in new FilteredElementCollector(linkedDoc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<SpatialElement>()
                .OfType<Room>()
                .Where(room => room.Area > 0))
            {
                roomsToProcess.Add((room, linkInstance));
            }
        }

        if (roomsToProcess.Count == 0)
        {
            TaskDialog.Show("Auto Room Dimension", "No placed rooms found in the active view or linked files.");
            return Result.Cancelled;
        }

        int createdDimensions = 0;
        using Transaction tx = new(doc, "Auto dimension rooms");
        tx.Start();

        foreach ((Room room, RevitLinkInstance? linkInstance) in roomsToProcess)
        {
            createdDimensions += TryDimensionRoom(doc, activeView, room, linkInstance);
        }

        tx.Commit();

        TaskDialog.Show(
            "Auto Room Dimension",
            $"Processed {roomsToProcess.Count} room(s). Created {createdDimensions} dimension(s)."
        );

        return Result.Succeeded;
    }

    private static int TryDimensionRoom(Document doc, View view, Room room, RevitLinkInstance? linkInstance = null)
    {
        if (room.Location is not LocationPoint locationPoint)
        {
            return 0;
        }

        Document elementDoc = linkInstance?.GetLinkDocument() ?? doc;
        Transform? linkTransform = linkInstance?.GetTotalTransform();

        SpatialElementBoundaryOptions options = new()
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
        };

        IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(options);
        if (loops == null || loops.Count == 0)
        {
            return 0;
        }

        var xDirectionReferences = new List<Reference>();
        var yDirectionReferences = new List<Reference>();
        HashSet<string> seenReferences = new();

        foreach (BoundarySegment segment in loops.SelectMany(loop => loop))
        {
            Element? boundaryElement = elementDoc.GetElement(segment.ElementId);
            Curve boundaryCurve = segment.GetCurve();
            XYZ curveDirection = (boundaryCurve.GetEndPoint(1) - boundaryCurve.GetEndPoint(0)).Normalize();
            XYZ curveNormal = new(-curveDirection.Y, curveDirection.X, 0);
            XYZ worldCurveNormal = linkTransform != null ? linkTransform.OfVector(curveNormal) : curveNormal;

            if (boundaryElement is Wall wall)
            {
                foreach (Reference faceReference in HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior))
                {
                    if (wall.GetGeometryObjectFromReference(faceReference) is not PlanarFace face)
                    {
                        continue;
                    }

                    XYZ faceNormal = linkTransform != null ? linkTransform.OfVector(face.FaceNormal) : face.FaceNormal;
                    Reference hostReference = linkInstance != null ? faceReference.CreateLinkReference(linkInstance) : faceReference;
                    AddReference(hostReference, faceNormal, doc, seenReferences, xDirectionReferences, yDirectionReferences);
                }

                foreach (ElementId insertId in wall.FindInserts(true, true, true, true))
                {
                    Element? insertElement = elementDoc.GetElement(insertId);
                    if (insertElement is not FamilyInstance instance)
                    {
                        continue;
                    }

                    if (instance.Category?.Id.IntegerValue is not (
                        (int)BuiltInCategory.OST_Doors or
                        (int)BuiltInCategory.OST_Windows))
                    {
                        continue;
                    }

                    foreach (FamilyInstanceReferenceType referenceType in new[]
                    {
                        FamilyInstanceReferenceType.Left,
                        FamilyInstanceReferenceType.Right,
                        FamilyInstanceReferenceType.Front,
                        FamilyInstanceReferenceType.Back,
                    })
                    {
                        foreach (Reference instanceReference in instance.GetReferences(referenceType))
                        {
                            XYZ referenceNormal = worldCurveNormal;
                            if (instance.GetGeometryObjectFromReference(instanceReference) is PlanarFace instanceFace)
                            {
                                referenceNormal = linkTransform != null
                                    ? linkTransform.OfVector(instanceFace.FaceNormal)
                                    : instanceFace.FaceNormal;
                            }

                            Reference hostReference = linkInstance != null
                                ? instanceReference.CreateLinkReference(linkInstance)
                                : instanceReference;
                            AddReference(
                                hostReference,
                                referenceNormal,
                                doc,
                                seenReferences,
                                xDirectionReferences,
                                yDirectionReferences
                            );
                        }
                    }
                }
            }
            else if (boundaryElement is CurveElement curveElement)
            {
                Reference? separatorReference = curveElement.GeometryCurve.Reference ?? boundaryCurve.Reference;
                if (separatorReference != null)
                {
                    Reference hostReference = linkInstance != null ? separatorReference.CreateLinkReference(linkInstance) : separatorReference;
                    AddReference(hostReference, worldCurveNormal, doc, seenReferences, xDirectionReferences, yDirectionReferences);
                }
            }
            else if (boundaryCurve.Reference != null)
            {
                Reference hostReference = linkInstance != null ? boundaryCurve.Reference.CreateLinkReference(linkInstance) : boundaryCurve.Reference;
                AddReference(hostReference, worldCurveNormal, doc, seenReferences, xDirectionReferences, yDirectionReferences);
            }
        }

        XYZ origin = linkTransform?.OfPoint(locationPoint.Point) ?? locationPoint.Point;

        int created = 0;
        created += CreateDimensionIfPossible(doc, view, origin, xDirectionReferences, XYZ.BasisX);
        created += CreateDimensionIfPossible(doc, view, origin, yDirectionReferences, XYZ.BasisY);
        return created;
    }

    private static void AddReference(
        Reference reference,
        XYZ normal,
        Document doc,
        ISet<string> seenReferences,
        ICollection<Reference> xDirectionReferences,
        ICollection<Reference> yDirectionReferences)
    {
        string stableReference = reference.ConvertToStableRepresentation(doc);
        if (!seenReferences.Add(stableReference))
        {
            return;
        }

        XYZ normalized = normal.Normalize();
        if (Math.Abs(normalized.X) >= Math.Abs(normalized.Y))
        {
            xDirectionReferences.Add(reference);
        }
        else
        {
            yDirectionReferences.Add(reference);
        }
    }

    private static int CreateDimensionIfPossible(
        Document doc,
        View view,
        XYZ origin,
        IReadOnlyCollection<Reference> refs,
        XYZ lineDirection)
    {
        if (refs.Count < 2)
        {
            return 0;
        }

        ReferenceArray referenceArray = new();
        foreach (Reference reference in refs)
        {
            referenceArray.Append(reference);
        }

        if (referenceArray.Size < 2)
        {
            return 0;
        }

        Line dimensionLine = Line.CreateBound(
            origin - lineDirection.Multiply(100),
            origin + lineDirection.Multiply(100)
        );

        try
        {
            doc.Create.NewDimension(view, dimensionLine, referenceArray);
            return 1;
        }
        catch
        {
            // NewDimension can throw if references are not parallel or not stable in the
            // current view (e.g. rotated linked-model references that Revit cannot resolve).
            // Silently skip the failed dimension rather than aborting the whole command.
            return 0;
        }
    }
}
