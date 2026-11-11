// Connectivity and block group tests. Module shapes mirror real entries of the original
// trackunit.rtu (link points in game units, Y pointing down).

using ReVolt.Track;
using System.Collections.Generic;
using System.Linq;

internal static partial class Program
{
    private const int Straight = 0, Corner = 1, Ramp = 2, Jump = 3, Pipe = 4, BigCorner = 5, Start = 6;

    private static ModuleShape Shape(bool pipe, (int, int)[] cells, params (ConnectorPoint, ConnectorPoint)[] zones)
    {
        var shape = new ModuleShape { IsPipe = pipe };
        shape.Cells.AddRange(cells);
        shape.Zones.AddRange(zones);
        return shape;
    }

    private static readonly (int, int)[] OneCell = { (0, 0) };

    private static readonly Dictionary<int, ModuleShape> Shapes = new Dictionary<int, ModuleShape>
    {
        // r_00 / start_grid: straight through, south to north
        [Straight] = Shape(false, OneCell, (new ConnectorPoint(0, 0, -500), new ConnectorPoint(0, 0, 500))),
        [Start] = Shape(false, OneCell, (new ConnectorPoint(0, 0, -500), new ConnectorPoint(0, 0, 500))),
        // c_basic: south to east
        [Corner] = Shape(false, OneCell, (new ConnectorPoint(0, 0, -500), new ConnectorPoint(500, 0, 0))),
        // r_10: the south end is two levels up
        [Ramp] = Shape(false, OneCell, (new ConnectorPoint(0, -250, -500), new ConnectorPoint(0, 0, 500))),
        // jump: the exit launches from far above
        [Jump] = Shape(false, OneCell, (new ConnectorPoint(0, 0, -500), new ConnectorPoint(0, -5000, 500))),
        [Pipe] = Shape(true, OneCell, (new ConnectorPoint(0, 0, -500), new ConnectorPoint(0, 0, 500))),
        // c_l_xt: three zones over eight cells, joined internally
        [BigCorner] = Shape(false, new[] { (0, 0), (1, 0), (2, 0), (0, -1), (1, -1), (2, -1), (0, -2), (1, -2) },
            (new ConnectorPoint(0, 0, -2500), new ConnectorPoint(0, 0, -1500)),
            (new ConnectorPoint(0, 0, -1500), new ConnectorPoint(1500, 0, 0)),
            (new ConnectorPoint(1500, 0, 0), new ConnectorPoint(2500, 0, 0))),
    };

    private static ConnectivityReport Analyze(params PlacedModule[] placements)
        => TrackConnectivity.Analyze(placements, id => Shapes[id], id => id == Start);

    private static void RunConnectivityTests()
    {
        Run(nameof(ClosedSquareHasNoOpenEnds), ClosedSquareHasNoOpenEnds);
        Run(nameof(StraightLineHasTwoOpenEnds), StraightLineHasTwoOpenEnds);
        Run(nameof(HeightDifferenceIsAStep), HeightDifferenceIsAStep);
        Run(nameof(RampJoinsRaisedPiece), RampJoinsRaisedPiece);
        Run(nameof(JumpExitIsNotAStep), JumpExitIsNotAStep);
        Run(nameof(BridgeDeckIsNotALaunchPoint), BridgeDeckIsNotALaunchPoint);
        Run(nameof(PipesNeedSameHeight), PipesNeedSameHeight);
        Run(nameof(CrossroadJoinsBothRoads), CrossroadJoinsBothRoads);
        Run(nameof(BridgeDeckAndRoadBelowStaySeparate), BridgeDeckAndRoadBelowStaySeparate);
        Run(nameof(InternalZonesAreNotCounted), InternalZonesAreNotCounted);
        Run(nameof(SectionsAndStartSection), SectionsAndStartSection);
        Run(nameof(EvaluateReportsConnectsBlocksAndSteps), EvaluateReportsConnectsBlocksAndSteps);
        Run(nameof(BestFitFindsRotationAndLevel), BestFitFindsRotationAndLevel);
        Run(nameof(BestFitReturnsNullWhenNothingImproves), BestFitReturnsNullWhenNothingImproves);
    }

    private static void ClosedSquareHasNoOpenEnds()
    {
        // four c_basic corners forming the smallest possible loop
        var r = Analyze(new PlacedModule(Corner, 0, 0, 3, 0), new PlacedModule(Corner, 1, 0, 2, 0),
                        new PlacedModule(Corner, 1, 1, 1, 0), new PlacedModule(Corner, 0, 1, 0, 0));
        Equal(4, r.ConnectionCount, "connections");
        Equal(0, r.OpenCount, "open ends");
        Equal(1, r.ComponentCount, "sections");
    }

