import os
import numpy as np
import json
from svgelements import SVG, Path, Rect, Polyline, Line, Circle, Ellipse, Polygon
from sklearn.cluster import DBSCAN  # pip install scikit-learn svgelements 필수


def analyze_svg_clusters(svg_path, eps_value=1500, min_samples=5):
    print(f"[*] SVG 분석 시작: {svg_path}")

    if not os.path.exists(svg_path):
        print(f"[Error] 파일을 찾을 수 없습니다: {svg_path}")
        return 0

    # 1. SVG 파싱 및 모든 벡터 요소의 중심점 수집
    points = []
    svg = SVG.parse(svg_path)

    print("[*] 벡터 요소 추출 중...")
    for el in svg.elements():
        # 실제 형상이 있는 요소들만 필터링
        if isinstance(el, (Path, Rect, Polyline, Line, Circle, Ellipse, Polygon)):
            bbox = el.bbox()  # (xmin, ymin, xmax, ymax)
            if bbox:
                # 요소의 중심점 계산
                cx = (bbox[0] + bbox[2]) / 2
                cy = (bbox[1] + bbox[3]) / 2

                # 비정상적으로 큰 좌표나 무한대 값 제외
                if abs(cx) < 1e12 and abs(cy) < 1e12:
                    points.append([cx, cy])

    if not points:
        print("[!] 유효한 벡터 객체를 찾을 수 없습니다.")
        return 0

    X = np.array(points)
    print(f"[*] 추출된 총 벡터 요소 수: {len(X)}개")

    # 2. 군집화 알고리즘 (DBSCAN) 실행
    # eps: 요소 간의 거리. 도면 내 부품 간 간격을 고려하여 조절 (SVG 단위 기준)
    print(f"[*] 군집화 실행 중 (eps={eps_value})...")
    db = DBSCAN(eps=eps_value, min_samples=min_samples).fit(X)

    labels = db.labels_
    # -1은 어느 군집에도 속하지 않는 노이즈
    unique_labels = set(labels)
    n_clusters = len(unique_labels) - (1 if -1 in labels else 0)

    # 3. 결과 리포트 및 캡처 플랜용 데이터 정리 (선택 사항)
    print(f"\n[결과 리포트]")
    print(f"------------------------------------------")
    print(f"식별된 부품(도면 블록) 개수: {n_clusters}개")
    print(f"노이즈(고립된 선/점) 개수: {list(labels).count(-1)}개")
    print(f"------------------------------------------")

    # 각 군집의 경계(Bounding Box)를 계산하여 저장하고 싶다면 아래 로직을 추가할 수 있습니다.
    return n_clusters


if __name__ == "__main__":
    # 1. 파일 경로 설정
    base_path = os.path.dirname(os.path.abspath(__file__))
    # 경로를 본인의 환경에 맞게 수정하세요 (예: ../data/drawing.svg)
    path = os.path.join(base_path, '../data/drawing.svg')

    # 2. 분석 실행
    # 도면의 밀집도에 따라 eps_value를 조절해 보세요.
    # SVG 단위가 CAD와 다를 수 있으므로 처음엔 500~1000 정도로 시작하는 것을 추천합니다.
    analyze_svg_clusters(path, eps_value=800, min_samples=5)