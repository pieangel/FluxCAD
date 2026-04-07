using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.ViewIsolation.Loops;

public sealed class ClosedLoopCandidate
{
    public List<Point2D> Vertices { get; } = new();
    public List<Segment2D> Segments { get; } = new();
    public List<int> NodeIds { get; } = new();

    public bool IsClosed { get; set; }

    public bool IsHole { get; set; }
    public int NestingDepth { get; set; }
    public int? ParentLoopIndex { get; set; }

    /// <summary>
    /// 시작점과 끝점 사이의 남은 간격.
    /// 닫힌 loop면 0 또는 매우 작은 값.
    /// </summary>
    public double ClosureGap { get; set; }

    public Bounds2D Bounds { get; private set; } = Bounds2D.Empty;
    public double Area { get; private set; }

    public int VertexCount => Vertices.Count;
    public int SegmentCount => Segments.Count;

    public void FinalizeGeometry()
    {
        if (Vertices.Count == 0)
        {
            Bounds = Bounds2D.Empty;
            Area = 0.0;
            ClosureGap = 0.0;
            return;
        }

        Bounds = BuildBounds(Vertices);
        ClosureGap = ComputeClosureGap(Vertices);

        Area = IsClosed ? Math.Abs(ComputeSignedArea(Vertices)) : 0.0;
    }

    private static double ComputeClosureGap(IReadOnlyList<Point2D> points)
    {
        if (points == null || points.Count < 2)
            return 0.0;

        var a = points[0];
        var b = points[^1];

        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Bounds2D BuildBounds(IReadOnlyList<Point2D> points)
    {
        if (points == null || points.Count == 0)
            return Bounds2D.Empty;

        double minX = points[0].X;
        double minY = points[0].Y;
        double maxX = points[0].X;
        double maxY = points[0].Y;

        for (int i = 1; i < points.Count; i++)
        {
            var p = points[i];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        return new Bounds2D(minX, minY, maxX, maxY);
    }

    private static double ComputeSignedArea(IReadOnlyList<Point2D> points)
    {
        if (points == null || points.Count < 3)
            return 0.0;

        double sum = 0.0;

        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum * 0.5;
    }
}