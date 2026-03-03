import json
import os


def get_leaf_bounds(node, bounds_list):
    """재귀적으로 트리를 탐색하여 실제 객체의 좌표 경계만 수집합니다."""
    children = node.get('Children', [])
    if not children:
        b = node.get('Bounds')
        if b:
            min_p = b.get('MinPoint')
            max_p = b.get('MaxPoint')
            # 좌표가 존재하고 유효한 데이터인지 확인
            if min_p and max_p and abs(min_p['X']) < 1e12:
                bounds_list.append({
                    'min_x': min_p['X'], 'min_y': min_p['Y'],
                    'max_x': max_p['X'], 'max_y': max_p['Y']
                })
    else:
        for child in children:
            get_leaf_bounds(child, bounds_list)


def main():
    # 1. 경로 설정
    base_path = os.path.dirname(os.path.abspath(__file__))
    json_path = os.path.join(base_path, '../data/spatial_tree.json')
    output_path = os.path.join(base_path, '../data/capture_plan.json')

    if not os.path.exists(json_path):
        print(f"[Error] 파일을 찾을 수 없습니다: {json_path}")
        return

    print(f"[*] 고해상도 분석 시작: {json_path}")
    with open(json_path, 'r', encoding='utf-8') as f:
        root_data = json.load(f)

    # 2. 객체 좌표 추출
    all_bounds = []
    get_leaf_bounds(root_data, all_bounds)

    if not all_bounds:
        print("[!] 유효한 객체 정보를 찾을 수 없습니다.")
        return

    # 3. 도면 범위 산출
    min_x = min(b['min_x'] for b in all_bounds)
    min_y = min(b['min_y'] for b in all_bounds)
    max_x = max(b['max_x'] for b in all_bounds)
    max_y = max(b['max_y'] for b in all_bounds)

    # 4. 해상도 강화 설정 (Tile Size를 1000으로 줄임)
    # 글자가 더 선명해야 한다면 이 값을 800까지 낮추셔도 됩니다.
    tile_size = 1000
    overlap = tile_size * 0.2  # 20% 중첩 (200단위)
    step_size = tile_size - overlap

    occupancy = set()
    for b in all_bounds:
        c1 = int((b['min_x'] - min_x) // step_size)
        r1 = int((b['min_y'] - min_y) // step_size)
        c2 = int((b['max_x'] - min_x) // step_size)
        r2 = int((b['max_y'] - min_y) // step_size)

        for r in range(r1, r2 + 1):
            for c in range(c1, c2 + 1):
                occupancy.add((r, c))

    # 5. 활성 구역 리스트 생성
    capture_plan = []
    for r, c in sorted(list(occupancy)):
        z_min_x = min_x + c * step_size
        z_min_y = min_y + r * step_size
        z_max_x = z_min_x + tile_size
        z_max_y = z_min_y + tile_size

        capture_plan.append({
            "id": f"R{r}_C{c}",
            "min": [z_min_x, z_min_y],
            "max": [z_max_x, z_max_y]
        })

    # 6. 결과 저장
    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(capture_plan, f, indent=2)

    print(f"[+] 고해상도 분석 완료! 유효 구역: {len(capture_plan)}개")
    print(f"[+] 결과 저장: {output_path}")


if __name__ == "__main__":
    main()