using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Results;

namespace FluxCAD.SheetAnalysis.Structure.Classifiers
{
    public sealed class StructuralSeparator
    {
        // 호출부가 SheetStructuralModel 전체를 넘길 때 사용하는 오버로드
        public StructuralSeparationResult Separate(SheetStructuralModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            // SheetStructuralModel의 실제 컬렉션 이름에 맞춰 바꾸세요.
            return Separate(model.Units);
        }


        public StructuralSeparationResult Separate(IEnumerable<StructuralUnit> units)
        {
            var result = new StructuralSeparationResult();

            foreach (var unit in units)
            {
                switch (unit.RoleHint)
                {
                    case StructuralRoleHint.GeometryCarrier:
                        result.GeometryUnits.Add(unit);
                        break;

                    case StructuralRoleHint.AnnotationCarrier:
                        result.AnnotationUnits.Add(unit);
                        break;

                    case StructuralRoleHint.TableCarrier:
                        result.TableUnits.Add(unit);
                        break;

                    case StructuralRoleHint.FrameCarrier:
                        result.FrameUnits.Add(unit);
                        break;

                    case StructuralRoleHint.MetadataCarrier:
                    case StructuralRoleHint.TitleBlockCarrier:
                        result.MetadataUnits.Add(unit);
                        break;

                    default:
                        result.MixedUnits.Add(unit);
                        break;
                }
            }

            return result;
        }
    }
}