    private static void StraightLineHasTwoOpenEnds()
    {
        var r = Analyze(new PlacedModule(Straight, 0, 0, 0, 0), new PlacedModule(Straight, 0, 1, 0, 0), new PlacedModule(Straight, 0, 2, 0, 0));
        Equal(2, r.ConnectionCount, "connections");
        Equal(2, r.OpenCount, "open ends");
        var facing = r.Joints.Where(j => j.Kind == JointKind.Open).Select(j => (j.A.FacingX, j.A.FacingY)).OrderBy(c => c.Item2).ToList();
        Equal((0, -1), facing[0], "south opening faces");
        Equal((0, 3), facing[1], "north opening faces");

        // rotated a quarter turn the same pieces run west to east
        var turned = Analyze(new PlacedModule(Straight, 0, 0, 1, 0), new PlacedModule(Straight, 1, 0, 1, 0));
        Equal(1, turned.ConnectionCount, "turned connections");
    }

    private static void HeightDifferenceIsAStep()
    {
        var r = Analyze(new PlacedModule(Straight, 0, 0, 0, 0), new PlacedModule(Straight, 0, 1, 0, 2));
        var step = r.Joints.Single(j => j.B != null);
        Equal(JointKind.Step, step.Kind, "kind");
        Equal(2f, step.Levels, "levels");
        Equal(1, step.Upper.Placement, "upper piece");
        Equal(1, r.ComponentCount, "a step still joins the sections");
    }

    private static void RampJoinsRaisedPiece()
    {
        // ramp at ground level rises towards the south, onto a piece two levels up
        var r = Analyze(new PlacedModule(Ramp, 0, 1, 0, 0), new PlacedModule(Straight, 0, 0, 0, 2));
        Equal(1, r.ConnectionCount, "connections");
        Equal(0, r.StepCount, "steps");
    }

    private static void JumpExitIsNotAStep()
    {
        var r = Analyze(new PlacedModule(Jump, 0, 0, 0, 0), new PlacedModule(Straight, 0, 1, 0, 0));
        Equal(1, r.JumpCount, "jumps");
        Equal(0, r.StepCount, "steps");
    }

    private static void BridgeDeckIsNotALaunchPoint()
    {
        // bridge_40: the deck joins track eight levels up; that is a normal connection, not a jump
        var bridge = Shape(false, OneCell, (new ConnectorPoint(0, 0, -500), new ConnectorPoint(0, 0, 500)),
                                           (new ConnectorPoint(500, -1000, 0), new ConnectorPoint(-500, -1000, 0)));
        var shapes = new Dictionary<int, ModuleShape>(Shapes) { [99] = bridge };
        var r = TrackConnectivity.Analyze(new[] { new PlacedModule(99, 1, 0, 0, 0), new PlacedModule(Straight, 2, 0, 1, 0) }, id => shapes[id], null);
        Equal(1, r.StepCount, "ground piece against the deck is a step");
        Equal(0, r.JumpCount, "not a jump");
        var deck = TrackConnectivity.Analyze(new[] { new PlacedModule(99, 1, 0, 0, 0), new PlacedModule(Straight, 2, 0, 1, 8) }, id => shapes[id], null);
        Equal(1, deck.ConnectionCount, "raised piece joins the deck");
    }

    // cross_road: two zones through one cell, north-south and east-west
    private static readonly ModuleShape Crossroad = Shape(false, OneCell,
        (new ConnectorPoint(0, 0, -500), new ConnectorPoint(0, 0, 500)),
        (new ConnectorPoint(500, 0, 0), new ConnectorPoint(-500, 0, 0)));

    private static void CrossroadJoinsBothRoads()
    {
        var shapes = new Dictionary<int, ModuleShape>(Shapes) { [98] = Crossroad };
        var placements = new[]
        {
            new PlacedModule(98, 1, 1, 0, 0),
            new PlacedModule(Straight, 1, 0, 0, 0),  // south
            new PlacedModule(Straight, 1, 2, 0, 0),  // north
            new PlacedModule(Straight, 0, 1, 1, 0),  // west
        };
        var r = TrackConnectivity.Analyze(placements, id => shapes[id], null);
        Equal(3, r.ConnectionCount, "three roads join the crossroad");
        Equal(0, r.Joints.Count(j => j.Internal), "the two roads don't join each other");
        // open: the east side of the crossroad and the far ends of the three straights
        Equal(4, r.OpenCount, "open ends");
        Assert(r.Joints.Any(j => j.Kind == JointKind.Open && j.A.Placement == 0 && j.A.FacingX == 2 && j.A.FacingY == 1), "east side is open");
        Equal(1, r.ComponentCount, "one section");
    }

