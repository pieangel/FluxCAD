import json
import os
import numpy as np
from sklearn.cluster import DBSCAN  # pip install scikit-learn 필수


def analyze_drawing_clusters(json_path):
    print(f"[*] 분석 시작: {json_path}")

    with open(json_path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    # 1. 모든 객체의 중심점(X, Y) 수집
    points = []

    def collect_points(node):
        b = node.get('Bounds')
        if b and node.get('Id') != 'ROOT':
            min_p, max_p = b['MinPoint'], b['MaxPoint']
            if abs(min_p['X']) < 1e12:  # 유효 좌표 확인
                cx = (min_p['X'] + max_p['X']) / 2
                cy = (min_p['Y'] + max_p['Y']) / 2
                points.append([cx, cy])

        for child in node.get('Children', []):
            collect_points(child)

    collect_points(data)
    X = np.array(points)
    print(f"[*] 총 객체 수: {len(X)}개")

    # 2. 군집화 알고리즘 (DBSCAN) 실행
    # eps: 객체 간의 거리(단위). 이 거리 안에 있으면 하나의 '부품'으로 인식
    # 도면 스케일에 따라 500~2000 사이로 조절하세요.
    eps_value = 1500
    db = DBSCAN(eps=eps_value, min_samples=5).fit(X)

    labels = db.labels_
    n_clusters = len(set(labels)) - (1 if -1 in labels else 0)

    print(f"\n[결과 리포트]")
    print(f"------------------------------------------")
    print(f"식별된 독립 부품(도면 블록) 개수: {n_clusters}개")
    print(f"소속되지 않은 노이즈 객체: {list(labels).count(-1)}개")
    print(f"------------------------------------------")

    return n_clusters


if __name__ == "__main__":
    # 파일 경로를 본인의 환경에 맞게 수정하세요.
    path = "../data/spatial_tree.json"
    analyze_drawing_clusters(path)