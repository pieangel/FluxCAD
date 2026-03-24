using FluxCAD.SheetAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    internal sealed class SheetAnalysisDebugRunner
    {
        private readonly IEntitySnapshotBuilder _snapshotBuilder;
        private readonly object _geometryClusterBuilder;
        private readonly object _viewCandidateBuilder;
        private readonly object _viewPackScorer;
        private readonly object _regionDetector;
        private readonly object _regionAwareClassifier;
        private readonly object _drawingContentFirstClassifier;
        private readonly object _quantityExtractor;

        private SheetAnalysisDebugRunner(
    IEntitySnapshotBuilder snapshotBuilder,
    object geometryClusterBuilder,
    object viewCandidateBuilder,
    object viewPackScorer,
    object regionDetector,
    object regionAwareClassifier,
    object drawingContentFirstClassifier,
    object quantityExtractor)
        {
            _snapshotBuilder = snapshotBuilder;
            _geometryClusterBuilder = geometryClusterBuilder;
            _viewCandidateBuilder = viewCandidateBuilder;
            _viewPackScorer = viewPackScorer;
            _regionDetector = regionDetector;
            _regionAwareClassifier = regionAwareClassifier;
            _drawingContentFirstClassifier = drawingContentFirstClassifier;
            _quantityExtractor = quantityExtractor;
        }

        public static SheetAnalysisDebugRunner CreateDefault(IEntitySnapshotBuilder snapshotBuilder)
        {
            return new SheetAnalysisDebugRunner(
                snapshotBuilder: snapshotBuilder,
                geometryClusterBuilder: new GeometryClusterBuilder(),
                viewCandidateBuilder: new ViewClusterCandidateBuilder(),
                viewPackScorer: new ViewPackScorer(),
                regionDetector: new DrawingContentFirstRegionDetector(),
                regionAwareClassifier: new RegionAwareEntityClassifier(),
                drawingContentFirstClassifier: new DrawingContentFirstEntityClassifier(),
                quantityExtractor: new QuantityFieldExtractor());
        }

        public SheetAnalysisDebugResult Run(SingleSheetDebugInput input)
        {
            var options = new SheetAnalysisOptions();

            var snapshots = BuildSnapshots(input);
            var geometryClusters = BuildGeometryClusters(snapshots);
            var viewCandidates = BuildViewCandidates(input, geometryClusters);
            var bestPack = SelectBestViewPack(viewCandidates);

            var regions = DetectRegions(snapshots, options);

            var firstPass = ClassifyByRegion(snapshots, regions, options);
            var finalPass = RefineWithContentFirst(snapshots, regions, options);
            var quantity = ExtractQuantity(snapshots, regions, finalPass);

            return new SheetAnalysisDebugResult(
                input.SourceName,
                input.SheetBounds,
                ToObjectList(snapshots),
                ToObjectList(geometryClusters),
                ToObjectList(viewCandidates),
                bestPack,
                ToObjectList(regions),
                ToObjectList(firstPass),
                ToObjectList(finalPass),
                quantity);
        }

        private static string DescribePublicMethods(object target)
        {
            var type = target.GetType();

            var methods = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .OrderBy(m => m.Name)
                .ToArray();

            if (methods.Length == 0)
                return $"Type={type.FullName}, PublicMethods=(none)";

            var lines = new List<string>();
            lines.Add($"Type={type.FullName}");

            foreach (var method in methods)
            {
                var ps = method.GetParameters();
                var paramText = string.Join(", ",
                    ps.Select(p => $"{p.ParameterType.Name} {p.Name}"));

                lines.Add($"  - {method.ReturnType.Name} {method.Name}({paramText})");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private object BuildSnapshots(SingleSheetDebugInput input)
        {
            return _snapshotBuilder.Build(input.SheetFilePath);
        }

        private object BuildSnapshots_old(SingleSheetDebugInput input)
        {
            // 우선순위:
            // 1) (db, tr, entityIds, sheetBounds)
            // 2) (db, tr, entities, sheetBounds)
            // 3) (entities, sheetBounds)
            // 4) (entityIds, sheetBounds)
            // 5) (entities)
            // 6) (entityIds)
            return InvokeAny(
                _snapshotBuilder,
                new[] { "Build", "Create", "BuildSnapshots" },
                new object?[] { input.Database, input.Transaction, input.EntityIds, input.SheetBounds },
                new object?[] { input.Database, input.Transaction, input.Entities, input.SheetBounds },
                new object?[] { input.Entities, input.SheetBounds },
                new object?[] { input.EntityIds, input.SheetBounds },
                new object?[] { input.Entities },
                new object?[] { input.EntityIds });
        }

        private object BuildGeometryClusters(object snapshots)
        {
            return InvokeAny(
                _geometryClusterBuilder,
                new[] { "Build" },
                new object?[] { snapshots, null });
        }

        private object BuildViewCandidates(SingleSheetDebugInput input, object geometryClusters)
        {
            var sheetBounds2D = ConvertToBounds2D(input.SheetBounds);

            return InvokeAny(
                _viewCandidateBuilder,
                new[] { "Build" },
                new object?[] { geometryClusters, sheetBounds2D, null });
        }

        private static object ConvertToBounds2D(Extents3d ext)
        {
            var boundsType = typeof(FluxCAD.SheetAnalysis.GeometryClusterBuilder)
                .Assembly
                .GetType("FluxCAD.SheetAnalysis.Bounds2D");

            if (boundsType == null)
                throw new InvalidOperationException("FluxCAD.SheetAnalysis.Bounds2D 타입을 찾지 못했습니다.");

            // 1) ctor(double minX, double minY, double maxX, double maxY) 시도
            var ctor4 = boundsType.GetConstructor(new[]
            {
        typeof(double), typeof(double), typeof(double), typeof(double)
    });

            if (ctor4 != null)
            {
                return ctor4.Invoke(new object[]
                {
            ext.MinPoint.X,
            ext.MinPoint.Y,
            ext.MaxPoint.X,
            ext.MaxPoint.Y
                });
            }

            // 2) parameterless ctor + property set 시도
            var ctor0 = boundsType.GetConstructor(Type.EmptyTypes);
            if (ctor0 != null)
            {
                var obj = ctor0.Invoke(null);

                SetPropertyIfExists(obj, "MinX", ext.MinPoint.X);
                SetPropertyIfExists(obj, "MinY", ext.MinPoint.Y);
                SetPropertyIfExists(obj, "MaxX", ext.MaxPoint.X);
                SetPropertyIfExists(obj, "MaxY", ext.MaxPoint.Y);

                SetPropertyIfExists(obj, "Left", ext.MinPoint.X);
                SetPropertyIfExists(obj, "Bottom", ext.MinPoint.Y);
                SetPropertyIfExists(obj, "Right", ext.MaxPoint.X);
                SetPropertyIfExists(obj, "Top", ext.MaxPoint.Y);

                return obj;
            }

            throw new InvalidOperationException(
                "Bounds2D 생성에 실패했습니다. 생성자 또는 settable property 구조를 확인해야 합니다.");
        }

        private static void SetPropertyIfExists(object target, string propertyName, object value)
        {
            var prop = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);

            if (prop == null || !prop.CanWrite)
                return;

            if (prop.PropertyType.IsAssignableFrom(value.GetType()))
            {
                prop.SetValue(target, value);
                return;
            }

            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(target, converted);
        }

        private object? SelectBestViewPack(object viewCandidates)
        {
            if (TryInvokeAny(
                _viewPackScorer,
                new[] { "SelectBest", "FindBest", "BuildBest", "Score" },
                out var result,
                new object?[] { viewCandidates }))
            {
                return result;
            }

            return null;
        }

        private object? DetectRegions(object snapshots, object options)
        {
            return InvokeAny(
                _regionDetector,
                new[] { "DetectRegions", "Detect", "Build", "Create", "Run" },
                new[]
                {
            new object[] { snapshots, options },
            new object[] { snapshots }
                });
        }

        private static object? DetectRegions(object detector, object snapshots, object options)
        {
            return InvokeAny(
                detector,
                new[] { "DetectRegions", "Detect", "Build", "Create", "Run" },
                new[]
                {
            new object[] { snapshots, options },
            new object[] { snapshots } // 혹시 다른 detector가 1-arg 패턴이면 fallback
                });
        }

        private object ClassifyByRegion(object snapshots, object regions, object options)
        {
            return InvokeAny(
                _regionAwareClassifier,
                new[] { "Classify", "Build", "Run" },
                new object?[] { snapshots, regions, options },
                new object?[] { regions, snapshots, options },
                new object?[] { snapshots, regions },
                new object?[] { regions, snapshots });
        }

        private object RefineWithContentFirst(object snapshots, object regions, object options)
        {
            return InvokeAny(
                _drawingContentFirstClassifier,
                new[] { "Classify", "Refine", "Build", "Run" },
                new object?[] { snapshots, regions, options },
                new object?[] { regions, snapshots, options },
                new object?[] { snapshots, regions });
        }

        private object? ExtractQuantity(object snapshots, object regions, object finalPass)
        {
            if (TryInvokeAny(
                _quantityExtractor,
                new[] { "Extract", "TryExtract", "Read", "Run" },
                out var result,
                new object?[] { snapshots, regions, finalPass },
                new object?[] { regions, finalPass },
                new object?[] { finalPass },
                new object?[] { snapshots, regions }))
            {
                return result;
            }

            return null;
        }

        private static string DescribeArgSets(object?[][] argSets)
        {
            var lines = new List<string>();

            for (int i = 0; i < argSets.Length; i++)
            {
                var args = argSets[i];
                var argText = string.Join(", ",
                    args.Select(a => a == null ? "null" : a.GetType().FullName));

                lines.Add($"ArgSet[{i}] = ({argText})");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static object InvokeAny_old(object target, string[] methodNames, params object?[][] argSets)
        {
            if (TryInvokeAny(target, methodNames, out var result, argSets))
                return result!;

            throw new InvalidOperationException(
    $"[{target.GetType().Name}] 에서 호출 가능한 메서드를 찾지 못했습니다. " +
    $"methodNames = {string.Join(", ", methodNames)}{Environment.NewLine}" +
    $"{DescribePublicMethods(target)}{Environment.NewLine}" +
    $"{DescribeArgSets(argSets)}");
        }

        private static object InvokeAny(object target, string[] methodNames, params object?[][] argSets)
        {
            if (TryInvokeAny(target, methodNames, out var result, argSets))
                return result!;

            throw new InvalidOperationException(
                $"[{target.GetType().Name}] 에서 호출 가능한 메서드를 찾지 못했습니다. " +
                $"methodNames = {string.Join(", ", methodNames)}{Environment.NewLine}" +
                $"{DescribePublicMethods(target)}{Environment.NewLine}" +
                $"{DescribeArgSets(argSets)}");
        }

        private static bool CanInvoke(MethodInfo method, object?[] args)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != args.Length)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                var arg = args[i];

                if (arg == null)
                {
                    if (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null)
                        return false;
                    continue;
                }

                var argType = arg.GetType();

                if (!paramType.IsAssignableFrom(argType))
                    return false;
            }

            return true;
        }

        private static bool TryInvokeAny(
            object target,
            string[] methodNames,
            out object? result,
            params object?[][] argSets)
        {
            result = null;
            Exception? lastError = null;

            foreach (var methodName in methodNames)
            {
                foreach (var args in argSets)
                {
                    if (TryInvoke(target, methodName, args, out result, out var error))
                        return true;

                    if (error != null)
                        lastError = error;
                }
            }

            return false;
        }

        private static bool TryInvoke(
            object target,
            string methodName,
            object?[] args,
            out object? result,
            out Exception? error)
        {
            result = null;
            error = null;

            var methods = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.Name == methodName)
                .ToArray();

            foreach (var method in methods)
            {
                var ps = method.GetParameters();

                // 인자를 더 많이 넘긴 경우는 탈락
                if (args.Length > ps.Length)
                    continue;

                var finalArgs = new object?[ps.Length];
                bool ok = true;

                for (int i = 0; i < ps.Length; i++)
                {
                    if (i < args.Length)
                    {
                        var arg = args[i];
                        var pType = ps[i].ParameterType;

                        if (arg == null)
                        {
                            if (pType.IsValueType && Nullable.GetUnderlyingType(pType) == null)
                            {
                                ok = false;
                                break;
                            }

                            finalArgs[i] = null;
                        }
                        else if (pType.IsAssignableFrom(arg.GetType()))
                        {
                            finalArgs[i] = arg;
                        }
                        else
                        {
                            ok = false;
                            break;
                        }
                    }
                    else
                    {
                        if (ps[i].HasDefaultValue)
                        {
                            finalArgs[i] = ps[i].DefaultValue;
                        }
                        else
                        {
                            ok = false;
                            break;
                        }
                    }
                }

                if (!ok)
                    continue;

                try
                {
                    result = method.Invoke(target, finalArgs);
                    return true;
                }
                catch (TargetInvocationException tie)
                {
                    error = tie.InnerException ?? tie;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }

            return false;
        }

        private static bool AreArgumentsCompatible(ParameterInfo[] ps, object?[] args)
        {
            for (int i = 0; i < ps.Length; i++)
            {
                var pType = ps[i].ParameterType;
                var arg = args[i];

                if (arg == null)
                {
                    if (pType.IsValueType && Nullable.GetUnderlyingType(pType) == null)
                        return false;

                    continue;
                }

                var aType = arg.GetType();

                if (pType.IsAssignableFrom(aType))
                    continue;

                return false;
            }

            return true;
        }

        private static IReadOnlyList<object> ToObjectList(object? value)
        {
            if (value == null)
                return Array.Empty<object>();

            if (value is string)
                return new[] { value };

            if (value is IEnumerable enumerable)
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                {
                    if (item != null)
                        list.Add(item);
                }
                return list;
            }

            return new[] { value };
        }
    }

    internal sealed record SheetAnalysisDebugResult(
        string SourceName,
        object SheetBounds,
        IReadOnlyList<object> Snapshots,
        IReadOnlyList<object> GeometryClusters,
        IReadOnlyList<object> ViewCandidates,
        object? BestViewPack,
        IReadOnlyList<object> Regions,
        IReadOnlyList<object> FirstPassClassified,
        IReadOnlyList<object> FinalPassClassified,
        object? QuantityResult);
}