    private static void BridgeDeckAndRoadBelowStaySeparate()
    {
        // bridge_25: road through at ground level north-south, deck five levels up east-west
        var bridge = Shape(false, OneCell, (new ConnectorPoint(0, 0, -500), new ConnectorPoint(0, 0, 500)),
                                           (new ConnectorPoint(500, -625, 0), new ConnectorPoint(-500, -625, 0)));
        var shapes = new Dictionary<int, ModuleShape>(Shapes) { [97] = bridge };
        var placements = new[]
        {
            new PlacedModule(97, 1, 1, 0, 0),
            new PlacedModule(Straight, 1, 0, 0, 0),  // under the bridge, ground level
            new PlacedModule(Straight, 2, 1, 1, 5),  // onto the deck, five levels up
        };
        var r = TrackConnectivity.Analyze(placements, id => shapes[id], null);
        Equal(2, r.ConnectionCount, "road below and deck both join");
        Equal(0, r.StepCount, "no steps");
    }

    private static void PipesNeedSameHeight()
    {
        Equal(1, Analyze(new PlacedModule(Pipe, 0, 0, 0, 0), new PlacedModule(Pipe, 0, 1, 0, 0)).ConnectionCount, "level pipes");
        Equal(1, Analyze(new PlacedModule(Pipe, 0, 0, 0, 0), new PlacedModule(Pipe, 0, 1, 0, 1)).StepCount, "offset pipes");
    }

    private static void InternalZonesAreNotCounted()
    {
        var r = Analyze(new PlacedModule(BigCorner, 5, 5, 0, 0));
        Equal(0, r.ConnectionCount, "internal joints are not connections");
        Equal(2, r.OpenCount, "two open ends");
        Equal(2, r.Joints.Count(j => j.Internal), "internal joints");
        Assert(r.Joints.Where(j => j.Kind == JointKind.Open).All(j => j.A.HasFacing), "open ends face out of the module");
    }

    private static void SectionsAndStartSection()
    {
        var r = Analyze(new PlacedModule(Straight, 5, 5, 0, 0), new PlacedModule(Straight, 5, 6, 0, 0), new PlacedModule(Start, 0, 0, 0, 0));
        Equal(2, r.ComponentCount, "sections");
        Assert(r.HasStart, "start found");
        Equal(r.Component[2], r.MainComponent, "main section has the start grid");

        var noStart = Analyze(new PlacedModule(Straight, 5, 5, 0, 0), new PlacedModule(Straight, 5, 6, 0, 0), new PlacedModule(Straight, 0, 0, 0, 0));
        Assert(!noStart.HasStart, "no start");
        Equal(noStart.Component[0], noStart.MainComponent, "largest section is main without a start grid");
    }

    private static void EvaluateReportsConnectsBlocksAndSteps()
    {
        var existing = Analyze(new PlacedModule(Straight, 0, 0, 0, 0));

        var inLine = TrackConnectivity.Evaluate(existing, Shapes[Straight], 0, 1, 0, 0);
        Equal(1, inLine.Connects, "in line connects");
        Equal(1, inLine.Open, "far end open");
        Equal(0, inLine.Blocks, "nothing blocked");

        var across = TrackConnectivity.Evaluate(existing, Shapes[Straight], 0, 1, 1, 0);
        Equal(0, across.Connects, "across connects nothing");
        Equal(1, across.Blocks, "across blocks the opening");

        var raised = TrackConnectivity.Evaluate(existing, Shapes[Straight], 0, 1, 0, 2);
        Equal(1, raised.Steps, "raised meets a step");

        // replacing the existing piece ignores it
        var replace = TrackConnectivity.Evaluate(existing, Shapes[Straight], 0, 0, 1, 0);
        Equal(0, replace.Connects + replace.Blocks + replace.Steps, "replaced piece is ignored");
    }

    private static void BestFitFindsRotationAndLevel()
    {
        var existing = Analyze(new PlacedModule(Straight, 0, 0, 0, 0));
        var fit = TrackConnectivity.FindBestFit(existing, Shapes[Straight], 0, 1, 1, 0, 20);
        Assert(fit != null, "a better fit exists");
        Equal(0, fit.Rotation % 2, "runs north-south");
        Equal(1, fit.Connects, "connects");

        var raised = Analyze(new PlacedModule(Straight, 0, 0, 0, 3));
        var levelFit = TrackConnectivity.FindBestFit(raised, Shapes[Straight], 0, 1, 0, 0, 20);
        Assert(levelFit != null, "a level fit exists");
        Equal(3, levelFit.Level, "matches the neighbour's level");

        // a corner next to the end of a straight turns to take the opening
        // (turned twice it leads north and west, away from the opening)
        var cornerFit = TrackConnectivity.FindBestFit(existing, Shapes[Corner], 0, 1, 2, 0, 20);
        Assert(cornerFit != null && cornerFit.Connects == 1 && cornerFit.Blocks == 0, "corner fits");
        Equal(1, cornerFit.Rotation, "nearest rotation that takes the opening");
    }

