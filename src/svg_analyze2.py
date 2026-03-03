import os
import numpy as np
import json
from svgelements import SVG, Path, Rect, Polyline, Line, Circle, Ellipse, Polygon


def analyze_svg_clusters_fast(svg_path, grid_size=1000):
    print(f"[*] SVG 분석 시작: {svg_path}")

    if not os.path.exists(svg_path):
        print(f"[Error] 파일을 찾을 수 없습니다: {svg_path}")
        return 0

    # 1. SVG 파싱 및 중심점 추출
    points = []
    svg = SVG.parse(svg_path)

    print("[*] 벡터 요소 추출 중 (10만 개 이상 예상)...")
    for el in svg.elements():
        if isinstance(el, (Path, Rect, Polyline, Line, Circle, Ellipse, Polygon)):
            bbox = el.bbox()
            if bbox:
                cx, cy = (bbox[0] + bbox[2]) / 2, (bbox[1] + bbox[3]) / 2
                if abs(cx) < 1e12: points.append([cx, cy])

    X = np.array(points)
    print(f"[*] 추출 완료: {len(X)}개 객체")

    # 2. 메모리 효율적인 그리드 군집화
    # 모든 점을 계산하는 대신, 그리드 칸에 객체 존재 여부만 표시
    print(f"[*] 그리드 기반 군집화 실행 중 (Grid Size: {grid_size})...")

    # 좌표 정규화 (최솟값을 0으로)
    min_x, min_y = X.min(axis=0)
    grid_coords = ((X - [min_x, min_y]) // grid_size).astype(int)

    # 중복된 그리드 제거 (메모리 절약의 핵심)
    unique_grids = np.unique(grid_coords, axis=0)
    print(f"[*] 유효 그리드 칸 수: {len(unique_grids)}개")

    # 3. 인접한 그리드끼리 그룹화 (Scipy의 Labeling 사용)
    from scipy.sparse import dok_matrix
    from scipy.sparse.csgraph import connected_components

    # 그리드 간의 연결망 구축
    # 간단하게 구현하기 위해 각 그리드를 노드로 보고 거리가 1(인접)인 것들을 연결
    from sklearn.neighbors import NearestNeighbors
    nn = NearestNeighbors(radius=1.5).fit(unique_grids)
    adj_matrix = nn.radius_neighbors_graph(unique_grids)

    # 연결된 덩어리(부품) 찾기
    n_clusters, labels = connected_components(adj_matrix, directed=False)

    print(f"\n[결과 리포트]")
    print(f"------------------------------------------")
    print(f"식별된 독립 부품(도면 블록) 개수: {n_clusters}개")
    print(f"평균 부품 밀도: {len(X) / n_clusters:.1f} 객체/블록")
    print(f"------------------------------------------")

    # 4. 캡처 플랜 저장용 (각 클러스터의 실제 좌표 범위 계산)
    cluster_plan = []
    for i in range(n_clusters):
        cluster_points_idx = np.where(labels == i)[0]
        cluster_grids = unique_grids[cluster_points_idx]

        # 그리드 좌표를 다시 원래 좌표로 복원하여 범위 산출
        c_min = cluster_grids.min(axis=0) * grid_size + [min_x, min_y]
        c_max = (cluster_grids.max(axis=0) + 1) * grid_size + [min_x, min_y]

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

    # grid_size는 부품 하나가 차지할 법한 최소 크기로 설정하세요 (예: 500~2000)
    analyze_svg_clusters_fast(path, grid_size=1500)