import os
import re
import numpy as np
import json
from scipy.sparse.csgraph import connected_components
from sklearn.neighbors import NearestNeighbors


def fast_svg_scan(svg_path):
    print(f"[*] 초고속 스캔 시작: {svg_path}")
    points = []

    # 정규표현식으로 숫자 쌍(좌표)만 초고속으로 추출
    # SVG 내의 'd="M x y ..."' 또는 'points="x,y ..."' 패턴을 찾습니다.
    coord_pattern = re.compile(r'([+-]?\d*\.\d+|[+-]?\d+)[,\s]([+-]?\d*\.\d+|[+-]?\d+)')

    with open(svg_path, 'r', encoding='utf-8') as f:
        for line in f:
            # 한 줄씩 읽으며 좌표 패턴 매칭 (메모리 절약 및 속도 향상)
            matches = coord_pattern.findall(line)
            for m in matches:
                points.append([float(m[0]), float(m[1])])

    X = np.array(points)
    if len(X) == 0: return None, None

    # 중복 좌표 제거 (속도 향상을 위한 핵심 단계)
    X = np.unique(X, axis=0)
    print(f"[*] 유효 좌표 추출 완료: {len(X)}개")
    return X, X.min(axis=0), X.max(axis=0)


def analyze_svg_clusters_ultra_fast(svg_path):
    # 1. 초고속 좌표 스캔
    X, min_pt, max_pt = fast_svg_scan(svg_path)
    if X is None: return

    # 2. 자동 스케일링 및 그리드 변환
    range_ptr = max_pt - min_pt
    # 해상도를 200단계로 조절 (너무 세밀하면 느리고, 너무 뭉치면 1개가 됨)
    auto_grid_size = max(range_ptr.max() / 200, 0.0001)

    grid_coords = ((X - min_pt) // auto_grid_size).astype(int)
    unique_grids = np.unique(grid_coords, axis=0)
    print(f"[*] 분석 구역 압축 완료: {len(unique_grids)}개 칸")

    # 3. 인접 구역 연결 분석 (부품 덩어리 찾기)
    nn = NearestNeighbors(radius=1.5, n_jobs=-1).fit(unique_grids)
    adj_matrix = nn.radius_neighbors_graph(unique_grids)
    n_clusters, labels = connected_components(adj_matrix, directed=False)

    print(f"\n[분석 결과]")
    print(f"------------------------------------------")
    print(f"식별된 독립 부품 수: {n_clusters}개")
    print(f"------------------------------------------")

    # 4. 캡처 플랜 저장
    cluster_plan = []
    for i in range(n_clusters):
        c_idx = np.where(labels == i)[0]
        c_grids = unique_grids[c_idx]
        c_min = c_grids.min(axis=0) * auto_grid_size + min_pt
        c_max = (c_grids.max(axis=0) + 1) * auto_grid_size + min_pt

        cluster_plan.append({
            "id": f"Part_{i}",
            "min": c_min.tolist(),
            "max": c_max.tolist()
        })

    with open("svg_cluster_plan.json", "w") as f:
        json.dump(cluster_plan, f, indent=2)
    print("[+] 캡처 플랜 저장 완료.")


if __name__ == "__main__":
    path = "../data/drawing.svg"  # 경로 확인
    analyze_svg_clusters_ultra_fast(path)