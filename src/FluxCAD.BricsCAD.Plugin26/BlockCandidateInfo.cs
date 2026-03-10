using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class BlockCandidateInfo
    {
        public ObjectId Id { get; set; }
        public string Handle { get; set; } = "";
        public Extents3d Bounds { get; set; }

        public List<string> Texts { get; } = new();
        public List<RelativePoint> TextPositions { get; } = new();

        public int TextCount { get; set; }

        public int KeywordScore { get; set; }
        public int TextDensityScore { get; set; }
        public int MetaRegionScore { get; set; }
        public int RepeatedTextScore { get; set; }
        public int RepeatedLayoutScore { get; set; }
        public int SizeScore { get; set; }

        public int FinalScore { get; set; }
        public CandidateGrade Grade { get; set; }
    }
}