    private static void BestFitReturnsNullWhenNothingImproves()
    {
        var existing = Analyze(new PlacedModule(Straight, 0, 0, 0, 0));
        Assert(TrackConnectivity.FindBestFit(existing, Shapes[Straight], 0, 1, 0, 0, 20) == null, "already fits");
        Assert(TrackConnectivity.FindBestFit(existing, Shapes[Straight], 6, 6, 1, 0, 20) == null, "nothing nearby");
    }

    // block groups
    private static void RunSelectionTests()
    {
        Run(nameof(GroupPivotIsWholeCell), GroupPivotIsWholeCell);
        Run(nameof(GroupRotationKeepsFootprintsTogether), GroupRotationKeepsFootprintsTogether);
        Run(nameof(GroupRotationFourTimesIsIdentity), GroupRotationFourTimesIsIdentity);
        Run(nameof(GroupFitChecksBoundsAndOverlap), GroupFitChecksBoundsAndOverlap);
    }

    private static IEnumerable<(int X, int Y)> Cells(int moduleIndex) => Shapes[moduleIndex].Cells;

    private static HashSet<(int, int)> Covered(IEnumerable<BlockSpec> blocks)
    {
        var set = new HashSet<(int, int)>();
        foreach (var b in blocks)
            foreach (var c in TrackConnectivity.Footprint(Shapes[b.ModuleIndex], b.X, b.Y, b.Rotation))
                set.Add(c);
        return set;
    }

    private static void GroupPivotIsWholeCell()
    {
        Equal((1, 1), BlockGroup.Pivot(new[] { (0, 0), (3, 3) }), "even box rounds down");
        Equal((2, 0), BlockGroup.Pivot(new[] { (1, 0), (3, 0) }), "odd box centre");
    }

    private static void GroupRotationKeepsFootprintsTogether()
    {
        // a big corner and a straight touching it; after turning the group, the cells it covers
        // must be the old cells turned around the pivot
        var blocks = new List<BlockSpec>
        {
            new BlockSpec { ModuleIndex = BigCorner, X = 4, Y = 6, Rotation = 0 },
            new BlockSpec { ModuleIndex = Straight, X = 4, Y = 7, Rotation = 0 },
        };
        blocks[1].Pickups.Add((4, 7));
        var before = Covered(blocks);
        var pivot = BlockGroup.Pivot(before.Select(c => (c.Item1, c.Item2)));

        var turned = blocks.Select(b => BlockGroup.Rotate(b, pivot, 1)).ToList();
        var expected = new HashSet<(int, int)>(before.Select(c => BlockGroup.RotateAbout(c, pivot, 1)));
        Assert(expected.SetEquals(Covered(turned)), "footprints turn with the group");
        Equal(1, turned[0].Rotation, "rotation advances");
        Equal(BlockGroup.RotateAbout((4, 7), pivot, 1), turned[1].Pickups[0], "pickups turn too");

        // and the turned group still connects the same way
        var placed = turned.Select(b => new PlacedModule(b.ModuleIndex, b.X, b.Y, b.Rotation, b.Level)).ToArray();
        var original = blocks.Select(b => new PlacedModule(b.ModuleIndex, b.X, b.Y, b.Rotation, b.Level)).ToArray();
        Equal(Analyze(original).ConnectionCount, Analyze(placed).ConnectionCount, "connections survive turning");
    }

    private static void GroupRotationFourTimesIsIdentity()
    {
        var block = new BlockSpec { ModuleIndex = BigCorner, X = 3, Y = 9, Rotation = 2, Level = 4 };
        var b = block;
        for (int i = 0; i < 4; i++)
            b = BlockGroup.Rotate(b, (5, 5), 1);
        Equal((3, 9, 2, 4), (b.X, b.Y, b.Rotation, b.Level), "full turn");
    }

    private static void GroupFitChecksBoundsAndOverlap()
    {
        var blocks = new[] { new BlockSpec { ModuleIndex = Straight, X = 0, Y = 0 }, new BlockSpec { ModuleIndex = Straight, X = 1, Y = 0 } };
        Assert(BlockGroup.CheckFit(blocks, Cells, 4, 4, 10, null) == null, "fits");
        Assert(BlockGroup.CheckFit(blocks.Select(b => BlockGroup.Translate(b, -1, 0, 0)), Cells, 4, 4, 10, null) != null, "out of bounds");
        Assert(BlockGroup.CheckFit(blocks, Cells, 4, 4, 10, (x, y) => x == 1 && y == 0) != null, "blocked");
        Assert(BlockGroup.CheckFit(new[] { blocks[0], blocks[0].Clone() }, Cells, 4, 4, 10, null) != null, "overlap");
        Assert(BlockGroup.CheckFit(blocks.Select(b => BlockGroup.Translate(b, 0, 0, 11)), Cells, 4, 4, 10, null) != null, "too high");
    }
}
