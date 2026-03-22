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

        var rooms = new FilteredElementCollector(doc, activeView.Id)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<SpatialElement>()
            .OfType<Room>()
            .Where(room => room.Area > 0)
            .ToList();

        if (rooms.Count == 0)
        {
            TaskDialog.Show("Auto Room Dimension", "No placed rooms found in the active view.");
            return Result.Cancelled;
        }

        int createdDimensions = 0;
        using Transaction tx = new(doc, "Auto dimension rooms");
        tx.Start();

        foreach (Room room in rooms)
        {
            createdDimensions += TryDimensionRoom(doc, activeView, room);
        }

        tx.Commit();

        TaskDialog.Show(
            "Auto Room Dimension",
            $"Processed {rooms.Count} room(s). Created {createdDimensions} dimension(s)."
        );

        return Result.Succeeded;
    }

    private static int TryDimensionRoom(Document doc, View view, Room room)
    {
        if (room.Location is not LocationPoint locationPoint)
        {
            return 0;
        }

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
            Element? boundaryElement = doc.GetElement(segment.ElementId);
            Curve boundaryCurve = segment.GetCurve();
            XYZ curveDirection = (boundaryCurve.GetEndPoint(1) - boundaryCurve.GetEndPoint(0)).Normalize();
            XYZ curveNormal = new(-curveDirection.Y, curveDirection.X, 0);

            if (boundaryElement is Wall wall)
            {
                foreach (Reference faceReference in HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior))
                {
                    if (wall.GetGeometryObjectFromReference(faceReference) is not PlanarFace face)
                    {
                        continue;
                    }

                    AddReference(faceReference, face.FaceNormal, doc, seenReferences, xDirectionReferences, yDirectionReferences);
                }

                foreach (ElementId insertId in wall.FindInserts(true, true, true, true))
                {
                    Element? insertElement = doc.GetElement(insertId);
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
                            XYZ referenceNormal = curveNormal;
                            if (instance.GetGeometryObjectFromReference(instanceReference) is PlanarFace instanceFace)
                            {
                                referenceNormal = instanceFace.FaceNormal;
                            }

                            AddReference(
                                instanceReference,
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
                    AddReference(separatorReference, curveNormal, doc, seenReferences, xDirectionReferences, yDirectionReferences);
                }
            }
            else if (boundaryCurve.Reference != null)
            {
                AddReference(boundaryCurve.Reference, curveNormal, doc, seenReferences, xDirectionReferences, yDirectionReferences);
            }
        }

        int created = 0;
        created += CreateDimensionIfPossible(doc, view, locationPoint.Point, xDirectionReferences, XYZ.BasisX);
        created += CreateDimensionIfPossible(doc, view, locationPoint.Point, yDirectionReferences, XYZ.BasisY);
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
            return 0;
        }
    }
}
