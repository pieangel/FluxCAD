using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using FluxCAD.Contracts.Summary;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class SpatialHierarchyEngine
    {
        public SpatialNode BuildTree2(List<SpatialNode> allNodes)
        {
            // 1. 면적이 큰 순서대로 정렬 (큰 놈이 부모가 될 확률이 높음)
            var sortedNodes = allNodes.OrderByDescending(n => GetArea(n.Bounds)).ToList();

            SpatialNode root = new SpatialNode { Id = "ROOT", Type = "ROOT" };

            foreach (var node in sortedNodes)
            {
                InsertNode(root, node);
            }
            return root;
        }


        public SpatialNode BuildTree(List<SpatialNode> allNodes)
        {
            // 1. 면적이 큰 순서대로 정렬 (큰 놈이 부모가 될 확률이 높음)
            var sortedNodes = allNodes.OrderByDescending(n => GetArea(n.Bounds)).ToList();

            SpatialNode root = new SpatialNode { Id = "ROOT", Type = "ROOT" };

            foreach (var node in sortedNodes)
            {
                InsertNode(root, node);
            }
            return root;
        }

        private void InsertNode(SpatialNode parent, SpatialNode newNode)
        {
            // 자식들 중에 newNode를 포함하는 놈이 있는지 확인
            foreach (var child in parent.Children)
            {
                if (child.Encloses(newNode))
                {
                    InsertNode(child, newNode);
                    return;
                }
            }
            // 포함하는 자식이 없으면 현재 부모의 직속 자식으로 등록
            parent.Children.Add(newNode);
        }

        private double GetArea(Extents3d ex) =>
            (ex.MaxPoint.X - ex.MinPoint.X) * (ex.MaxPoint.Y - ex.MinPoint.Y);
    }
}
