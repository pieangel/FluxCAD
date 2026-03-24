using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ComponentRoleAnalyzer : IComponentRoleAnalyzer
    {
        public ComponentAnalysisResult Analyze(
            SemanticComponent component,
            SheetAnalysisContext context)
        {
            var result = new ComponentAnalysisResult
            {
                Component = component
            };

            var scores = new List<RoleScore>
            {
                ScoreGeometryCore(component, context),
                ScoreDimensionRelated(component, context),
                ScoreTitleBlock(component, context),
                ScoreMetaField(component, context),
                ScoreMarginAnnotation(component, context),
                ScoreAuxiliaryMarker(component, context),
                ScoreProjectionMethodSymbol(component, context)
            };

            result.Scores.AddRange(scores);

            var best = scores.OrderByDescending(x => x.Score).First();

            if (best.Score < 20)
            {
                result.FinalRole = ComponentRole.Unknown;
                result.Confidence = RoleConfidence.Low;
                result.FinalReason = "no role reached threshold";
                return result;
            }

            result.FinalRole = best.Role;
            result.Confidence = best.Score >= 70
                ? RoleConfidence.High
                : best.Score >= 40
                    ? RoleConfidence.Medium
                    : RoleConfidence.Low;

            result.FinalReason = string.Join("; ", best.Reasons);

            foreach (var s in scores.Where(x => x.Score > 0))
            {
                foreach (var r in s.Reasons)
                    result.Evidence.Notes.Add($"[{s.Role}] {r}");
            }

            return result;
        }

        private RoleScore ScoreGeometryCore(SemanticComponent c, SheetAnalysisContext ctx)
        {
            var f = c.Features;
            var score = new RoleScore { Role = ComponentRole.GeometryCore };

            if (f.HasClosedOutlineLikeShape)
            {
                score.Score += 25;
                score.Reasons.Add("closed-outline-like shape detected");
            }

            if (f.ConnectedToDimensionCluster)
            {
                score.Score += 20;
                score.Reasons.Add("connected to dimension cluster");
            }

            if (f.ConnectedToGeometryCluster)
            {
                score.Score += 20;
                score.Reasons.Add("connected to geometry cluster");
            }

            if (f.InCentralContentBand)
            {
                score.Score += 15;
                score.Reasons.Add("located in central content band");
            }

            if (f.NearSheetBorder)
            {
                score.Score -= 15;
                score.Reasons.Add("near border lowers geometry likelihood");
            }

            if (f.HasDocumentControlKeyword)
            {
                score.Score -= 30;
                score.Reasons.Add("document-control text is not geometry");
            }

            if (f.HasProjectionSymbolPattern)
            {
                score.Score -= 20;
                score.Reasons.Add("projection symbol should be separated");
            }

            return score;
        }

        private RoleScore ScoreDimensionRelated(SemanticComponent c, SheetAnalysisContext ctx)
        {
            var f = c.Features;
            var score = new RoleScore { Role = ComponentRole.DimensionRelated };

            if (f.DimensionCount > 0)
            {
                score.Score += 35;
                score.Reasons.Add("contains dimension entities");
            }

            if (f.ConnectedToGeometryCluster)
            {
                score.Score += 20;
                score.Reasons.Add("dimension group connected to geometry");
            }

            if (f.TextCount > 0 && f.HasNumericOnlyText)
            {
                score.Score += 10;
                score.Reasons.Add("numeric text supports dimension-like role");
            }

            return score;
        }

        private RoleScore ScoreTitleBlock(SemanticComponent c, SheetAnalysisContext ctx)
        {
            var f = c.Features;
            var score = new RoleScore { Role = ComponentRole.TitleBlock };

            if (f.NearTitleBlockArea)
            {
                score.Score += 25;
                score.Reasons.Add("near title block area");
            }

            if (f.HasTitleKeyword)
            {
                score.Score += 25;
                score.Reasons.Add("title-related keyword found");
            }

            if (f.HasScaleKeyword || f.HasMaterialKeyword || f.HasQuantityKeyword)
            {
                score.Score += 15;
                score.Reasons.Add("meta fields around title block");
            }

            return score;
        }

        private RoleScore ScoreMetaField(SemanticComponent c, SheetAnalysisContext ctx)
        {
            var f = c.Features;
            var score = new RoleScore { Role = ComponentRole.MetaField };

            if (f.HasScaleKeyword)
            {
                score.Score += 25;
                score.Reasons.Add("scale keyword found");
            }

            if (f.HasMaterialKeyword)
            {
                score.Score += 25;
                score.Reasons.Add("material keyword found");
            }

            if (f.HasQuantityKeyword)
            {
                score.Score += 25;
                score.Reasons.Add("quantity keyword found");
            }

            if (f.NearTitleBlockArea)
            {
                score.Score += 15;
                score.Reasons.Add("meta field likely near title block");
            }

            return score;
        }

        private RoleScore ScoreMarginAnnotation(SemanticComponent c, SheetAnalysisContext ctx)
        {
            var f = c.Features;
            var score = new RoleScore { Role = ComponentRole.MarginAnnotation };

            if (f.NearSheetBorder)
            {
                score.Score += 20;
                score.Reasons.Add("near sheet border");
            }

            if (f.HasVerticalText)
            {
                score.Score += 20;
                score.Reasons.Add("vertical text pattern");
            }

            if (f.HasDocumentControlKeyword)
            {
                score.Score += 30;
                score.Reasons.Add("document-control keyword found");
            }

            if (f.ConnectedToGeometryCluster)
            {
                score.Score -= 20;
                score.Reasons.Add("connected geometry lowers margin annotation likelihood");
            }

            return score;
        }

        private RoleScore ScoreAuxiliaryMarker(SemanticComponent c, SheetAnalysisContext ctx)
        {
            var f = c.Features;
            var score = new RoleScore { Role = ComponentRole.AuxiliaryMarker };

            if (f.HasEllipseLikeMarkerPattern)
            {
                score.Score += 35;
                score.Reasons.Add("ellipse-like marker pattern");
            }

            if (f.HasNumericOnlyText)
            {
                score.Score += 10;
                score.Reasons.Add("numeric-only text inside marker");
            }

            if (f.IsIsolatedSmallMarker)
            {
                score.Score += 20;
                score.Reasons.Add("isolated small marker");
            }

            if (f.ConnectedToGeometryCluster)
            {
                score.Score -= 20;
                score.Reasons.Add("connected geometry lowers marker likelihood");
            }

            return score;
        }

        private RoleScore ScoreProjectionMethodSymbol(SemanticComponent c, SheetAnalysisContext ctx)
        {
            var f = c.Features;
            var score = new RoleScore { Role = ComponentRole.ProjectionMethodSymbol };

            if (f.HasConcentricCirclePattern)
            {
                score.Score += 20;
                score.Reasons.Add("concentric circles detected");
            }

            if (f.HasTrapezoidLikePattern)
            {
                score.Score += 20;
                score.Reasons.Add("trapezoid-like side profile detected");
            }

            if (f.CenterLineLikeCount > 0)
            {
                score.Score += 10;
                score.Reasons.Add("centerline-like entities detected");
            }

            if (f.HasProjectionSymbolPattern)
            {
                score.Score += 30;
                score.Reasons.Add("projection-method symbol pattern matched");
            }

            return score;
        }
    }
}