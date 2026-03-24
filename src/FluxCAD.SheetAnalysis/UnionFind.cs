using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public class UnionFind
    {
        private readonly int[] _p;
        private readonly int[] _r;

        public UnionFind(int n)
        {
            _p = new int[n];
            _r = new int[n];
            for (int i = 0; i < n; i++) _p[i] = i;
        }

        public int Find(int x)
        {
            while (_p[x] != x)
            {
                _p[x] = _p[_p[x]];
                x = _p[x];
            }
            return x;
        }

        public void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return;
            if (_r[ra] < _r[rb]) _p[ra] = rb;
            else if (_r[ra] > _r[rb]) _p[rb] = ra;
            else { _p[rb] = ra; _r[ra]++; }
        }
    }

    public sealed class UnionFind_New
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public UnionFind_New(int size)
        {
            _parent = new int[size];
            _rank = new int[size];

            for (int i = 0; i < size; i++)
            {
                _parent[i] = i;
                _rank[i] = 0;
            }
        }

        public int Find(int x)
        {
            if (_parent[x] != x)
                _parent[x] = Find(_parent[x]);

            return _parent[x];
        }

        public void Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);

            if (ra == rb)
                return;

            if (_rank[ra] < _rank[rb])
            {
                _parent[ra] = rb;
            }
            else if (_rank[ra] > _rank[rb])
            {
                _parent[rb] = ra;
            }
            else
            {
                _parent[rb] = ra;
                _rank[ra]++;
            }
        }
    }
}
