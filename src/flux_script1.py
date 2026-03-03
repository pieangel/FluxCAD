import json
import os
import numpy as np
from sklearn.cluster import DBSCAN  # pip install scikit-learn


def main():
    base_path = os.path.dirname(os.path.abspath(__file__))
    json_path = os.path.join(base_path, '../data/spatial_tree.json')
    output_path = os.path.join(base_path, '../data/cluster_plan.json')

    with open(json_path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    # 1. 모든 객체의 중심점과 ID 수집
    points = []
    entities = []

    def collect_entities(node):
        b = node.get('Bounds')
        if b and node.get('Id') != 'ROOT':
            min_p, max_p = b['MinPoint'], b['MaxPoint']
            if abs(min_p['X']) < 1e12:
                # 중심점 계산
                cx = (min_p['X'] + max_p['X']) / 2
                cy = (min_p['Y'] + max_p['Y']) / 2
                points.append([cx, cy])
                entities.append({
                    'id': node['Id'],
                    'min': [min_p['X'], min_p['Y']],
                    'max': [max_p['X'], max_p['Y']]
                })
        for child in node.get('Children', []):
            collect_entities(child)

    collect_entities(data)
    X = np.array(points)

    # 2. DBSCAN 군집화 (객체 간 거리가 300단위 이내면 하나의 블록으로 묶음)
    # eps 값(300)을 조절하여 부품을 얼마나 조밀하게 묶을지 결정하세요.
    clustering = DBSCAN(eps=300, min_samples=1).fit(X)
    labels = clustering.labels_

    # 3. 군집별 Bounding Box 계산 (이것이 하나의 부품 도면이 됩니다)
    clusters = {}
    for idx, label in enumerate(labels):
        if label not in clusters:
            clusters[label] = {"ids": [], "min_x": float('inf'), "min_y": float('inf'), "max_x": float('-inf'),
                               "max_y": float('-inf')}

        ent = entities[idx]
        clusters[label]["ids"].append(ent['id'])
        clusters[label]["min_x"] = min(clusters[label]["min_x"], ent['min'][0])
        clusters[label]["min_y"] = min(clusters[label]["min_y"], ent['min'][1])
        clusters[label]["max_x"] = max(clusters[label]["max_x"], ent['max'][0])
        clusters[label]["max_y"] = max(clusters[label]["max_y"], ent['max'][1])

    # 4. 결과 저장 (BricsCAD가 읽을 최종 캡처 플랜)
    cluster_plan = []
    for label, c in clusters.items():
        # 너무 작은 군집(노이즈)이나 너무 큰 군집은 제외 가능
        width = c["max_x"] - c["min_x"]
        height = c["max_y"] - c["min_y"]

        cluster_plan.append({
            "id": f"Cluster_{label}",
            "min": [c["min_x"], c["min_y"]],
            "max": [c["max_x"], c["max_y"]],
            "object_count": len(c["ids"])
        })

    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(cluster_plan, f, indent=2)

    print(f"[*] 분석 완료: 총 {len(cluster_plan)}개의 부품 블록을 식별했습니다.")
    print(f"[*] 결과 저장: {output_path}")


if __name__ == "__main__":
    main()