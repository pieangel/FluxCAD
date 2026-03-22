using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public enum RegionKind
    {
        Unknown = 0,

        Geometry,       // 형상 중심
        DimensionNote,  // 치수/주석 중심
        MetaTable,      // 표/메타 정보 중심
        TitleBlock,     // 타이틀/도면번호/기본정보 영역
        Preview,        // 축소 미리보기
        Noise           // 잡음/불필요
    }
}
