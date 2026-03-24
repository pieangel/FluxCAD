using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace FluxCAD.SheetAnalysis
{
    public sealed class QuantityFieldExtractor : IFieldExtractor
    {
        private static readonly Regex InlineQtyRegex = new(
            @"\b(Q['’]?\s*TY|QTY|Q'TY|QUANTITY|수량)\b\s*[:=]?\s*(?<n>\d{1,4})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InlineUnitQtyRegex = new(
            @"\b(?<n>\d{1,4})\s*(EA|PCS|SET)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InlineXSetRegex = new(
            @"\b[X×]\s*(?<n>\d{1,4})\s*(SET|EA|PCS)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PureIntegerRegex = new(
            @"^\s*(?<n>\d{1,4})\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public void Extract(SheetAnalysisResult result, SheetAnalysisOptions options)
        {
            if (result == null)
                return;

            var candidates = new List<QuantityCandidate>();

            var entities = result.Entities ?? new List<AnalyzedEntity>();
            if (entities.Count == 0)
            {
                MarkNotFound(result, "no analyzed entities available for quantity extraction");
                return;
            }

            // 1) 가장 먼저 명시적인 inline quantity를 찾습니다.
            candidates.AddRange(FindInlineCandidates(entities));

            // 2) Meta/Title region에서 label + value 쌍을 찾습니다.
            candidates.AddRange(FindLabelNeighborCandidates(entities));

            // 3) 최종 판단
            Decide(result, candidates);
        }

        private static IEnumerable<QuantityCandidate> FindInlineCandidates(
            IReadOnlyList<AnalyzedEntity> entities)
        {
            foreach (var ae in entities)
            {
                if (!ae.Entity.IsTextLike)
                    continue;

                var text = GetNormalizedText(ae);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (LooksLikeEngineeringNonQuantityText(text))
                    continue;

                // QTY: 4 / QUANTITY 4
                var m1 = InlineQtyRegex.Match(text);
                if (m1.Success && TryParsePositiveInt(m1.Groups["n"].Value, out var qty1))
                {
                    yield return BuildCandidate(
                        ae,
                        qty1,
                        text,
                        0.97,
                        "explicit inline quantity label + number");
                    continue;
                }

                // 4 SET / 4EA / 4 PCS
                var m2 = InlineUnitQtyRegex.Match(text);
                if (m2.Success && TryParsePositiveInt(m2.Groups["n"].Value, out var qty2))
                {
                    double score = BaseRegionScore(ae) + 0.82;
                    if (text.Contains("SET", StringComparison.OrdinalIgnoreCase))
                        score += 0.04;

                    yield return BuildCandidate(
                        ae,
                        qty2,
                        text,
                        Clamp01(score),
                        "inline quantity with unit");
                    continue;
                }

                // X 4SET / X 4
                var m3 = InlineXSetRegex.Match(text);
                if (m3.Success && TryParsePositiveInt(m3.Groups["n"].Value, out var qty3))
                {
                    double score = BaseRegionScore(ae) + 0.78;
                    yield return BuildCandidate(
                        ae,
                        qty3,
                        text,
                        Clamp01(score),
                        "inline X + quantity pattern");
                }
            }
        }

        private static IEnumerable<QuantityCandidate> FindLabelNeighborCandidates(
            IReadOnlyList<AnalyzedEntity> entities)
        {
            var labelEntities = entities
                .Where(ae => ae.Entity.IsTextLike)
                .Where(ae => IsQuantityLabel(GetNormalizedText(ae)))
                .Where(ae => ae.RegionKind == RegionKind.MetaTable || ae.RegionKind == RegionKind.TitleBlock)
                .ToList();

            foreach (var label in labelEntities)
            {
                var regionPeers = entities
                    .Where(ae => ae.Entity.IsTextLike)
                    .Where(ae => ae.Entity.Handle != label.Entity.Handle)
                    .Where(ae => string.Equals(ae.RegionId, label.RegionId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var neighborCandidates = new List<(AnalyzedEntity Entity, int Qty, double Score, string Reason)>();

                foreach (var peer in regionPeers)
                {
                    var text = GetNormalizedText(peer);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (LooksLikeEngineeringNonQuantityText(text))
                        continue;

                    if (!TryExtractStandaloneQuantityValue(text, out var qty, out var standaloneReason))
                        continue;

                    var relationScore = ComputeLabelRelationScore(label.Entity, peer.Entity, out var relationReason);
                    if (relationScore <= 0)
                        continue;

                    var score = Clamp01(0.52 + BaseRegionScore(peer) + relationScore);

                    // 너무 큰 값은 살짝 보수적으로
                    if (qty > 500)
                        score -= 0.06;

                    neighborCandidates.Add((peer, qty, score, $"{standaloneReason}; {relationReason}"));
                }

                foreach (var pick in neighborCandidates
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => Distance(label.Entity, x.Entity.Entity))
                    .Take(3))
                {
                    yield return BuildCandidate(
                        pick.Entity,
                        pick.Qty,
                        GetNormalizedText(pick.Entity),
                        pick.Score,
                        $"quantity label neighbor: {pick.Reason}",
                        label.Entity.Handle);
                }
            }
        }

        private static void Decide(
            SheetAnalysisResult result,
            List<QuantityCandidate> candidates)
        {
            if (candidates.Count == 0)
            {
                MarkNotFound(result, "no reliable quantity candidate found");
                return;
            }

            // 같은 값이 반복적으로 나오는 경우 합산 점수로 안정화
            var grouped = candidates
                .GroupBy(c => c.Quantity)
                .Select(g => new QuantityAggregate
                {
                    Quantity = g.Key,
                    TotalScore = g.Max(x => x.Score) + Math.Min(0.20, (g.Count() - 1) * 0.05),
                    Count = g.Count(),
                    Best = g.OrderByDescending(x => x.Score).First()
                })
                .OrderByDescending(x => x.TotalScore)
                .ThenByDescending(x => x.Count)
                .ThenByDescending(x => x.Best.Score)
                .ToList();

            var best = grouped[0];
            var second = grouped.Count > 1 ? grouped[1] : null;

            // 확정 규칙
            bool clearlyConfirmed =
                best.Best.Score >= 0.88 &&
                (second == null || best.TotalScore - second.TotalScore >= 0.12);

            bool repeatedSameValueConfirmed =
                best.Best.Score >= 0.76 &&
                best.Count >= 2 &&
                (second == null || best.TotalScore - second.TotalScore >= 0.08);

            // 충돌 규칙
            bool ambiguousConflict =
                second != null &&
                best.Quantity != second.Quantity &&
                best.TotalScore >= 0.70 &&
                second.TotalScore >= 0.70 &&
                Math.Abs(best.TotalScore - second.TotalScore) < 0.12;

            if (ambiguousConflict)
            {
                MarkAmbiguous(
                    result,
                    $"multiple competing quantity candidates: {best.Quantity} vs {second!.Quantity}",
                    best.Best,
                    second.Best);
                return;
            }

            if (clearlyConfirmed || repeatedSameValueConfirmed)
            {
                MarkConfirmed(result, best.Best, best.TotalScore, best.Count);
                return;
            }

            // 약한 후보 하나만 있는 경우도 억지 확정하지 않습니다.
            if (best.Best.Score >= 0.60)
            {
                MarkAmbiguous(
                    result,
                    $"quantity candidate exists but confidence is insufficient for confirmation: {best.Quantity}",
                    best.Best,
                    second?.Best);
                return;
            }

            MarkNotFound(result, "only weak quantity-like texts were found");
        }

        private static QuantityCandidate BuildCandidate(
            AnalyzedEntity ae,
            int qty,
            string sourceText,
            double score,
            string reason,
            string? labelHandle = null)
        {
            return new QuantityCandidate
            {
                Quantity = qty,
                Score = Clamp01(score),
                SourceText = sourceText,
                SourceHandle = ae.Entity.Handle,
                LabelHandle = labelHandle,
                RegionId = ae.RegionId,
                RegionKind = ae.RegionKind,
                Reason = reason
            };
        }

        private static bool TryExtractStandaloneQuantityValue(
            string text,
            out int quantity,
            out string reason)
        {
            quantity = 0;
            reason = "";

            var pure = PureIntegerRegex.Match(text);
            if (pure.Success && TryParsePositiveInt(pure.Groups["n"].Value, out quantity))
            {
                reason = "pure integer near quantity label";
                return true;
            }

            var unit = InlineUnitQtyRegex.Match(text);
            if (unit.Success && TryParsePositiveInt(unit.Groups["n"].Value, out quantity))
            {
                reason = "unit-based quantity near label";
                return true;
            }

            var xset = InlineXSetRegex.Match(text);
            if (xset.Success && TryParsePositiveInt(xset.Groups["n"].Value, out quantity))
            {
                reason = "X+n pattern near label";
                return true;
            }

            return false;
        }

        private static bool IsQuantityLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("QTY", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Q'TY", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("QUANTITY", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("수량", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetNormalizedText(AnalyzedEntity ae)
        {
            return (ae.Entity.TextNormalized ?? ae.Entity.Text ?? string.Empty).Trim();
        }

        private static bool LooksLikeEngineeringNonQuantityText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var t = text.ToUpperInvariant();

            if (t.Contains("THRU") ||
                t.Contains("TAP") ||
                t.Contains("C'BORE") ||
                t.Contains("CBORE") ||
                t.Contains("CBOR") ||
                t.Contains("R") ||
                t.Contains("Ø") ||
                t.Contains("PHI") ||
                t.Contains("M3") ||
                t.Contains("M4") ||
                t.Contains("M5") ||
                t.Contains("M6") ||
                t.Contains("M8"))
            {
                return true;
            }

            if (t.Contains("."))
                return true;

            // 2-M3, 4-Ø10 같은 패턴 제외
            if (Regex.IsMatch(t, @"^\s*\d+\s*[-].+$"))
                return true;

            return false;
        }

        private static double BaseRegionScore(AnalyzedEntity ae)
        {
            return ae.RegionKind switch
            {
                RegionKind.MetaTable => 0.10,
                RegionKind.TitleBlock => 0.06,
                RegionKind.DimensionNote => -0.12,
                RegionKind.Geometry => -0.18,
                RegionKind.Preview => -0.15,
                _ => -0.04
            };
        }

        private static double ComputeLabelRelationScore(
            SheetEntity label,
            SheetEntity value,
            out string reason)
        {
            reason = "unrelated to label";

            var dx = value.Anchor.X - label.Anchor.X;
            var dy = value.Anchor.Y - label.Anchor.Y;

            double labelH = label.TextHeight > 0 ? label.TextHeight : 3.5;
            double valueH = value.TextHeight > 0 ? value.TextHeight : 3.5;
            double baseH = Math.Max(labelH, valueH);

            double rowTol = Math.Max(8.0, baseH * 1.8);
            double colTol = Math.Max(10.0, baseH * 2.0);
            double maxRightDistance = Math.Max(120.0, baseH * 35.0);
            double maxDownDistance = Math.Max(80.0, baseH * 22.0);

            // 오른쪽 셀 값
            if (dx >= 0 && dx <= maxRightDistance && Math.Abs(dy) <= rowTol)
            {
                reason = "right-side value near quantity label";
                return 0.34;
            }

            // 아래 셀 값
            if (dy <= 0 && Math.Abs(dx) <= colTol && Math.Abs(dy) <= maxDownDistance)
            {
                reason = "below-label value near quantity label";
                return 0.24;
            }

            // 아주 가까운 경우 보조 허용
            var distance = Distance(label, value);
            if (distance <= Math.Max(16.0, baseH * 4.5))
            {
                reason = "very close to quantity label";
                return 0.18;
            }

            return 0.0;
        }

        private static double Distance(SheetEntity a, SheetEntity b)
        {
            return Bounds2DHelper.Distance(a.Anchor, b.Anchor);
        }

        private static bool TryParsePositiveInt(string text, out int value)
        {
            if (int.TryParse(text.Trim(), out value))
                return value > 0;

            value = 0;
            return false;
        }

        private static void MarkConfirmed(
            SheetAnalysisResult result,
            QuantityCandidate candidate,
            double aggregateScore,
            int evidenceCount)
        {
            SetQuantityStatus(result.Quantity, "Confirmed", "Found", "Success");
            SetQuantityReason(result.Quantity,
                $"confirmed quantity={candidate.Quantity}; {candidate.Reason}; evidenceCount={evidenceCount}");

            SetOptionalQuantityValue(result.Quantity, candidate.Quantity);
            SetOptionalQuantityText(result.Quantity, candidate.SourceText);
            SetOptionalQuantityConfidence(result.Quantity, Clamp01(Math.Max(candidate.Score, aggregateScore)));
            SetOptionalQuantityRegion(result.Quantity, candidate.RegionId);
            SetOptionalQuantityHandle(result.Quantity, candidate.SourceHandle, candidate.LabelHandle);
        }

        private static void MarkAmbiguous(
            SheetAnalysisResult result,
            string reason,
            QuantityCandidate first,
            QuantityCandidate? second)
        {
            SetQuantityStatus(result.Quantity, "Ambiguous");
            SetQuantityReason(
                result.Quantity,
                second == null
                    ? $"ambiguous quantity extraction: {reason}; best={first.Quantity}"
                    : $"ambiguous quantity extraction: {reason}; best={first.Quantity}, second={second.Quantity}");

            SetOptionalQuantityValue(result.Quantity, first.Quantity);
            SetOptionalQuantityText(result.Quantity, first.SourceText);
            SetOptionalQuantityConfidence(result.Quantity, Math.Min(0.79, first.Score));
            SetOptionalQuantityRegion(result.Quantity, first.RegionId);
            SetOptionalQuantityHandle(result.Quantity, first.SourceHandle, first.LabelHandle);

            result.Warnings.Add(reason);
        }

        private static void MarkNotFound(
            SheetAnalysisResult result,
            string reason)
        {
            SetQuantityStatus(result.Quantity, "NotFound");
            SetQuantityReason(result.Quantity, reason);
            result.Warnings.Add(reason);
        }

        // ----------------------------
        // QuantityInfo reflection helpers
        // ----------------------------

        private static void SetQuantityStatus(object quantityInfo, params string[] preferredNames)
        {
            var prop = quantityInfo.GetType().GetProperty("Status", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                return;

            var enumType = prop.PropertyType;
            if (!enumType.IsEnum)
                return;

            foreach (var name in preferredNames)
            {
                try
                {
                    var value = Enum.Parse(enumType, name, ignoreCase: true);
                    prop.SetValue(quantityInfo, value);
                    return;
                }
                catch
                {
                    // 다음 이름 시도
                }
            }
        }

        private static void SetQuantityReason(object quantityInfo, string reason)
        {
            SetIfExists(quantityInfo, "Reason", reason);
        }

        private static void SetOptionalQuantityValue(object quantityInfo, int quantity)
        {
            if (TrySetNumeric(quantityInfo, "Value", quantity)) return;
            if (TrySetNumeric(quantityInfo, "Quantity", quantity)) return;
            if (TrySetNumeric(quantityInfo, "ConfirmedValue", quantity)) return;
            if (TrySetNumeric(quantityInfo, "DetectedValue", quantity)) return;
        }

        private static void SetOptionalQuantityText(object quantityInfo, string sourceText)
        {
            if (SetIfExists(quantityInfo, "Text", sourceText)) return;
            if (SetIfExists(quantityInfo, "RawText", sourceText)) return;
            if (SetIfExists(quantityInfo, "SourceText", sourceText)) return;
        }

        private static void SetOptionalQuantityConfidence(object quantityInfo, double confidence)
        {
            if (TrySetNumeric(quantityInfo, "Confidence", confidence)) return;
            if (TrySetNumeric(quantityInfo, "Score", confidence)) return;
        }

        private static void SetOptionalQuantityRegion(object quantityInfo, string? regionId)
        {
            if (string.IsNullOrWhiteSpace(regionId))
                return;

            if (SetIfExists(quantityInfo, "RegionId", regionId)) return;
            if (SetIfExists(quantityInfo, "SourceRegionId", regionId)) return;
        }

        private static void SetOptionalQuantityHandle(object quantityInfo, string? handle, string? labelHandle)
        {
            if (!string.IsNullOrWhiteSpace(handle))
            {
                if (SetIfExists(quantityInfo, "Handle", handle)) { }
                else if (SetIfExists(quantityInfo, "SourceHandle", handle)) { }
            }

            if (!string.IsNullOrWhiteSpace(labelHandle))
            {
                if (SetIfExists(quantityInfo, "LabelHandle", labelHandle)) { }
                else if (SetIfExists(quantityInfo, "AnchorHandle", labelHandle)) { }
            }
        }

        private static bool SetIfExists(object target, string propertyName, object value)
        {
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                return false;

            try
            {
                if (value == null)
                {
                    prop.SetValue(target, null);
                    return true;
                }

                if (prop.PropertyType.IsAssignableFrom(value.GetType()))
                {
                    prop.SetValue(target, value);
                    return true;
                }

                var converted = Convert.ChangeType(value, prop.PropertyType);
                prop.SetValue(target, converted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetNumeric(object target, string propertyName, double value)
        {
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                return false;

            try
            {
                var converted = Convert.ChangeType(value, prop.PropertyType);
                prop.SetValue(target, converted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double Clamp01(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }

        private sealed class QuantityCandidate
        {
            public int Quantity { get; set; }
            public double Score { get; set; }
            public string SourceText { get; set; } = "";
            public string? SourceHandle { get; set; }
            public string? LabelHandle { get; set; }
            public string? RegionId { get; set; }
            public RegionKind RegionKind { get; set; }
            public string Reason { get; set; } = "";
        }

        private sealed class QuantityAggregate
        {
            public int Quantity { get; set; }
            public double TotalScore { get; set; }
            public int Count { get; set; }
            public QuantityCandidate Best { get; set; } = new();
        }
    }
}