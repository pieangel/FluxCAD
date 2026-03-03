import cv2
import numpy as np
import os


def identify_part_count(image_path):
    # 1. 이미지 로드
    img = cv2.imread(image_path)
    if img is None:
        print("이미지를 찾을 수 없습니다.")
        return

    # 2. 전처리: 그레이스케일 및 이진화
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    # 배경이 흰색이면 반전(THRESH_BINARY_INV)
    _, thresh = cv2.threshold(gray, 240, 255, cv2.THRESH_BINARY_INV)

    # 3. 팽창(Dilation) 연산: 부품 내의 선들을 하나의 '섬'으로 뭉침
    # 이 커널 크기가 중요합니다. 부품 내 간격보다 크고 부품 간 거리보다 작아야 합니다.
    kernel = np.ones((15, 15), np.uint8)
    dilated = cv2.dilate(thresh, kernel, iterations=2)

    # 4. 윤곽선(Contour) 검출
    contours, _ = cv2.findContours(dilated, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    # 5. 유효 부품 필터링 및 시각화
    count = 0
    output_img = img.copy()

    for cnt in contours:
        x, y, w, h = cv2.boundingRect(cnt)

        # 너무 작은 점(노이즈)이나 너무 큰 배경 영역 제외
        area = w * h
        if area > 500 and area < (img.shape[0] * img.shape[1] * 0.8):
            count += 1
            # 찾은 부품에 사각형 그리기
            cv2.rectangle(output_img, (x, y), (x + w, y + h), (0, 255, 0), 3)
            # 번호 매기기
            cv2.putText(output_img, str(count), (x, y - 10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2)

    # 6. 결과 출력 및 저장
    print(f"---------------------------------------")
    print(f"[*] 분석 결과: 총 {count}개의 부품 도면 식별")
    print(f"---------------------------------------")

    result_path = "Analyzed_Full_Drawing.png"
    cv2.imwrite(result_path, output_img)
    print(f"[+] 분석 이미지 저장 완료: {result_path}")


# 실행
identify_part_count('../data/Full_Drawing_HD.png')