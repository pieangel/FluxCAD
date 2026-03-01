using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Adapter26
{
    public static class AttributeParser
    {
        public static PartAttributes Parse(List<SpatialNode> texts)
        {
            var attr = new PartAttributes();
            // 실제 파싱 로직은 추후 구현하되, 우선 컴파일 에러만 해결
            foreach (var t in texts)
            {
                // Regex 등을 사용한 분석 예정
            }
            return attr;
        }


        public static List<LaserPart> ExtractPartsFromClusters(List<CadEntity> analyzedEntities)
        {
            var finalInventory = new List<LaserPart>();

            // 1. ClusterId 별로 그룹화 (null인 경우는 군집화 실패이므로 제외)
            var groups = analyzedEntities
                .Where(e => e.ClusterId.HasValue)
                .GroupBy(e => e.ClusterId.Value);

            foreach (var group in groups)
            {
                // 2. 해당 그룹에서 가장 큰 외곽선을 찾음 (보통 부품의 몸체)
                var body = group.FirstOrDefault(e => e.EntityType.Contains("POLYLINE"));
                if (body == null) continue;

                var part = new LaserPart
                {
                    PartId = body.Handle,
                    // 중심점 기반으로 가공 영역(Bounds) 설정
                    Bounds = new BoundingBox(group.Min(e => e.MinX), group.Min(e => e.MinY),
                                             group.Max(e => e.MaxX), group.Max(e => e.MaxY))
                };

                // 3. 그룹 내 포함된 텍스트들을 모아 속성 파싱
                var groupTexts = group
                    .Where(e => !string.IsNullOrEmpty(e.TextContent))
                    .Select(e => new JsonSpatialNode { Content = e.TextContent, Type = e.EntityType })
                    .ToList();

                if (groupTexts.Any())
                {
                    var attrs = AttributeParser.ParseJsonNodes(groupTexts);
                    part.Material = attrs.Material;
                    part.Thickness = attrs.Thickness;
                    part.Quantity = attrs.Quantity;
                }

                finalInventory.Add(part);
            }

            return finalInventory;
        }

        public static PartAttributes ParseJsonNodes(List<JsonSpatialNode> textNodes)
        {
            // 기본값 설정
            var attr = new PartAttributes { Material = "UNKNOWN", Quantity = 1, Thickness = 0 };

            if (textNodes == null || !textNodes.Any()) return attr;

            // 1. 모든 텍스트 결합 (노이즈 제거 및 공백 정규화)
            string rawFullText = string.Join(" | ", textNodes.Select(n => n.Content));
            // CAD 특수 제어문자(\P, \f...;) 제거
            string cleanText = Regex.Replace(rawFullText, @"\\[A-Z].*?;", " ");

            // --- [A] 재질(Material) 파싱 ---
            // 우선순위: 1. 금속 재질(SS400 등), 2. 표면처리(Paint 등)
            var metalMatch = Regex.Match(cleanText, @"(SS400|SUS\d+|AL|SM45C|S\d+C|SPCC|SHP|GI|STS\d+)", RegexOptions.IgnoreCase);
            var finishMatch = Regex.Match(cleanText, @"(아연도금|Zn\s*도금|Paint|Mirror)", RegexOptions.IgnoreCase);

            if (metalMatch.Success)
                attr.Material = metalMatch.Value.ToUpper();
            else if (finishMatch.Success)
                attr.Material = finishMatch.Value.ToUpper();


            // --- [B] 두께(Thickness) 파싱 ---
            // 패턴: 숫자 + T (예: 10T, 3.2t, T20)
            var thickMatch = Regex.Match(cleanText, @"(\d+(\.\d+)?)\s*(T|t|thk|THK)", RegexOptions.IgnoreCase);
            if (!thickMatch.Success) // T가 뒤에 없는 경우 (예: T10)
                thickMatch = Regex.Match(cleanText, @"(T|t|thk|THK)\s*(\d+(\.\d+)?)", RegexOptions.IgnoreCase);

            if (thickMatch.Success)
            {
                var valStr = Regex.Match(thickMatch.Value, @"\d+(\.\d+)?").Value;
                if (double.TryParse(valStr, out double t)) attr.Thickness = t;
            }


            // --- [C] 수량(Quantity) 파싱 ---
            // 1순위: 곱셈 수식 (8*2, 4X3)
            var formulaMatch = Regex.Match(cleanText, @"(\d+)\s*[\*xX]\s*(\d+)");
            if (formulaMatch.Success)
            {
                if (int.TryParse(formulaMatch.Groups[1].Value, out int baseQty) &&
                    int.TryParse(formulaMatch.Groups[2].Value, out int multiplier))
                {
                    attr.Quantity = baseQty * multiplier;
                }
            }
            else
            {
                // 2순위: 일반 숫자 수량 (16EA, 16개)
                var simpleQtyMatch = Regex.Match(cleanText, @"(\d+)\s*(EA|ea|개|SET|set|수량)", RegexOptions.IgnoreCase);
                if (simpleQtyMatch.Success)
                {
                    int.TryParse(simpleQtyMatch.Groups[1].Value, out int q);
                    attr.Quantity = q;
                }
            }

            return attr;
        }
        // 기존 Parse 함수는 유지하고, JsonSpatialNode용 오버로드를 추가합니다.
        /*
        public static PartAttributes ParseJsonNodes(List<JsonSpatialNode> textNodes)
        {
            var attr = new PartAttributes();

            foreach (var node in textNodes)
            {
                string content = node.Content ?? "";
                if (string.IsNullOrWhiteSpace(content)) continue;

                // 1. 재질 파싱 (예: SS400, SUS304...)
                if (content.Contains("SS400")) attr.Material = "SS400";
                else if (content.Contains("SUS304")) attr.Material = "SUS304";

                // 2. 두께 파싱 (예: 12T, 9t...)
                var tMatch = System.Text.RegularExpressions.Regex.Match(content, @"(\d+)\s*[Tt]");
                if (tMatch.Success)
                {
                    attr.Thickness = double.Parse(tMatch.Groups[1].Value);
                }

                // 3. 수량 파싱 (예: 4EA, Q'TY : 5...)
                var qMatch = System.Text.RegularExpressions.Regex.Match(content, @"(\d+)\s*[Ee][Aa]");
                if (qMatch.Success)
                {
                    attr.Quantity = int.Parse(qMatch.Groups[1].Value);
                }
            }

            return attr;
        }
        */
    }
}
