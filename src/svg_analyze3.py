import os
import numpy as np
import json
from svgelements import SVG, Path, Rect, Polyline, Line, Circle, Ellipse, Polygon
from scipy.sparse.csgraph import connected_components
from sklearn.neighbors import NearestNeighbors


def analyze_svg_clusters_smart(svg_path):
    print(f"[*] SVG 분석 시작: {svg_path}")

    if not os.path.exists(svg_path):
        print(f"[Error] 파일을 찾을 수 없습니다: {svg_path}")
        return 0

    # 1. SVG 파싱 및 모든 중심점 추출
    points = []
    svg = SVG.parse(svg_path)

    for el in svg.elements():
        if isinstance(el, (Path, Rect, Polyline, Line, Circle, Ellipse, Polygon)):
            bbox = el.bbox()
            if bbox:
                cx, cy = (bbox[0] + bbox[2]) / 2, (bbox[1] + bbox[3]) / 2
                if abs(cx) < 1e12: points.append([cx, cy])

    X = np.array(points)
    if len(X) == 0: return 0
    print(f"[*] 추출 완료: {len(X)}개 객체")

    # 2. 자동 그리드 크기 계산 (Auto-Scaling)
    # 도면의 전체 너비/높이 중 짧은 쪽의 1/100 정도를 기본 해상도로 잡습니다.
    min_pt = X.min(axis=0)
    max_pt = X.max(axis=0)
    range_ptr = max_pt - min_pt

    # 너무 크면 1개로 뭉치고, 너무 작으면 메모리가 터지므로 적절한 해상도(Resolution) 설정
    # 도면 전체를 가로세로 약 200~500개 칸으로 나눕니다.
    auto_grid_size = max(range_ptr.max() / 300, 0.0001)
    print(f"[*] 자동 계산된 Grid Size: {auto_grid_size:.4f}")

    # 3. 그리드 좌표 변환 및 유니크화
    grid_coords = ((X - min_pt) // auto_grid_size).astype(int)
    unique_grids = np.unique(grid_coords, axis=0)
    print(f"[*] 유효 그리드 칸 수: {len(unique_grids)}개 (이제 1개 이상일 것입니다)")

    # 4. 인접 그리드 연결 (Nearest Neighbors)
    # 반지름 1.5는 상하좌우 및 대각선까지 인접한 칸을 묶겠다는 뜻입니다.
    print("[*] 부품 군집 식별 중...")
    nn = NearestNeighbors(radius=1.5).fit(unique_grids)
    adj_matrix = nn.radius_neighbors_graph(unique_grids)

    # 5. 연결된 덩어리(부품) 개수 파악
    n_clusters, labels = connected_components(adj_matrix, directed=False)

    print(f"\n[결과 리포트]")
    print(f"------------------------------------------")
    print(f"식별된 부품(도면 블록) 개수: {n_clusters}개")
    print(f"도면 전체 범위: {range_ptr[0]:.2f} x {range_ptr[1]:.2f}")
    print(f"------------------------------------------")

    # 6. 결과 저장 (BricsCAD에서 줌을 당길 좌표들)
    cluster_plan = []
    for i in range(n_clusters):
        c_idx = np.where(labels == i)[0]
        c_grids = unique_grids[c_idx]

        # 다시 원래 도면 좌표로 복원
        c_min = c_grids.min(axis=0) * auto_grid_size + min_pt
        c_max = (c_grids.max(axis=0) + 1) * auto_grid_size + min_pt

        cluster_plan.append({
            "id": f"Part_{i}",
            "min": c_min.tolist(),
            "max": c_max.tolist()
        })

    with open("svg_cluster_plan.json", "w") as f:
        json.dump(cluster_plan, f, indent=2)

    return n_clusters


if __name__ == "__main__":
    base_path = os.path.dirname(os.path.abspath(__file__))
    path = os.path.join(base_path, '../data/drawing.svg')
    analyze_svg_clusters_smart(path)