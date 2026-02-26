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

        public static PartAttributes ParseJsonNodes(List<JsonSpatialNode> textNodes)
        {
            var attr = new PartAttributes { Material = "UNKNOWN", Quantity = 1 };

            // 1. MTEXT 제어 코드 제거 및 텍스트 통합
            string rawFullText = string.Join(" ", textNodes.Select(n => n.Content));
            string cleanText = Regex.Replace(rawFullText, @"\\[A-Z].*?;", ""); // CAD 제어문 제거

            // 2. 재질 (더 넓은 범위)
            var matMatch = Regex.Match(cleanText, @"(SS400|SUS\d+|AL|SM45C|S\d+C|SPCC|SHP|GI)", RegexOptions.IgnoreCase);
            if (matMatch.Success) attr.Material = matMatch.Value.ToUpper();

            // 3. 두께 (T10, 10T, 10.0t 모두 대응)
            var thickMatch = Regex.Match(cleanText, @"([tT]\s*=?\s*(\d+(\.\d+)?))|((\d+(\.\d+)?)\s*[tT])");
            if (thickMatch.Success)
            {
                string val = Regex.Match(thickMatch.Value, @"\d+(\.\d+)?").Value;
                double.TryParse(val, out double t);
                attr.Thickness = t;
            }

            // 4. 수량 (EA, 개, SET 등)
            var qtyMatch = Regex.Match(cleanText, @"(\d+)\s*(EA|ea|개|SET|set)", RegexOptions.IgnoreCase);
            if (qtyMatch.Success)
            {
                int.TryParse(qtyMatch.Groups[1].Value, out int q);
                attr.Quantity = q;
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
