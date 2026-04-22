using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.Contours
{
    public sealed class OuterContourExtractor
    {
        public OuterContourExtractionResult ExtractFromViewLocalEntities(
    ViewContourInput input,
    OuterContourExtractionOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(input);

            options ??= new OuterContourExtractionOptions();

            var result = new OuterContourExtractionResult
            {
                InputViewId = input.ViewId,
                InputMode = "ViewLocal",
                InputSourceTag = input.SourceTag ?? string.Empty,
                RawInputEntityCount = input.Entities?.Count ?? 0,
                ViewBounds = Bounds2DHelper.Normalize(input.Bounds)
            };

            var prepared = PrepareViewLocalEntities(
                input.Entities ?? Array.Empty<SheetEntity>(),
                result.ViewBounds,
                options,
                result);

            RunCore(result, prepared, options);
            return result;
        }

        public OuterContourExtractionResult Extract(
    IReadOnlyList<SheetEntity> semanticEntities,
    Bounds2D viewBounds,
    OuterContourExtractionOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(semanticEntities);

            options ??= new OuterContourExtractionOptions();

            var result = new OuterContourExtractionResult
            {
                InputViewId = -1,
                InputMode = "LegacyGlobal",
                InputSourceTag = "GlobalSemanticPool",
                RawInputEntityCount = semanticEntities.Count,
                ViewBounds = Bounds2DHelper.Normalize(viewBounds)
            };

            var eligible = CollectEligibleEntitiesFromGlobal(
                semanticEntities,
                result.ViewBounds,
                options);

            RunCore(result, eligible, options);
            return result;
        }

        private void RunCore(
    OuterContourExtractionResult result,
    IReadOnlyList<SheetEntity> eligible,
    OuterContourExtractionOptions options)
        {
            result.EligibleEntities.AddRange(eligible);

            if (eligible.Count == 0)
            {
                result.Diagnostics.Add("NoEligibleEntities");
                return;
            }

            var preferred = SelectPreferredEntitiesByStyle(eligible, result.ViewBounds, options, result);
            result.PreferredEntities.AddRange(preferred);

            var workingEntities = preferred.Count > 0 ? preferred : eligible;

            var edges = BuildEdges(workingEntities, result.ViewBounds, options);
            result.Edges.AddRange(edges);

            if (edges.Count == 0)
            {
                result.Diagnostics.Add("NoEdges");
                return;
            }

            var seeds = FindOuterSeeds(edges, result.ViewBounds, options);
            result.Seeds.AddRange(seeds);

            if (seeds.Count == 0)
                result.Diagnostics.Add("NoSeeds");

            var links = BuildAdjacency(edges, result.ViewBounds, options);
            foreach (var pair in links)
                result.LinksByEdgeId[pair.Key] = pair.Value;

            var loops = TraceLoops(edges, seeds, links, result.ViewBounds, options);
            result.Loops.AddRange(loops);

            if (loops.Count == 0)
                result.Diagnostics.Add("NoLoops");

            result.BestLoop = SelectBestLoop(loops, result.ViewBounds, options);

            if (result.BestLoop == null)
                result.Diagnostics.Add("BestLoopIsNull");

            if (result.BestLoop == null &&
                options.FallbackToAllEligibleIfNoLoop &&
                !ReferenceEquals(workingEntities, eligible))
            {
                result.Diagnostics.Add("FallbackToAllEligible");

                result.Edges.Clear();
                result.Seeds.Clear();
                result.LinksByEdgeId.Clear();
                result.Loops.Clear();

                edges = BuildEdges(eligible, result.ViewBounds, options);
                result.Edges.AddRange(edges);

                if (edges.Count == 0)
                {
                    result.Diagnostics.Add("FallbackNoEdges");
                    return;
                }

                seeds = FindOuterSeeds(edges, result.ViewBounds, options);
                result.Seeds.AddRange(seeds);

                links = BuildAdjacency(edges, result.ViewBounds, options);
                foreach (var pair in links)
                    result.LinksByEdgeId[pair.Key] = pair.Value;

                loops = TraceLoops(edges, seeds, links, result.ViewBounds, options);
                result.Loops.AddRange(loops);

                result.BestLoop = SelectBestLoop(loops, result.ViewBounds, options);

                if (result.BestLoop == null)
                    result.Diagnostics.Add("FallbackBestLoopIsNull");
            }
        }

        private IReadOnlyList<SheetEntity> PrepareViewLocalEntities(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D viewBounds,
    OuterContourExtractionOptions options,
    OuterContourExtractionResult result)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            var prepared = new List<SheetEntity>();

            foreach (var e in entities)
            {
                if (e == null)
                    continue;

                if (!e.IsVisible)
                    continue;

                if (!IsSupportedContourKind(e))
                    continue;

                if (e.IsTextLike || e.IsDimensionLike)
                    continue;

                if (e.Role == SheetEntityRole.Text ||
                    e.Role == SheetEntityRole.Dimension ||
                    e.Role == SheetEntityRole.Leader ||
                    e.Role == SheetEntityRole.Symbol ||
                    e.Role == SheetEntityRole.BlockContainer)
                    continue;

                if (e.IsCenterLine || e.IsHiddenLine)
                    continue;

                if (e.ContainsOrEnclosesHatchLike)
                    continue;

                if (e.IsLikelySemanticNoise)
                    continue;

                if (e.Bounds.IsEmpty)
                    continue;

                if (!Bounds2DHelper.Intersects(e.Bounds, viewBounds, tolerance: 0.0))
                    continue;

                prepared.Add(e);
            }

            if (prepared.Count == 0)
                result.Diagnostics.Add("PrepareViewLocalEntities=0");

            return prepared;
        }

        public IReadOnlyList<SheetEntity> CollectEligibleEntitiesFromGlobal(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D viewBounds,
    OuterContourExtractionOptions options)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(options);

            var result = new List<SheetEntity>();

            foreach (var e in entities)
            {
                if (e == null)
                    continue;

                if (!e.IsVisible)
                    continue;

                if (!IsSupportedContourKind(e))
                    continue;

                if (!IsEligibleRoleForOuterContour(e, options))
                    continue;

                if (e.IsCenterLine || e.IsHiddenLine)
                    continue;

                if (e.ContainsOrEnclosesHatchLike)
                    continue;

                if (e.IsLikelySemanticNoise)
                    continue;

                if (e.IsFadedLike && e.Role != SheetEntityRole.Geometry && e.Role != SheetEntityRole.ReferenceGeometry)
                    continue;

                if (options.ExcludeVisualHintCandidates &&
                    e.IsVisualHintCandidate &&
                    e.Role != SheetEntityRole.ReferenceGeometry)
                    continue;

                if (e.Bounds.IsEmpty)
                    continue;

                if (!Bounds2DHelper.Intersects(e.Bounds, viewBounds, tolerance: 0.0))
                    continue;

                var style = ContourStyleSignature.FromEntity(e);
                if (options.RequireContinuousLikeStyle &&
                    !style.IsContinuousLike &&
                    !e.IsOuterContourLikeLayer)
                    continue;

                result.Add(e);
            }

            return result;
        }

        public IReadOnlyList<SheetEntity> FilterEligibleEntities(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D viewBounds,
    OuterContourExtractionOptions options)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(options);

            var result = new List<SheetEntity>();

            foreach (var e in entities)
            {
                if (e == null)
                    continue;

                if (!e.IsVisible)
                    continue;

                if (!IsSupportedContourKind(e))
                    continue;

                if (!IsEligibleRoleForOuterContour(e, options))
                    continue;

                if (e.IsCenterLine || e.IsHiddenLine)
                    continue;

                if (e.ContainsOrEnclosesHatchLike)
                    continue;

                if (e.IsLikelySemanticNoise)
                    continue;

                if (e.IsFadedLike && e.Role != SheetEntityRole.Geometry && e.Role != SheetEntityRole.ReferenceGeometry)
                    continue;

                if (options.ExcludeVisualHintCandidates &&
                    e.IsVisualHintCandidate &&
                    e.Role != SheetEntityRole.ReferenceGeometry)
                    continue;

                if (e.Bounds.IsEmpty)
                    continue;

                if (!Bounds2DHelper.Intersects(e.Bounds, viewBounds, tolerance: 0.0))
                    continue;

                var style = ContourStyleSignature.FromEntity(e);
                if (options.RequireContinuousLikeStyle &&
                    !style.IsContinuousLike &&
                    !e.IsOuterContourLikeLayer)
                    continue;

                result.Add(e);
            }

            return result;
        }


        public IReadOnlyList<ContourEdge> BuildEdges(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(options);

            var result = new List<ContourEdge>();
            double minDim = Math.Max(1.0, Math.Min(viewBounds.Width, viewBounds.Height));
            double minEdgeLength = Math.Max(0.5, minDim * options.MinEdgeLengthRatio);

            int nextEdgeId = 0;

            foreach (var e in entities)
            {
                if (e == null)
                    continue;

                switch (e.Kind)
                {
                    case SheetEntityKind.Line:
                        {
                            if (!TryGetLineEndpoints(e, out var s, out var t))
                                break;

                            double len = Bounds2DHelper.Distance(s, t);
                            if (len < minEdgeLength)
                                break;

                            result.Add(new ContourEdge
                            {
                                EdgeId = nextEdgeId++,
                                Handle = e.Handle ?? string.Empty,
                                Source = e,
                                Kind = ContourEdgeKind.Line,
                                Start = s,
                                End = t,
                                Bounds = NormalizeBoundsFromPoints(s, t),
                                Length = len,
                                IsClosedPrimitive = false,
                                Style = ContourStyleSignature.FromEntity(e)
                            });
                            break;
                        }

                    case SheetEntityKind.Polyline:
                        {
                            var vertices = e.Vertices?.ToList() ?? new List<Point2D>();
                            if (vertices.Count < 2)
                            {
                                if (TryGetLineEndpoints(e, out var ps, out var pe))
                                {
                                    double len = Bounds2DHelper.Distance(ps, pe);
                                    if (len >= minEdgeLength)
                                    {
                                        result.Add(new ContourEdge
                                        {
                                            EdgeId = nextEdgeId++,
                                            Handle = e.Handle ?? string.Empty,
                                            Source = e,
                                            Kind = ContourEdgeKind.PolylineSegment,
                                            Start = ps,
                                            End = pe,
                                            Bounds = NormalizeBoundsFromPoints(ps, pe),
                                            Length = len,
                                            IsClosedPrimitive = e.IsClosed,
                                            Style = ContourStyleSignature.FromEntity(e)
                                        });
                                    }
                                }
                                break;
                            }

                            for (int i = 0; i < vertices.Count - 1; i++)
                            {
                                var s = vertices[i];
                                var t = vertices[i + 1];
                                double len = Bounds2DHelper.Distance(s, t);
                                if (len < minEdgeLength)
                                    continue;

                                result.Add(new ContourEdge
                                {
                                    EdgeId = nextEdgeId++,
                                    Handle = e.Handle ?? string.Empty,
                                    Source = e,
                                    Kind = ContourEdgeKind.PolylineSegment,
                                    Start = s,
                                    End = t,
                                    Bounds = NormalizeBoundsFromPoints(s, t),
                                    Length = len,
                                    IsClosedPrimitive = false,
                                    Style = ContourStyleSignature.FromEntity(e)
                                });
                            }

                            if (e.IsClosed && vertices.Count >= 3)
                            {
                                var s = vertices[vertices.Count - 1];
                                var t = vertices[0];
                                double len = Bounds2DHelper.Distance(s, t);
                                if (len >= minEdgeLength)
                                {
                                    result.Add(new ContourEdge
                                    {
                                        EdgeId = nextEdgeId++,
                                        Handle = e.Handle ?? string.Empty,
                                        Source = e,
                                        Kind = ContourEdgeKind.PolylineSegment,
                                        Start = s,
                                        End = t,
                                        Bounds = NormalizeBoundsFromPoints(s, t),
                                        Length = len,
                                        IsClosedPrimitive = true,
                                        Style = ContourStyleSignature.FromEntity(e)
                                    });
                                }
                            }
                            break;
                        }

                    case SheetEntityKind.Arc:
                        {
                            if (!TryGetArcEndpoints(e, out var s, out var t))
                                break;

                            double len = EstimateArcLength(e, s, t);
                            if (len < minEdgeLength)
                                break;

                            result.Add(new ContourEdge
                            {
                                EdgeId = nextEdgeId++,
                                Handle = e.Handle ?? string.Empty,
                                Source = e,
                                Kind = ContourEdgeKind.Arc,
                                Start = s,
                                End = t,
                                Bounds = e.Bounds,
                                Length = len,
                                IsClosedPrimitive = false,
                                Style = ContourStyleSignature.FromEntity(e)
                            });
                            break;
                        }

                    case SheetEntityKind.Circle:
                        {
                            if (e.Bounds.IsEmpty)
                                break;

                            var c = e.Center ?? e.CenterPoint ?? e.Bounds.Center;
                            double r = e.Radius ?? Math.Min(e.Bounds.Width, e.Bounds.Height) * 0.5;
                            if (r <= 0)
                                break;

                            result.Add(new ContourEdge
                            {
                                EdgeId = nextEdgeId++,
                                Handle = e.Handle ?? string.Empty,
                                Source = e,
                                Kind = ContourEdgeKind.Circle,
                                Start = new Point2D(c.X + r, c.Y),
                                End = new Point2D(c.X + r, c.Y),
                                Bounds = e.Bounds,
                                Length = 2.0 * Math.PI * r,
                                IsClosedPrimitive = true,
                                Style = ContourStyleSignature.FromEntity(e)
                            });
                            break;
                        }

                    case SheetEntityKind.Ellipse:
                        {
                            if (e.Bounds.IsEmpty)
                                break;

                            double a = e.MajorRadius ?? (e.Bounds.Width * 0.5);
                            double b = e.MinorRadius ?? (e.Bounds.Height * 0.5);
                            if (a <= 0 || b <= 0)
                                break;

                            var c = e.Center ?? e.CenterPoint ?? e.Bounds.Center;
                            double perimeter = EstimateEllipsePerimeter(a, b);

                            result.Add(new ContourEdge
                            {
                                EdgeId = nextEdgeId++,
                                Handle = e.Handle ?? string.Empty,
                                Source = e,
                                Kind = ContourEdgeKind.Ellipse,
                                Start = new Point2D(c.X + a, c.Y),
                                End = new Point2D(c.X + a, c.Y),
                                Bounds = e.Bounds,
                                Length = perimeter,
                                IsClosedPrimitive = true,
                                Style = ContourStyleSignature.FromEntity(e)
                            });
                            break;
                        }
                }
            }

            return result;
        }

        public IReadOnlyList<OuterSeedCandidate> FindOuterSeeds(
            IReadOnlyList<ContourEdge> edges,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options)
        {
            ArgumentNullException.ThrowIfNull(edges);
            ArgumentNullException.ThrowIfNull(options);

            var result = new List<OuterSeedCandidate>();
            if (edges.Count == 0 || viewBounds.IsEmpty)
                return result;

            double band = Math.Max(options.OuterBandMin, Math.Min(viewBounds.Width, viewBounds.Height) * options.OuterBandRatio);

            var topBand = new Bounds2D(viewBounds.MinX, viewBounds.MaxY - band, viewBounds.MaxX, viewBounds.MaxY);
            var bottomBand = new Bounds2D(viewBounds.MinX, viewBounds.MinY, viewBounds.MaxX, viewBounds.MinY + band);
            var leftBand = new Bounds2D(viewBounds.MinX, viewBounds.MinY, viewBounds.MinX + band, viewBounds.MaxY);
            var rightBand = new Bounds2D(viewBounds.MaxX - band, viewBounds.MinY, viewBounds.MaxX, viewBounds.MaxY);

            AddSeedsForBand(edges, topBand, OuterSeedSide.Top, result, viewBounds, options);
            AddSeedsForBand(edges, bottomBand, OuterSeedSide.Bottom, result, viewBounds, options);
            AddSeedsForBand(edges, leftBand, OuterSeedSide.Left, result, viewBounds, options);
            AddSeedsForBand(edges, rightBand, OuterSeedSide.Right, result, viewBounds, options);

            return result
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.EdgeId)
                .ToList();
        }

        public Dictionary<int, List<ContourLink>> BuildAdjacency(
            IReadOnlyList<ContourEdge> edges,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options)
        {
            ArgumentNullException.ThrowIfNull(edges);
            ArgumentNullException.ThrowIfNull(options);

            var result = new Dictionary<int, List<ContourLink>>();
            if (edges.Count == 0)
                return result;

            double endpointTol = ResolveEndpointTolerance(viewBounds, options);

            for (int i = 0; i < edges.Count; i++)
            {
                var a = edges[i];
                var links = new List<ContourLink>();

                for (int j = 0; j < edges.Count; j++)
                {
                    if (i == j)
                        continue;

                    var b = edges[j];

                    var candidates = new[]
                    {
                        BuildLinkCandidate(a, a.End, b, b.Start),
                        BuildLinkCandidate(a, a.End, b, b.End),
                        BuildLinkCandidate(a, a.Start, b, b.Start),
                        BuildLinkCandidate(a, a.Start, b, b.End)
                    };

                    var best = candidates
                        .Where(x => x != null)
                        .Where(x => x!.EndpointDistance <= endpointTol)
                        .OrderByDescending(x => x!.TotalScore)
                        .FirstOrDefault();

                    if (best != null)
                        links.Add(best);
                }

                result[a.EdgeId] = links
                    .OrderByDescending(x => x.TotalScore)
                    .ThenBy(x => x.EndpointDistance)
                    .Take(Math.Max(1, options.MaxOutgoingLinksPerEdge))
                    .ToList();
            }

            return result;
        }

        public IReadOnlyList<TracedContourLoop> TraceLoops(
            IReadOnlyList<ContourEdge> edges,
            IReadOnlyList<OuterSeedCandidate> seeds,
            IReadOnlyDictionary<int, List<ContourLink>> adjacency,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options)
        {
            ArgumentNullException.ThrowIfNull(edges);
            ArgumentNullException.ThrowIfNull(seeds);
            ArgumentNullException.ThrowIfNull(adjacency);
            ArgumentNullException.ThrowIfNull(options);

            var result = new List<TracedContourLoop>();
            if (edges.Count == 0 || seeds.Count == 0)
                return result;

            var globalSeenSignatures = new HashSet<string>(StringComparer.Ordinal);

            foreach (var seed in seeds)
            {
                if (seed.EdgeId < 0 || seed.EdgeId >= edges.Count)
                    continue;

                var loop = TraceSingleLoop(edges, seed, adjacency, viewBounds, options);
                if (loop == null)
                    continue;

                var signature = string.Join(",", loop.EdgeIds.OrderBy(x => x));
                if (!globalSeenSignatures.Add(signature))
                    continue;

                if (IsValidLoop(loop, viewBounds, options))
                    result.Add(loop);
            }

            return result
                .OrderByDescending(x => x.OuterScore)
                .ThenByDescending(x => x.EstimatedArea)
                .ToList();
        }

        public TracedContourLoop? SelectBestLoop(
    IReadOnlyList<TracedContourLoop> loops,
    Bounds2D viewBounds,
    OuterContourExtractionOptions options)
        {
            ArgumentNullException.ThrowIfNull(loops);

            if (loops.Count == 0)
                return null;

            var valid = loops
                .Where(x => x != null)
                .Where(x => IsValidLoop(x, viewBounds, options))
                .OrderByDescending(x => x.OuterScore)
                .ThenByDescending(x => x.TouchingSideCount)
                .ThenByDescending(x => x.AreaRatio)
                .ThenByDescending(x => x.BoundsCoverageScore)
                .ThenByDescending(x => x.Perimeter)
                .ToList();

            if (valid.Count > 0)
                return valid[0];

            return loops
                .OrderByDescending(x => x.OuterScore)
                .ThenByDescending(x => x.TouchingSideCount)
                .ThenByDescending(x => x.AreaRatio)
                .ThenByDescending(x => x.BoundsCoverageScore)
                .ThenByDescending(x => x.Perimeter)
                .FirstOrDefault();
        }

        private static void AddSeedsForBand(
            IReadOnlyList<ContourEdge> edges,
            Bounds2D band,
            OuterSeedSide side,
            List<OuterSeedCandidate> output,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options)
        {
            foreach (var edge in edges)
            {
                if (!Bounds2DHelper.Intersects(edge.Bounds, band, tolerance: 0.0))
                    continue;

                double score = 0.0;
                score += edge.Length;

                if (edge.Style.IsContinuousLike)
                    score += 1000.0;

                if (edge.Source.IsOuterContourLikeLayer && options.PreferOuterContourLikeLayer)
                    score += 400.0;

                if (edge.Source.GeometryConfidenceScore > 0)
                    score += edge.Source.GeometryConfidenceScore * 100.0;

                double sideBonus = ComputeSideAdhesionBonus(edge.Bounds, side, viewBounds);
                score += sideBonus;

                output.Add(new OuterSeedCandidate
                {
                    EdgeId = edge.EdgeId,
                    Side = side,
                    Score = score,
                    Reason = $"Touches {side} outer band; Len={edge.Length:0.###}; SideBonus={sideBonus:0.###}"
                });
            }
        }

        private static double ComputeSideAdhesionBonus(Bounds2D edgeBounds, OuterSeedSide side, Bounds2D viewBounds)
        {
            edgeBounds = Bounds2DHelper.Normalize(edgeBounds);
            viewBounds = Bounds2DHelper.Normalize(viewBounds);

            return side switch
            {
                OuterSeedSide.Top => Math.Max(0.0, 100.0 - Math.Abs(viewBounds.MaxY - edgeBounds.MaxY)),
                OuterSeedSide.Bottom => Math.Max(0.0, 100.0 - Math.Abs(edgeBounds.MinY - viewBounds.MinY)),
                OuterSeedSide.Left => Math.Max(0.0, 100.0 - Math.Abs(edgeBounds.MinX - viewBounds.MinX)),
                OuterSeedSide.Right => Math.Max(0.0, 100.0 - Math.Abs(viewBounds.MaxX - edgeBounds.MaxX)),
                _ => 0.0
            };
        }

        private static ContourLink? BuildLinkCandidate(
            ContourEdge fromEdge,
            Point2D fromPoint,
            ContourEdge toEdge,
            Point2D toPoint)
        {
            double dist = Bounds2DHelper.Distance(fromPoint, toPoint);

            bool styleExact = fromEdge.Style.MatchesExactly(toEdge.Style);
            bool styleLoose = fromEdge.Style.MatchesLoosely(toEdge.Style);

            double directionScore = ComputeDirectionContinuityScore(fromEdge, fromPoint, toEdge, toPoint);

            double total = 0.0;
            total += Math.Max(0.0, 100.0 - dist * 10.0);
            total += directionScore * 50.0;
            if (styleExact) total += 80.0;
            else if (styleLoose) total += 40.0;

            return new ContourLink
            {
                FromEdgeId = fromEdge.EdgeId,
                ToEdgeId = toEdge.EdgeId,
                FromPoint = fromPoint,
                ToPoint = toPoint,
                EndpointDistance = dist,
                StyleMatchedExactly = styleExact,
                StyleMatchedLoosely = styleLoose,
                DirectionScore = directionScore,
                TotalScore = total
            };
        }

        private static double ComputeDirectionContinuityScore(
    ContourEdge fromEdge,
    Point2D fromPoint,
    ContourEdge toEdge,
    Point2D toPoint)
        {
            var v1 = ResolveTravelVector(fromEdge, fromPoint, outgoing: true);
            var v2 = ResolveTravelVector(toEdge, toPoint, outgoing: false);

            double l1 = Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y);
            double l2 = Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y);

            if (l1 <= 1e-9 || l2 <= 1e-9)
                return 0.0;

            double dot = (v1.X * v2.X + v1.Y * v2.Y) / (l1 * l2);
            dot = Math.Max(-1.0, Math.Min(1.0, dot));

            return (dot + 1.0) * 0.5;
        }

        private static Point2D ResolveTravelVector(
            ContourEdge edge,
            Point2D connectionPoint,
            bool outgoing)
        {
            double ds = Bounds2DHelper.Distance(connectionPoint, edge.Start);
            double de = Bounds2DHelper.Distance(connectionPoint, edge.End);

            bool atStart = ds <= de;

            if (outgoing)
            {
                return atStart
                    ? new Point2D(edge.End.X - edge.Start.X, edge.End.Y - edge.Start.Y)
                    : new Point2D(edge.Start.X - edge.End.X, edge.Start.Y - edge.End.Y);
            }

            return atStart
                ? new Point2D(edge.Start.X - edge.End.X, edge.Start.Y - edge.End.Y)
                : new Point2D(edge.End.X - edge.Start.X, edge.End.Y - edge.Start.Y);
        }

        private static double ComputeDirectionContinuityScore(ContourEdge a, ContourEdge b)
        {
            var va = new Point2D(a.End.X - a.Start.X, a.End.Y - a.Start.Y);
            var vb = new Point2D(b.End.X - b.Start.X, b.End.Y - b.Start.Y);

            double la = Math.Sqrt(va.X * va.X + va.Y * va.Y);
            double lb = Math.Sqrt(vb.X * vb.X + vb.Y * vb.Y);

            if (la <= 1e-9 || lb <= 1e-9)
                return 0.0;

            double dot = (va.X * vb.X + va.Y * vb.Y) / (la * lb);
            dot = Math.Max(-1.0, Math.Min(1.0, dot));

            return (dot + 1.0) * 0.5;
        }

        private TracedContourLoop? TraceSingleLoop(
            IReadOnlyList<ContourEdge> edges,
            OuterSeedCandidate seed,
            IReadOnlyDictionary<int, List<ContourLink>> adjacency,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options)
        {
            var visited = new HashSet<int>();
            var orderedEdges = new List<int>();

            int current = seed.EdgeId;
            int guard = 0;

            while (guard < options.MaxTraceDepth)
            {
                guard++;

                if (!visited.Add(current))
                    break;

                orderedEdges.Add(current);

                if (!adjacency.TryGetValue(current, out var nextLinks) || nextLinks.Count == 0)
                    break;

                int next = -1;
                foreach (var link in nextLinks.OrderByDescending(x => x.TotalScore))
                {
                    if (!visited.Contains(link.ToEdgeId))
                    {
                        next = link.ToEdgeId;
                        break;
                    }
                }

                if (next < 0)
                    break;

                current = next;
            }

            if (orderedEdges.Count == 0)
                return null;

            var loop = BuildLoop(edges, orderedEdges, viewBounds, options);
            loop.Reason = $"Seed={seed.Side}, Edge#{seed.EdgeId}, TraceCount={orderedEdges.Count}";
            return loop;
        }

        private TracedContourLoop BuildLoop(
    IReadOnlyList<ContourEdge> edges,
    IReadOnlyList<int> orderedEdgeIds,
    Bounds2D viewBounds,
    OuterContourExtractionOptions options)
        {
            var loop = new TracedContourLoop();

            foreach (var id in orderedEdgeIds)
                loop.EdgeIds.Add(id);

            double perimeter = 0.0;
            var boundsList = new List<Bounds2D>();
            var polygon = new List<Point2D>();

            foreach (var id in orderedEdgeIds)
            {
                if (id < 0 || id >= edges.Count)
                    continue;

                var e = edges[id];
                perimeter += e.Length;
                boundsList.Add(e.Bounds);
                polygon.Add(e.Start);
            }

            if (boundsList.Count > 0)
                loop.Bounds = Bounds2DHelper.Union(boundsList);
            else
                loop.Bounds = Bounds2D.Empty;

            loop.Perimeter = perimeter;
            loop.IsClosed = IsClosedLoop(edges, orderedEdgeIds, viewBounds, options);
            loop.EstimatedArea = EstimatePolygonArea(polygon);

            double viewArea = Math.Max(1.0, viewBounds.Area);
            loop.AreaRatio = Math.Max(0.0, loop.EstimatedArea / viewArea);

            loop.BoundsCoverageScore = ComputeBoundsCoverageScore(loop.Bounds, viewBounds);
            loop.TouchingSideCount = CountTouchingSides(loop.Bounds, viewBounds, ResolveEndpointTolerance(viewBounds, options) * 2.0);
            loop.StyleConsistencyScore = ComputeLoopStyleConsistency(edges, orderedEdgeIds);
            loop.TotalGapToView = ComputeTotalGapToView(loop.Bounds, viewBounds);

            double normalizedGapPenalty = loop.TotalGapToView / Math.Max(1.0, viewBounds.Width + viewBounds.Height);
            double closureBonus = loop.IsClosed ? 1_000_000.0 : 0.0;

            loop.OuterScore =
                closureBonus +
                loop.AreaRatio * 400_000.0 +
                loop.BoundsCoverageScore * 220_000.0 +
                loop.TouchingSideCount * 120_000.0 +
                loop.StyleConsistencyScore * 80_000.0 +
                perimeter * 1.5 -
                normalizedGapPenalty * 180_000.0;

            loop.Reason =
                $"Closed={loop.IsClosed}, " +
                $"AreaRatio={loop.AreaRatio:0.###}, " +
                $"Coverage={loop.BoundsCoverageScore:0.###}, " +
                $"TouchSides={loop.TouchingSideCount}, " +
                $"StyleConsistency={loop.StyleConsistencyScore:0.###}, " +
                $"Gap={loop.TotalGapToView:0.###}, " +
                $"Perimeter={perimeter:0.###}";

            return loop;
        }


        private bool IsValidLoop(
    TracedContourLoop loop,
    Bounds2D viewBounds,
    OuterContourExtractionOptions options)
        {
            if (loop == null)
                return false;

            if (!loop.IsClosed)
                return false;

            double minDim = Math.Max(1.0, Math.Min(viewBounds.Width, viewBounds.Height));
            double minPerimeter = minDim * options.MinLoopPerimeterRatio;
            double minArea = Math.Max(1.0, viewBounds.Area * options.MinLoopAreaRatio);

            if (loop.Perimeter < minPerimeter)
                return false;

            if (loop.EstimatedArea < minArea)
                return false;

            if (loop.TouchingSideCount < 2)
                return false;

            return true;
        }

        private bool IsClosedLoop(
            IReadOnlyList<ContourEdge> edges,
            IReadOnlyList<int> orderedEdgeIds,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options)
        {
            if (orderedEdgeIds.Count <= 1)
                return false;

            var first = edges[orderedEdgeIds[0]];
            var last = edges[orderedEdgeIds[orderedEdgeIds.Count - 1]];
            double tol = ResolveEndpointTolerance(viewBounds, options);

            return Bounds2DHelper.Distance(first.Start, last.End) <= tol ||
                   Bounds2DHelper.Distance(first.Start, last.Start) <= tol ||
                   Bounds2DHelper.Distance(first.End, last.End) <= tol ||
                   Bounds2DHelper.Distance(first.End, last.Start) <= tol;
        }

        private static double ComputeBoundsCoverageScore(Bounds2D candidate, Bounds2D viewBounds)
        {
            candidate = Bounds2DHelper.Normalize(candidate);
            viewBounds = Bounds2DHelper.Normalize(viewBounds);

            if (candidate.IsEmpty || viewBounds.IsEmpty)
                return 0.0;

            double left = Math.Abs(candidate.MinX - viewBounds.MinX);
            double right = Math.Abs(candidate.MaxX - viewBounds.MaxX);
            double bottom = Math.Abs(candidate.MinY - viewBounds.MinY);
            double top = Math.Abs(candidate.MaxY - viewBounds.MaxY);

            double totalGap = left + right + bottom + top;
            return Math.Max(0.0, 1.0 - totalGap / Math.Max(1.0, viewBounds.Width + viewBounds.Height));
        }

        private static double ResolveEndpointTolerance(Bounds2D viewBounds, OuterContourExtractionOptions options)
        {
            double minDim = Math.Max(1.0, Math.Min(viewBounds.Width, viewBounds.Height));
            return Math.Max(options.EndpointToleranceMin, minDim * options.EndpointToleranceRatio);
        }

        private static Bounds2D NormalizeBoundsFromPoints(Point2D a, Point2D b)
        {
            return Bounds2DHelper.Normalize(new Bounds2D(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Max(a.X, b.X),
                Math.Max(a.Y, b.Y)));
        }

        private static bool TryGetLineEndpoints(SheetEntity e, out Point2D start, out Point2D end)
        {
            if (e.StartPoint.HasValue && e.EndPoint.HasValue)
            {
                start = e.StartPoint.Value;
                end = e.EndPoint.Value;
                return true;
            }

            var vertices = e.Vertices?.ToList() ?? new List<Point2D>();
            if (vertices.Count >= 2)
            {
                start = vertices[0];
                end = vertices[vertices.Count - 1];
                return true;
            }

            start = default;
            end = default;
            return false;
        }

        private static bool TryGetArcEndpoints(SheetEntity e, out Point2D start, out Point2D end)
        {
            if (e.StartPoint.HasValue && e.EndPoint.HasValue)
            {
                start = e.StartPoint.Value;
                end = e.EndPoint.Value;
                return true;
            }

            if (e.Center.HasValue && e.Radius.HasValue &&
                e.StartAngleDeg2D.HasValue && e.EndAngleDeg2D.HasValue)
            {
                start = PointOnCircle(e.Center.Value, e.Radius.Value, e.StartAngleDeg2D.Value);
                end = PointOnCircle(e.Center.Value, e.Radius.Value, e.EndAngleDeg2D.Value);
                return true;
            }

            start = default;
            end = default;
            return false;
        }

        private static Point2D PointOnCircle(Point2D center, double radius, double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new Point2D(
                center.X + radius * Math.Cos(rad),
                center.Y + radius * Math.Sin(rad));
        }

        private static double EstimateArcLength(SheetEntity e, Point2D start, Point2D end)
        {
            if (e.Radius.HasValue && e.StartAngleDeg2D.HasValue && e.EndAngleDeg2D.HasValue)
            {
                double startDeg = NormalizeAngleDeg(e.StartAngleDeg2D.Value);
                double endDeg = NormalizeAngleDeg(e.EndAngleDeg2D.Value);
                double sweep = endDeg - startDeg;
                if (sweep < 0)
                    sweep += 360.0;

                return e.Radius.Value * sweep * Math.PI / 180.0;
            }

            return Bounds2DHelper.Distance(start, end);
        }

        private static double NormalizeAngleDeg(double deg)
        {
            deg %= 360.0;
            if (deg < 0)
                deg += 360.0;
            return deg;
        }

        private static double EstimateEllipsePerimeter(double a, double b)
        {
            // Ramanujan approximation
            double h = Math.Pow(a - b, 2) / Math.Pow(a + b, 2);
            return Math.PI * (a + b) * (1.0 + (3.0 * h) / (10.0 + Math.Sqrt(4.0 - 3.0 * h)));
        }

        private static double EstimatePolygonArea(IReadOnlyList<Point2D> pts)
        {
            if (pts == null || pts.Count < 3)
                return 0.0;

            double area = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                area += a.X * b.Y - b.X * a.Y;
            }

            return Math.Abs(area) * 0.5;
        }

        private static bool IsSupportedContourKind(SheetEntity e)
        {
            return e.Kind == SheetEntityKind.Line ||
                   e.Kind == SheetEntityKind.Polyline ||
                   e.Kind == SheetEntityKind.Arc ||
                   e.Kind == SheetEntityKind.Circle ||
                   e.Kind == SheetEntityKind.Ellipse;
        }

        private static bool IsEligibleRoleForOuterContour(
            SheetEntity e,
            OuterContourExtractionOptions options)
        {
            if (e.Role == SheetEntityRole.Geometry)
                return true;

            if (options.AllowReferenceGeometry && e.Role == SheetEntityRole.ReferenceGeometry)
                return true;

            if (options.AllowVisualHintFallback &&
                e.Role == SheetEntityRole.VisualHint &&
                !e.ContainsOrEnclosesHatchLike &&
                !e.IsLikelySemanticNoise &&
                !e.IsFadedLike &&
                e.GeometryConfidenceScore >= e.VisualHintScore)
            {
                return true;
            }

            return false;
        }

        private IReadOnlyList<SheetEntity> SelectPreferredEntitiesByStyle(
            IReadOnlyList<SheetEntity> eligible,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options,
            OuterContourExtractionResult result)
        {
            if (eligible == null || eligible.Count == 0)
                return Array.Empty<SheetEntity>();

            if (!options.UseStyleMajorityFilter)
                return eligible.ToList();

            var groups = eligible
                .GroupBy(ContourStyleSignature.FromEntity)
                .Select(g =>
                {
                    int outerTouchCount = g.Count(x => Bounds2DHelper.Intersects(
                        x.Bounds,
                        BuildOuterBand(viewBounds, options),
                        tolerance: 0.0));

                    int outerContourLikeCount = g.Count(x => x.IsOuterContourLikeLayer);

                    double length = g.Sum(EstimateEntityContributionLength);

                    double score =
                        length * 3.0 +
                        outerTouchCount * 600.0 +
                        outerContourLikeCount * 400.0 +
                        (g.Key.IsContinuousLike ? 500.0 : 0.0);

                    return new ContourStyleGroupStats
                    {
                        Style = g.Key,
                        EntityCount = g.Count(),
                        EstimatedTotalLength = length,
                        OuterBandTouchCount = outerTouchCount,
                        OuterContourLikeCount = outerContourLikeCount,
                        Score = score
                    };
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            result.StyleGroups.AddRange(groups);

            if (groups.Count == 0)
                return eligible.ToList();

            var best = groups[0];
            double cutoff = best.Score * options.PreferredStyleScoreRatio;

            var selectedStyles = groups
                .Where(x => x.Score >= cutoff)
                .Where(x => x.EntityCount >= options.MinPreferredStyleEntityCount || x.OuterContourLikeCount > 0)
                .ToList();

            if (selectedStyles.Count == 0)
            {
                best.IsSelected = true;
                return eligible
                    .Where(x => ContourStyleSignature.FromEntity(x).MatchesLoosely(best.Style))
                    .ToList();
            }

            foreach (var g in selectedStyles)
                g.IsSelected = true;

            return eligible
                .Where(e =>
                {
                    var sig = ContourStyleSignature.FromEntity(e);
                    return selectedStyles.Any(g => sig.MatchesLoosely(g.Style));
                })
                .ToList();
        }

        private static Bounds2D BuildOuterBand(Bounds2D viewBounds, OuterContourExtractionOptions options)
        {
            double band = Math.Max(options.OuterBandMin, Math.Min(viewBounds.Width, viewBounds.Height) * options.OuterBandRatio);
            return new Bounds2D(
                viewBounds.MinX - band,
                viewBounds.MinY - band,
                viewBounds.MaxX + band,
                viewBounds.MaxY + band);
        }

        private static double EstimateEntityContributionLength(SheetEntity e)
        {
            if (e == null)
                return 0.0;

            if (e.Kind == SheetEntityKind.Line || e.Kind == SheetEntityKind.Polyline)
            {
                if (TryGetEntityEndpointsStatic(e, out var s, out var t))
                    return Bounds2DHelper.Distance(s, t);
            }

            if (e.Kind == SheetEntityKind.Arc)
            {
                if (TryGetEntityArcEndpointsStatic(e, out var s, out var t))
                    return EstimateArcLength(e, s, t);
            }

            if (e.Kind == SheetEntityKind.Circle && e.Radius.HasValue)
                return 2.0 * Math.PI * e.Radius.Value;

            if (e.Kind == SheetEntityKind.Ellipse && e.MajorRadius.HasValue && e.MinorRadius.HasValue)
                return EstimateEllipsePerimeter(e.MajorRadius.Value, e.MinorRadius.Value);

            return Math.Max(e.Bounds.Width, e.Bounds.Height);
        }

        private static bool TryGetEntityEndpointsStatic(SheetEntity e, out Point2D start, out Point2D end)
        {
            if (e.StartPoint.HasValue && e.EndPoint.HasValue)
            {
                start = e.StartPoint.Value;
                end = e.EndPoint.Value;
                return true;
            }

            var vertices = e.Vertices?.ToList() ?? new List<Point2D>();
            if (vertices.Count >= 2)
            {
                start = vertices[0];
                end = vertices[vertices.Count - 1];
                return true;
            }

            start = default;
            end = default;
            return false;
        }

        private static bool TryGetEntityArcEndpointsStatic(SheetEntity e, out Point2D start, out Point2D end)
        {
            if (e.StartPoint.HasValue && e.EndPoint.HasValue)
            {
                start = e.StartPoint.Value;
                end = e.EndPoint.Value;
                return true;
            }

            if (e.Center.HasValue && e.Radius.HasValue &&
                e.StartAngleDeg2D.HasValue && e.EndAngleDeg2D.HasValue)
            {
                start = PointOnCircle(e.Center.Value, e.Radius.Value, e.StartAngleDeg2D.Value);
                end = PointOnCircle(e.Center.Value, e.Radius.Value, e.EndAngleDeg2D.Value);
                return true;
            }

            start = default;
            end = default;
            return false;
        }

        private static int CountTouchingSides(Bounds2D candidate, Bounds2D viewBounds, double tolerance)
        {
            candidate = Bounds2DHelper.Normalize(candidate);
            viewBounds = Bounds2DHelper.Normalize(viewBounds);

            int count = 0;

            if (Math.Abs(candidate.MinX - viewBounds.MinX) <= tolerance) count++;
            if (Math.Abs(candidate.MaxX - viewBounds.MaxX) <= tolerance) count++;
            if (Math.Abs(candidate.MinY - viewBounds.MinY) <= tolerance) count++;
            if (Math.Abs(candidate.MaxY - viewBounds.MaxY) <= tolerance) count++;

            return count;
        }

        private static double ComputeTotalGapToView(Bounds2D candidate, Bounds2D viewBounds)
        {
            candidate = Bounds2DHelper.Normalize(candidate);
            viewBounds = Bounds2DHelper.Normalize(viewBounds);

            if (candidate.IsEmpty || viewBounds.IsEmpty)
                return double.MaxValue;

            double left = Math.Abs(candidate.MinX - viewBounds.MinX);
            double right = Math.Abs(candidate.MaxX - viewBounds.MaxX);
            double bottom = Math.Abs(candidate.MinY - viewBounds.MinY);
            double top = Math.Abs(candidate.MaxY - viewBounds.MaxY);

            return left + right + bottom + top;
        }

        private static double ComputeLoopStyleConsistency(
            IReadOnlyList<ContourEdge> edges,
            IReadOnlyList<int> orderedEdgeIds)
        {
            if (orderedEdgeIds == null || orderedEdgeIds.Count == 0)
                return 0.0;

            var grouped = orderedEdgeIds
                .Where(id => id >= 0 && id < edges.Count)
                .GroupBy(id => edges[id].Style)
                .Select(g => g.Count())
                .OrderByDescending(x => x)
                .ToList();

            if (grouped.Count == 0)
                return 0.0;

            return (double)grouped[0] / orderedEdgeIds.Count;
        }
    }
}