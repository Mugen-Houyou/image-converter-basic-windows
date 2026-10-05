# 이미지 "압축 잘 될 이미지" 예측: 선행 연구·업계 사례·WebP 서비스 적용 방안

결론부터 말하면, 네, 이 문제는 오래전부터 연구되어 왔고 업계에서도 실제로 쓰고 있습니다. 다만 하나의 이름으로 불리지는 않고, 학계에서는 "file size prediction", "rate estimation / rate modeling(R-Q, ρ-domain, R-λ)", "image/visual complexity", "JND / satisfied user ratio(SUR) 예측"이라는 이름으로, 업계에서는 "content-adaptive / per-title encoding", "perceptual quality targeting", "auto quality(q_auto)"라는 이름으로 흩어져 있습니다. 귀하의 상황(이미 해상도 기반 Q 모델이 있는 WebP 서비스)에서 가장 비용 대비 효과가 좋은 방법은 "순수 예측 모델"이 아니라 **기준 Q로 1회 시험 인코딩(trial encode) → 결과 bpp로 Q 보정 → 필요하면 1~2회 추가 탐색**하는 방식이고, 장기적으로는 파일 크기가 아니라 **지각 품질(SSIMULACRA2/Butteraugli/DSSIM) 목표**로 Q를 정하는 쪽이 정답에 가깝습니다.

## TL;DR

- **연구는 많습니다.** 다음 연구들이 모두 귀하의 질문과 같은 문제를 다룹니다.
  - JPEG 파일 크기를 품질 계수(QF)와 스케일로 예측하는 연구(Pigeon & Coulombe, 2008). 입출력 QF가 80, 스케일이 100%일 때 평균 절대 상대 오차 2.42%, 예측의 97%가 ±10% 이내였습니다.
  - 코덱 rate control의 ρ-domain 모델(He & Mitra, 2001~2002)
  - DCT 에너지 특징으로 QP별 비트를 예측하는 random forest(VVC all-intra rate control, 2023)
  - JPEG에서 "눈에 띄지 않는 최저 QF"를 예측하는 JND/SUR 연구(MCL-JCI, SUR-Net 등)
- **업계는 "예측"보다 "측정 + 탐색"을 씁니다.**
  - Etsy는 DSSIM 기반 이진 탐색으로 업로드마다 JPEG 품질을 정합니다. Code as Craft(2017)에 따르면 기본값 85였던 Q가 평균 "roughly 73"으로 내려갔고, 2016년 4월 대비 2017년 4월 저장 이미지 크기가 "25% to 30%" 줄었습니다.
  - Cloudinary q_auto는 지각 지표와 휴리스틱을 씁니다. 핵심 지표인 자체 SSIMULACRA는 Jon Sneyers의 2017년 블로그에서 사람 판단과 87% 일치했고, PSNR은 67%였습니다.
  - Akamai는 SSIM 기반 perceptual quality를 씁니다.
  - libwebp 자체에도 `-size`(목표 바이트), `-psnr`(목표 PSNR), `-pass`(탐색 횟수) 옵션이 있습니다.
- **권장안:**
  1. 현재 해상도 모델 Q로 빠른 시험 인코딩을 1회 합니다. 나온 bpp를 해상도 기준 기대값과 비교해 Q를 보정하고(로그-선형 R-Q 모델), 상·하한(예: 65~90)으로 클램프합니다.
  2. 여력이 있으면 목표를 "크기"가 아니라 "SSIMULACRA2 점수"로 바꿔 2~4회 이진/할선 탐색을 합니다.
  3. 데이터가 쌓이면 저렴한 특징(DCT 에너지, Sobel SI, 원본 JPEG 양자화 테이블·bpp 등)으로 초기 Q를 예측하는 경량 회귀 모델을 붙여 탐색 횟수를 줄입니다.
  4. 텍스트·그래픽·스크린샷은 별도로 감지해 lossless/near-lossless WebP를 시도해야 합니다.

## Key Findings

1. **귀하의 직관은 학계에서 이미 "압축률 = 복잡도"로 정식화되어 있습니다.** 시각적 복잡도(visual complexity) 연구에서는 JPEG 파일 크기나 압축률 자체를 복잡도 척도로 쓰는 것이 표준적인 방법입니다. 예로 Rosenholtz et al.(2007)의 compression ratio·subband entropy, Yu & Winkler(2013) "Image complexity and spatial information", Marin & Leder의 GIF/TIFF 압축률이 있습니다. 즉 "이 이미지는 크게 나올 것"이라는 판단은 "이 이미지는 복잡하다"와 거의 같은 측정량입니다.
2. **가장 직접적인 선행 연구는 "JPEG 트랜스코딩 시 파일 크기 예측"입니다.**
   - Steven Pigeon & Stéphane Coulombe의 두 논문이 있습니다: "Very Low Cost Algorithms for Predicting the File Size of JPEG Images Subject to Changes of Quality Factor and Scaling"(DCC 2008)과 "Computationally efficient algorithms for predicting the file size of JPEG images…"(Queen's Biennial Symposium 2008). 원본 파일 크기·원본 QF·해상도만으로 목표 QF·스케일에서의 크기를 예측합니다.
   - 입력·출력 QF가 모두 80이고 스케일 100%일 때 평균 절대 상대 오차는 2.42%, 예측의 97%가 ±10% 이내였습니다. 50% 스케일에서는 오차 8.97%(±10% 이내 66%), 10% 스케일에서는 54%까지 커졌습니다.
   - 후속작 Coulombe & Pigeon, "Low-complexity transcoding of JPEG images with near-optimal quality using a predictive quality factor and scaling parameters"(IEEE TIP, 2010)는 SSIM까지 함께 예측해 최적에 가까운 QF·스케일을 고릅니다.
3. **코덱 rate control 분야는 "콘텐츠 → 비트" 모델의 본진입니다.**
   - He & Mitra의 ρ-domain 모델은 비트레이트가 양자화 후 0이 아닌 계수 비율에 선형이라는 R = θ·(1−ρ) 관계를 씁니다. HEVC의 R-λ 모델과 Laplacian/Cauchy 분포 기반 DCT 계수 모델링이 뒤를 잇습니다.
   - 최근 "All-intra rate control using low complexity video features for Versatile Video Coding"(arXiv 2306.16786, 2023)은 DCT 에너지 특징 6개(휘도/색차 텍스처 에너지·평균 밝기)와 QP를 입력으로 random forest가 I-프레임 비트 수를 예측하게 했습니다. 이 논문은 흔히 쓰는 SI와 비트레이트의 상관이 "매우 낮다"고 명시합니다.
4. **"목표 지각 품질에 필요한 Q" 예측도 별도 분야로 존재합니다.**
   - MCL-JCI(Jin, Lin, Kuo 외, 2016)는 1920×1080 원본 50장과 QF 1~100으로 만든 JPEG 왜곡본 5,000장을 피험자 30명에게 보여 JND를 측정했습니다.
   - 대부분의 이미지에서 사람이 구분하는 왜곡 단계는 4~7개였고, 그 수는 이미지 내용에 따라 달랐습니다. 지각 품질은 비트레이트에 대해 계단 함수 모양이었습니다.
   - 이 데이터로 "몇 % 사용자가 만족하는 최저 QF"를 예측하는 모델들이 나왔습니다: SUR-Net(Siamese CNN, 예측·실측 JND 분포 간 평균 Bhattacharyya 거리 0.072), SUR-FeatNet(2020), SG-JND(2024), KonJND-1k 등.
5. **동영상 업계의 교훈은 "고정 비트레이트가 아니라 고정 품질"입니다.**
   - Netflix의 Per-Title Encode Optimization(2015)과 Dynamic Optimizer(Katsavounidis, 2018)는 콘텐츠별로 R-D convex hull을 구해 같은 품질을 더 적은 비트로 냅니다.\[1\]\[2\]
   - 이후 연구들은 "전수 인코딩 대신 특징으로 R-D 곡선을 예측"하는 방향입니다: Katsenou et al.(2016) "Predicting video rate-distortion curves using textural features", Wu/Kondratenko/Katsavounidis(2021) "Encoding Parameters Prediction for Convex Hull Video Encoding", Bovik 그룹의 convex hull 예측. 이 방향은 정지 이미지에도 그대로 옮겨옵니다.
6. **WebP의 Q→양자화기 매핑 자체가 R-Q 모델을 품고 있습니다.**
   - libwebp `quant_enc.c`의 주석은 "파일 크기가 대략 양자화기의 3제곱(실제 지수 2.8~3.2)에 비례한다"고 적고, 이를 상쇄하려고 quant ≈ compression^(1/3)로 매핑합니다.\[3\]
   - SNS(spatial noise shaping)는 세그먼트별 "susceptibility(alpha)"에 따라 "조밀한(복잡한) 세그먼트를 더 거칠게 양자화"합니다.
   - 즉 WebP는 이미 이미지 안에서는 콘텐츠 적응을 하고 있고, 귀하가 하려는 것은 그 바깥, 즉 이미지 단위의 적응입니다.

## Details

### 1. 학술 연구 계열

#### 1-1. 이미지 복잡도 지표와 압축 크기의 상관

| 지표 | 정의/출처 | 압축 크기 예측력에 대한 근거 |
|---|---|---|
| SI (Spatial Information) | ITU-T P.910: Sobel 필터 결과의 표준편차 | P.910(10/2023)은 Robitza 등의 결과를 인용해 "평균·중앙값 SI가 최소·최대 SI보다 압축성과 더 잘 상관된다"고 기술. 다만 VVC all-intra 논문은 SI와 비트레이트의 상관이 "매우 낮아" 파라미터 예측에는 부족하다고 평가\[4\]\[5\] |
| DCT 텍스처 에너지 | VCA(Video Complexity Analyzer; Menon, Feldmann, Amirpour, Ghanbari, Timmerer, ACM MMSys 2022, GPLv3 오픈소스)\[6\]\[7\] | 블록 DCT 고주파 가중 에너지. VVC intra 비트 예측, per-title 래더 예측 등에 SI보다 나은 특징으로 쓰임 |
| JPEG 파일 크기/압축률 | Rosenholtz 2007, Yu & Winkler 2013 | 지각 복잡도와 상관.\[8\] 다른 코덱의 lossy 크기와 강하게 연관되는 "가장 직접적인 프록시" |
| 엔트로피 | Shannon entropy, subband entropy | Yu & Winkler는 전역 엔트로피가 공간 구조를 반영하지 못해 복잡도 척도로 부족하다고 지적\[8\] |
| 학습 기반 복잡도 | DeepVCA(intra 인코딩 비트를 라벨로 학습), 딥 특징 기반 visual complexity | 인코딩 비트 자체를 라벨로 쓰므로 목적에 가장 가까움\[9\] |

주의할 점: CAT: Content-Adaptive Image Tokenization(arXiv 2501.03120, 2025)은 JPEG 크기·MSE·LPIPS 같은 지표가 고대비 반복 패턴(예: 얼룩말)은 복잡하다고 판단하면서 **텍스트가 많은 이미지의 지각적 어려움은 과소평가**한다고 보여줍니다.\[10\] 이는 "크기가 작게 나온다 = Q를 올려도/내려도 괜찮다"는 추론이 텍스트·선화에서는 틀릴 수 있다는 뜻입니다.

#### 1-2. Rate modeling / rate estimation

- **ρ-domain 모델(He & Mitra, ICASSP 2001; IEEE TCSVT 2002 "Low-delay rate control for DCT video coding via ρ-domain source modeling"):**
  - 양자화 후 0 계수 비율 ρ와 비트 R이 R = θ(1−ρ)의 선형 관계이며, θ는 콘텐츠 의존 상수입니다.\[11\]\[12\]
  - 정지 이미지에 적용하면 **"DCT 한 번 + 양자화 시뮬레이션"만으로 엔트로피 코딩 없이 크기를 추정**할 수 있습니다. WebP(VP8)의 4×4 DCT에도 원리상 적용 가능합니다.
- **R-Q 모델과 R-λ 모델:** HEVC HM의 λ-domain rate control은 QP = 4.2005·ln λ + 13.7122 관계를 씁니다. 기존 R-Q 모델 대비 저지연 코딩에서 평균 0.55dB, 최대 1.81dB 개선이 보고되었습니다(특허 문헌의 요약 인용).
- **콘텐츠 특징 → QP별 비트 회귀:** 위 VVC all-intra 논문은 DCT 에너지 특징 + QP → random forest로 비트 수를 예측해 2-pass rate control을 대체합니다.\[5\] "QP를 입력 변수로 넣고 크기를 예측하는 회귀"라는 구조는 귀하의 문제(Q를 입력으로 bpp 예측, 또는 그 역함수)와 정확히 같습니다.
- **특허 사례:** "Method and apparatus for controlling a compression rate for a file"(US 8150176)은 "복잡한 이미지"와 "단순한 이미지"의 압축률-크기 상관 곡선 두 개를 둡니다. 한 번 압축한 결과 크기로 그 사이를 보간해 목표 크기에 맞는 압축률을 계산합니다. 귀하가 상상한 "시험 인코딩 1회 후 보정" 방식의 고전적 형태입니다.

#### 1-3. ML/DL로 크기·품질·Q를 예측하는 연구

- **Pigeon & Coulombe(2008)의 예측 테이블:**
  - 크롤링한 JPEG 대규모 코퍼스로 학습했습니다(IEEE 초록은 10만 장, 본문 실험 설명은 약 7만 장으로 버전 간 차이가 있음).\[13\]\[14\]
  - 입력 QF 80 기준 평균 크기 비율은 q10→0.20, q50→0.54, q80→0.95, q90→1.12, q100→2.22였습니다. q80에서 50% 축소 시 비율은 면적비 0.25가 아닌 0.31이었습니다.
  - 해상도 클러스터링으로 최악 오차를 112.9%에서 24.8%로 줄였습니다.
  - **함의:** 축소본으로 원본 크기를 외삽하면 축소 비율 50% 이하에서 오차가 급증합니다. 따라서 귀하의 서비스에서 "썸네일로 시험 인코딩해 원본 크기 추정"은 조심해야 합니다.
- **K-means 기반 크기·SSIM 동시 예측(Coulombe 그룹):** "K-Means Based Prediction of Transcoded JPEG File Size and Structural Similarity", "Efficient Clustering-based Algorithm for Predicting File Size and Structural Similarity of Transcoded JPEG Images" 등이 있습니다.\[15\]\[16\]
- **Deep Selector-JPEG(arXiv 2302.09560, 2023):** 후보 QF마다 "이 QF가 목표 MS-SSIM을 만족하는가"를 DNN 이진 분류기로 예측하고, 그중 가장 낮은 feasible QF를 고릅니다. **"이미지 → 필요한 QF" 직접 예측의 전형적 구조**입니다.
- **JND/SUR 예측:**
  - SUR-Net(PCS 2019), SUR-FeatNet(arXiv 2001.02002)은 MCL-JCI·JND-Pano 데이터로 학습합니다.\[17\]
  - SUR-FeatNet 저자들은 "목표 만족 사용자 비율에 대해 예측된 SUR 곡선으로 원본과 구분 안 되는 JPEG QF를 결정할 수 있어, 주관 평가 없이 비트 절감이 가능하다"고 명시합니다.\[17\]
  - Bondžulić 외의 "Efficient Prediction of the First Just Noticeable Difference Point for JPEG Images"는 PSNR 기반의 가벼운 함수로 첫 JND를 예측합니다.\[18\]
- **최신 데이터셋:** JPEG AIC2026(arXiv 2607.22783)은 ColorVideoVDP 기준 0.2~4.0 JND 범위의 왜곡 이미지 9,618장을 제공합니다. 고충실도 구간의 Q 선택 모델 학습에 쓸 수 있습니다.

### 2. 업계/실무 시스템

#### 2-1. 동영상의 content-adaptive encoding

- **Netflix Per-Title(2015, Aaron·Li·Manohara·De Cock·Ronca):** "각 타이틀은 고유한 복잡도에 맞춘 비트레이트 래더를 가져야 한다"는 원칙입니다. 해상도×비트레이트 격자를 전수 인코딩해 convex hull을 구합니다.\[1\]\[19\]
- **Dynamic Optimizer(2018):** shot 단위 convex hull + 동일 R-D 기울기(equal slope) 원칙입니다. 정지 이미지 다수를 "하나의 저장 예산"으로 보고 이미지별 Q를 배분하는 문제에 그대로 적용 가능한 이론 틀입니다.
- **CRF/constant-quality:** x264/x265 CRF처럼 "고정 품질 + 가변 비트"가 사실상의 표준입니다. ab-av1 같은 도구는 VMAF 목표에 맞는 CRF를 샘플 구간 인코딩으로 탐색합니다. 이미지에서는 "샘플 구간"이 "샘플 타일/크롭"에 해당합니다.
- **전이되는 교훈:**
  - (a) 고정 크기보다 고정 품질이 효율적입니다.
  - (b) 전수 탐색은 비싸므로 빠른 인코더/프리셋의 결과를 느린 인코더의 프록시로 쓸 수 있습니다. 빠른 인코더로 만든 convex hull이 느린 인코더의 좋은 대리값이라는 결과가 있습니다.
  - (c) 특징 기반 예측으로 탐색 횟수를 줄입니다.

#### 2-2. 이미지 CDN의 auto quality

| 서비스 | 공개된 동작 방식 | 콘텐츠 적응 여부 |
|---|---|---|
| Cloudinary `q_auto` | "이미지 내용과 포맷에 따라 지각 지표와 휴리스틱으로 품질 설정을 조정"하며 특허 기술. `q_auto:best/good/eco/low` 단계 제공, Save-Data 헤더 시 eco로 전환.\[20\] 핵심 지표로 자체 SSIMULACRA를 개발. Jon Sneyers의 2017년 블로그(Scale API 크라우드소싱 비교 4,000건 이상)에서 사람 판단과 갈린 이미지 쌍 682개 기준 정답률은 SSIMULACRA 87%, DSSIM 82%, Butteraugli 80%, PSNR 67%. 고신뢰 판단만 보면 SSIMULACRA 거의 98%, Butteraugli·DSSIM 각 91%, PSNR 약 78% | 예 (지각 지표 기반) |
| Akamai Image & Video Manager | "Perceptual Quality": SSIM 기반으로 이미지마다 품질을 조정, "Perceptual Quality Minimum"으로 하한 설정, 정적 기본값 85\[21\]\[22\] | 예 (SSIM 기반)\[23\] |
| imgix `auto=compress` | 품질을 75→45로 고정 하향, 메타데이터 제거, 투명도에 따라 포맷 선택\[24\]\[25\] | 사실상 아니오 (고정 Q) |

imgix 사례는 "auto"라는 이름이 붙어도 이미지별 적응이 아닐 수 있다는 점을 보여줍니다. 실제 이미지별 적응은 Cloudinary와 Akamai가 공개적으로 문서화한 수준에 그칩니다(세부 알고리즘은 비공개).

#### 2-3. 기업 엔지니어링 사례

- **Etsy, "Reducing Image File Size at Etsy"(Code as Craft, 2017, Will Gallego):** 귀하의 서비스와 구조가 가장 비슷한 공개 사례입니다.
  - 업로드 원본을 기준으로 DSSIM을 계산하며 JPEG 품질을 이진 탐색합니다. Q 78에서 시작해 70~85 범위로 제한합니다.
  - **한 크기에서 구한 Q를 같은 업로드의 모든 리사이즈본에 재사용**합니다(크기별로 Q가 거의 달라지지 않았기 때문).
  - 평균 Q는 기본값 85에서 "roughly 73"으로 내려갔고, 2016년 4월 대비 2017년 4월 저장 이미지 크기가 "often ranging from 25% to 30%" 줄었습니다.
- **Meta(Facebook):**
  - Facebook 지원을 받은 "A Hitchhiker's Guide to Structural Similarity"(Venkataramanan, Wu, Bovik, Katsavounidis, Shahid, 2021)는 SSIM 구현마다 결과가 달라 "휘는 자" 문제가 생긴다며 인코딩 평가용 SSIM 사용 권고를 정리했습니다.\[26\]
  - Katsavounidis 그룹의 convex hull 파라미터 예측 연구도 있습니다.
- **대규모 사진 서비스 재압축:** "Reducing Storage in Large-Scale Photo Sharing Services using Recompression"(arXiv 1912.11145)은 이미 양자화된 업로드 JPEG의 QF를 더 낮추면 오차가 크게 누적된다고 지적합니다.\[27\] 원본이 저품질 JPEG일 때는 Q를 공격적으로 낮추지 말아야 한다는 근거입니다.

#### 2-4. 오픈소스 도구

- **jpeg-archive / jpeg-recompress:**
  - 목표 지표(SSIM 기본, MS-SSIM, SmallFry, MPE)에 대해 min~max(기본 40~95) 범위에서 JPEG 품질을 이진 탐색합니다.
  - 기본 목표 SSIM은 0.9999이고, `--quality low/medium/high/veryhigh` 프리셋이 목표값을 바꿉니다.
  - `--accurate` 모드는 3~4배 느립니다.
- **Guetzli(Alakuijala, Obryk, Stoliarchuk, Szabadka, Vandevenne, Wassenberg, arXiv 1703.04421, 2017):**
  - Butteraugli 피드백 폐루프로 양자화 테이블과 계수를 최적화합니다.
  - 같은 Butteraugli 거리에서 "29-45% reduction in data size"를 보고했습니다. 비교 대상별로는 mozjpeg -quality 95 대비 −29.25%, 프로그레시브 mozjpeg 대비 −29.95%, -tune-ms-ssim 4:4:4 대비 −45.39%입니다.
  - 저자 스스로 "computation is currently extremely slow"라고 밝혀 실시간 서비스에는 부적합합니다.
- **JPEG XL `cjxl -d`(distance) / jpegli:**
  - Butteraugli 기반 "거리"를 품질 파라미터로 노출하며, 1.0이 대략 visually lossless입니다.\[28\]\[29\] 이론상 "고정 지각 품질" 인코딩입니다.
  - 하지만 Halide Compression의 "Measuring Image Encoder Consistency"(2025-09)는 SSIMULACRA2 기준으로 Q 단계별 표준편차를 측정했습니다. 그 결과 libjpeg-turbo가 오히려 가장 일관적이었고 **libjxl은 전반적으로 그다지 일관적이지 않았다**고 보고합니다.
  - libaom(AVIF)은 3.12.0에서 SSIMULACRA2 기반 `tune=iq`를 도입했습니다.\[30\]
- **libwebp 내장 옵션:**
  - `-size <bytes>`: 목표 크기에 맞추기 위해 부분 인코딩을 여러 번 수행.\[31\]\[32\]
  - `-psnr <dB>`: 목표 PSNR(문서상 일반적 값 42).\[32\]\[33\]
  - `-pass <n>`: 위 두 옵션의 이분 탐색 최대 횟수(최대 10). `-size`/`-psnr`를 쓰고 `-pass`를 지정하지 않으면 기본 6회.\[32\]\[34\]
  - `-sns <0..100>`(기본 50): "쉬운 부분에서 비트를 가져와 어려운 부분에 쓰는" 이미지 내 비트 재배분. 같은 Q에서 sns를 올리면 대개 파일이 커지고 품질이 좋아집니다.\[33\]\[35\]\[36\]
  - `-segments <1..4>`(기본 4), `-jpeg_like`(같은 Q의 JPEG와 비슷한 크기가 나오도록 매핑), `-preset photo/picture/drawing/icon/text`, `-near_lossless`, `-z`(lossless 프리셋).\[33\]\[34\]\[35\]
  - API에서는 `WebPConfig.target_size`, `target_PSNR`, `pass`로 같은 기능을 씁니다.\[37\]\[38\]
- **sharp/libvips:** WebP 출력 시 `quality`, `nearLossless`, `smartSubsample`, `effort` 등 기본 파라미터를 노출합니다. 하지만 목표 크기·목표 지각 품질 탐색은 내장하지 않으므로 애플리케이션 쪽에서 루프를 구현해야 합니다.
- **지각 지표 구현체:** SSIMULACRA2(Cloudinary, BSD-3), Butteraugli(libjxl 내장), DSSIM(Kornel Lesiński).\[39\] 최근 평가(Mohammadi, Jenadeleh, Sneyers, Saupe, Ascenso, "Evaluation of Objective Image Quality Metrics for High-Fidelity Image Compression", 2025)와 JPEG AIC-3 결과는 SSIMULACRA2를 고충실도 구간 최상위권 지표로 평가합니다.\[30\]\[40\]

### 3. 귀하가 바로 구현할 수 있는 저비용 프록시

#### 3-1. 시험 인코딩(trial encode) 계열 — 가장 정확하고 구현이 쉬움

- **원리:** 실제 인코더를 한 번 돌리는 것이 어떤 특징보다 정확한 "압축성 측정"입니다. libwebp `-m 0~2`(빠른 method)로 기준 Q에서 인코딩하면 최종 `-m 4~6` 크기와 강하게 상관된 bpp를 얻을 수 있습니다(정확한 비율은 자체 데이터로 보정 필요).
- **크기-Q 곡선의 모양:**
  - libwebp 주석에 따르면 크기는 양자화 단계의 약 3제곱 법칙을 따릅니다.
  - Q→양자화 인덱스 매핑은 q_index ≈ 127·(1 − L(Q/100)^(1/3))입니다. L은 Q<75에서 2Q/3, 이상에서 2Q−1인 구간 선형 함수이고, 여기에 SNS 세그먼트 보정이 더해집니다. 기본값(Q=75, sns=50)에서 기본 인덱스는 약 26입니다.
  - 실무적으로는 **Q 60~90 구간에서 log(bpp)가 Q에 대해 거의 선형**이라고 놓고, 이미지별 기울기를 두 점(두 번의 인코딩)으로 추정하는 할선법(secant)이 2~3회 안에 수렴하는 것이 일반적입니다. 이 수렴 횟수는 공개 벤치마크가 아니라 경험칙이므로 자체 검증이 필요합니다.
- **축소본/크롭 시험 인코딩:** Pigeon & Coulombe의 JPEG 결과에 따르면 축소본으로 원본 크기를 외삽할 때 축소 비율 50% 이하에서 오차가 커집니다. 이를 감안하면 **축소본보다는 원본 해상도의 무작위 타일(예: 전체 면적의 10~20%) 샘플링**이 더 안전합니다. 공개된 WebP 대상 오차 측정은 찾지 못했으므로 자체 데이터로 검증해야 합니다.
- **`cwebp -size`를 그대로 쓰기:** 목표 크기를 이미지별로 정하면(예: 해상도 기반 목표 bpp × 픽셀 수) 인코더가 내부에서 최대 6~10회 탐색합니다.\[34\] 구현은 가장 쉽지만 "목표가 크기"라는 근본적 한계(아래 3-3)는 그대로입니다.

#### 3-2. 저렴한 콘텐츠 특징

| 특징 | 비용 | 기대 예측력 | 비고 |
|---|---|---|---|
| 원본 JPEG의 bpp + 양자화 테이블(추정 QF) | 거의 0 (헤더 파싱) | 높음 (Pigeon & Coulombe: 동일 QF 근처 오차 수 %)\[14\] | 원본이 JPEG인 업로드에 한해 최고의 무료 특징 |
| PNG/lossless 압축 크기 | 중 | 중~높음 | 노이즈·그레인에 과민 |
| DCT 텍스처 에너지(VCA 방식) | 낮음 | 높음 | 코덱 비트와 직접 연관 |
| ρ (가상 양자화 후 0 계수 비율) | 낮음 | 높음 | He & Mitra 모델, Q별로 계산 가능\[12\] |
| Sobel SI, Laplacian 분산, 그래디언트 평균 | 매우 낮음 | 낮음~중 | VVC 논문은 SI 단독 예측력이 낮다고 평가\[5\] |
| 전역 엔트로피 | 매우 낮음 | 낮음 | 공간 구조 미반영\[8\] |
| 색상 수, 평탄 영역 비율, 텍스트/엣지 검출 | 낮음 | (크기보다는) lossless 선택·Q 하한 판단용 | 그래픽·스크린샷 분류 |

이 특징들 + 해상도 + Q를 입력으로 log(bpp)를 예측하는 gradient boosting/random forest 회귀는 VVC all-intra 논문과 같은 구조입니다. 귀하가 이미 만든 해상도 모델에도 자연스럽게 확장됩니다.

#### 3-3. "고정 크기" vs "고정 지각 품질" — 그리고 시각 마스킹

- **연구가 말하는 바:** 텍스처가 많은 영역은 대비 마스킹(contrast/texture masking) 때문에 같은 양자화 오차가 덜 보입니다. 이 원리는 JPEG 양자화 행렬 최적화(Watson의 DCTune, 1993)부터 libwebp SNS의 "조밀한 세그먼트를 더 양자화"하는 설계까지 반영되어 있습니다. 반대로 평탄한 그라데이션, 하늘, 피부, 텍스트, 선화는 블로킹·링잉·색 번짐이 쉽게 보입니다.
- **함의 1 — 귀하의 직관은 대체로 맞습니다:** 복잡한 풍경 사진에서 Q를 낮추는 것은 지각적으로 덜 해롭습니다. 지각 지표 목표(SSIMULACRA2/Butteraugli/DSSIM) 방식은 이를 자동으로 반영합니다. Etsy처럼 지표를 쓰면 복잡한 이미지는 자연스럽게 낮은 Q, 단순한 이미지는 높은 Q가 됩니다.
- **함의 2 — 하지만 "크기 기준"만으로는 위험합니다:**
  - 텍스트가 섞인 이미지·스크린샷은 크기가 중간 정도로 나와도 Q를 낮추면 바로 티가 납니다(CAT 논문의 지적).
  - 단순한 이미지는 Q를 올려도 바이트가 거의 늘지 않으므로 Q를 올려 얻는 이득이 큽니다.
  - 크기만 보고 Q를 내리면 노이즈 많은 저조도 사진·필름 그레인처럼 "크지만 품질 손실이 잘 보이는" 이미지를 망칠 수 있습니다.
- **함의 3 — 하이브리드가 실용적입니다:** "지각 품질 목표 + 크기 상한(cap) + Q 하한(floor)"이 Akamai(Perceptual Quality Minimum), Etsy(70~85 범위 제한) 모두가 택한 형태입니다.\[23\]\[41\]

#### 3-4. WebP 특유의 주의점

- **Q→양자화 매핑:** 위 식처럼 Q 75 부근이 "JPEG 같은 좋은 품질"의 중심이 되도록 설계되어 있고, `-jpeg_like`는 같은 Q의 libjpeg와 비슷한 크기가 나오도록 지수를 바꿉니다.\[3\]\[34\] 같은 Q라도 이미지마다 지각 품질이 다르므로(Halide의 일관성 측정 참조), 고정 Q 정책 자체가 콘텐츠별 품질 편차를 만듭니다.
- **4:2:0 고정:** WebP lossy는 크로마 서브샘플링이 4:2:0으로 고정되어 빨간 텍스트·채도 높은 얇은 선에서 번짐이 생깁니다. Q를 올려도 해결되지 않으므로 이런 이미지는 lossless 또는 near-lossless로 보내야 합니다(sharp의 `smartSubsample`이 일부 완화).
- **lossy vs lossless 자동 선택:**
  - libwebp는 정지 이미지에서 lossy/lossless를 자동 선택하지 않습니다(`WebPConfig.lossless`는 호출자가 지정). 자동 선택은 애니메이션 전용 `allow_mixed`/img2webp `-mixed`에만 있습니다.\[42\]\[43\]
  - Google WebP FAQ는 색 수가 적은 그래픽은 lossy로 변환하면 오히려 커질 수 있다고 밝힙니다.\[44\]
  - 따라서 **"색 수/평탄도/텍스트 휴리스틱으로 후보를 고른 뒤 lossy와 lossless(또는 `-near_lossless 60`)를 둘 다 인코딩해 작은 쪽 선택"**이 실무적 해법입니다.
- **원본이 이미 손실 압축된 경우:** 저품질 JPEG 원본을 높은 Q로 WebP 인코딩하면 블록 아티팩트까지 충실히 보존하느라 비트를 낭비합니다. 원본 JPEG의 추정 QF를 Q 상한으로 쓰는 규칙이 유효합니다.

## Recommendations

귀하의 서비스(업로드 → WebP 저장, 원본 불필요, 해상도 기반 Q 모델 보유)에 대한 단계별 권장안입니다.

**1단계 (즉시, 비용 최소): 시험 인코딩 1회 기반 보정**
1. 해상도 모델로 Q₀를 정합니다(기존 그대로).
2. Q₀, 빠른 method(`-m 2` 정도)로 한 번 인코딩해 bpp₀를 얻습니다.
3. 해상도 그룹별로 미리 수집한 "기대 bpp" 분포(중앙값)와 비교해 비율 r = bpp₀ / bpp_기대 를 구합니다.
4. 로그-선형 R-Q 모델(log bpp ≈ a + b·Q, b는 전체 데이터에서 추정)로 Q₁ = Q₀ − log(r)/b 를 계산하고 [Q_min, Q_max](예: 65~90)로 클램프합니다. 원본이 JPEG이면 원본 추정 QF + 소폭 여유를 상한으로 둡니다.
5. Q₁로 최종 인코딩합니다(최종 method 4~6). 인코딩 2회로 끝나며, 같은 업로드의 다른 리사이즈본에는 Etsy처럼 같은 Q 보정값을 재사용합니다.\[41\]

**2단계 (권장, 품질 중심): 지각 품질 목표 탐색**
- 목표를 크기 대신 SSIMULACRA2 점수(예: 70~80 구간 중 서비스 기준) 또는 Butteraugli 거리로 정합니다. 1단계 Q₁을 시작점으로 할선/이진 탐색을 2~4회 수행합니다.
- 대표 크기 하나에서만 탐색하고 결과 Q를 다른 크기에 적용하면 비용을 크게 줄일 수 있습니다.
- 크기 상한(해상도별 최대 bpp)과 Q 하한을 함께 둬 극단값을 막습니다.

**3단계 (데이터가 쌓이면): 경량 예측 모델로 탐색 횟수 줄이기**
- 2단계에서 나온 (특징, 최종 Q) 쌍을 학습 데이터로 씁니다. 특징은 해상도, 원본 포맷·원본 JPEG bpp·추정 QF, DCT 에너지(휘도/색차), Sobel SI, ρ@Q₀, 1단계의 bpp₀, 텍스트/그래픽 점수 등입니다.
- LightGBM 같은 회귀로 "목표 SSIMULACRA2를 만족하는 Q"를 예측해 탐색 시작점으로 쓰면 대부분 1~2회 인코딩으로 끝납니다.
- Deep Selector-JPEG나 SUR 예측 연구처럼 CNN을 쓸 수도 있습니다. 다만 업로드 서비스에서는 시험 인코딩 bpp 자체가 가장 강력한 특징이라 CNN의 추가 이득은 제한적일 가능성이 큽니다.

**공통 가드레일**
- 그래픽/스크린샷/텍스트 감지 시: lossless와 near-lossless를 시도해 작은 쪽을 택하고, lossy로 가더라도 Q 하한을 높게 잡습니다.
- 고정 크기 목표(`cwebp -size`)는 "저장 예산을 반드시 지켜야 하는" 경우에만 쓰고, 기본 정책은 고정 지각 품질로 합니다.
- 평가는 반드시 자체 업로드 샘플(수천 장)로 하십시오. 공개 수치는 대부분 JPEG/동영상 기반이어서 WebP에 그대로 옮겨지지 않습니다.

**더 파고들 검색 키워드(영어)**
- 파일 크기 예측: "JPEG file size prediction quality factor scaling", "transcoding file size prediction", "Pigeon Coulombe", "rate estimation intra coding", "bits estimation random forest QP"
- Rate 모델: "rho-domain rate model", "R-Q model", "R-lambda rate control", "Laplacian DCT coefficient rate model", "Cauchy rate model"
- 복잡도: "image complexity compression ratio", "spatial information P.910 compressibility", "Video Complexity Analyzer DCT energy"
- 지각 품질 기반 Q 선택: "just noticeable difference JPEG MCL-JCI", "satisfied user ratio prediction", "perceptually lossless quality factor selection", "visually lossless threshold", "target quality encoding SSIMULACRA2", "Butteraugli distance"
- 업계: "per-title encoding", "convex hull prediction", "content-adaptive encoding images", "q_auto", "perceptual quality image CDN"
- 원리: "contrast masking", "texture masking JPEG", "DCTune Watson"

## Caveats

- **공개 수치의 한계:** 크기 예측 오차(2.42% 등)는 JPEG→JPEG 트랜스코딩에서 원본 QF·크기를 아는 조건의 결과입니다. 원본 PNG/HEIC에서 WebP 크기를 예측하는 공개 벤치마크나, WebP에서 축소본·크롭 시험 인코딩의 오차를 측정한 공개 자료는 찾지 못했습니다.
- **Pigeon & Coulombe 코퍼스 규모:** IEEE 초록(10만 장)과 본문 실험 설명(약 7만 장) 사이에 차이가 있어, 논문 버전에 따라 다른 것으로 보입니다.\[13\]\[14\]
- **업계 알고리즘은 비공개:** Cloudinary q_auto, Akamai perceptual quality의 세부 로직은 공개되지 않았습니다. 여기 서술은 공식 문서·블로그에 공개된 범위에 한정됩니다.
- **libwebp 내부 상수:** 인용한 Q→양자화 매핑은 libwebp 1.x 소스 기준이며, 버전에 따라 일부 상수(필터 컷오프 등)가 다릅니다.
- **할선법 2~3회 수렴, "축소본보다 타일 샘플링이 안전" 등은 논리적 추론·경험칙**이며 공개 실험으로 검증된 수치가 아닙니다. 자체 데이터로 반드시 확인하십시오.
- **지각 지표도 완벽하지 않습니다:** SSIMULACRA2가 현재 최상위권이지만 지표별로 텍스트·색 번짐에 대한 민감도가 다릅니다. SSIM 구현마다 값이 달라지는 문제(Hitchhiker's Guide)도 있으므로 지표 구현을 고정하고 소규모 육안 검수를 병행해야 합니다.

## Sources

1. [Per-Title Encode Optimization. delivering the same or better…](http://techblog.netflix.com/2015/12/per-title-encode-optimization.html)
2. [Dynamic optimizer — a perceptual video encoding optimization framework](https://netflixtechblog.com/dynamic-optimizer-a-perceptual-video-encoding-optimization-framework-e19f1e3a277f)
3. [quant\_enc.c source code \[qtimageformats/src/3rdparty/libwebp/src/enc/quant\_enc.c\] - Codebrowser](https://codebrowser.dev/qt5/qtimageformats/src/3rdparty/libwebp/src/enc/quant_enc.c.html)
4. [Recommendation ITU-T P.910 (10/2023)](https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-P.910-202310-I!!PDF-E&type=items)
5. [All-intra rate control using low complexity video features for Versatile Video Coding](https://arxiv.org/pdf/2306.16786)
6. [. . Latest updates: hps://dl.acm.org/doi/10.1145/3524273.3532896 . .](https://dl.acm.org/doi/pdf/10.1145/3524273.3532896)
7. [Multimedia Communication: VCA: Video Complexity Analyzer](https://multimediacommunication.blogspot.com/2022/09/vca-video-complexity-analyzer.html)
8. [Image complexity and spatial information](https://www.researchgate.net/publication/260737316_Image_complexity_and_spatial_information)
9. [Average spatial information (SI) and temporal information (TI) for...](https://www.researchgate.net/figure/Average-spatial-information-SI-and-temporal-information-TI-for-video-sequences_fig1_357901898)
10. [CAT: Content-Adaptive Image Tokenization](https://arxiv.org/pdf/2501.03120)
11. [Recent Advances in Rate Control: From Optimisation to Implementation and Beyond](https://arxiv.org/html/2205.10815)
12. [Pre-encoding for high efficiency video coding](https://image-ppubs.uspto.gov/dirsearch-public/print/downloadPdf/10264268)
13. <https://espace2.etsmtl.ca/id/eprint/3016/1/Coulombe%20S.%202008%203016%20Computationally%20efficient%20algorithms%20for%20predicting%20the%20file%20size%20of%20JPEG%20images.pdf>
14. [Very Low Cost Algorithms for Predicting the File Size of JPEG Images Subject to Changes of Quality Factor and Scaling](https://ieeexplore.ieee.org/document/4483365/)
15. [Computationally efficient algorithms for predicting the file size of JPEG images subject to changes of quality factor and scaling](https://www.researchgate.net/publication/4350542_Computationally_efficient_algorithms_for_predicting_the_file_size_of_JPEG_images_subject_to_changes_of_quality_factor_and_scaling)
16. [Low-complexity transcoding of jpeg images with near-optimal quality using a predictive quality factor and scaling parameters](https://dl.acm.org/doi/10.5555/1771901.1771914)
17. [SUR-FeatNet: Predicting the Satisfied User Ratio Curvefor Image Compression with Deep Feature Learning](https://arxiv.org/pdf/2001.02002)
18. [Efficient Prediction of the First Just Noticeable Difference ...](https://acta.uni-obuda.hu/Bondzulic_Stojanovic_Petrovic_Pavlovic_Milicevic_115.pdf)
19. [Convex Hull Encoding: Optimal Bitrate-Resolution · Telemedicine · Fora Soft Learn](https://www.forasoft.com/learn/video-quality/articles-vqm/convex-hull-bitrate-resolution)
20. [Optimize Images](https://cloudinary.com/documentation/image_optimization)
21. [IVM Concepts](https://techdocs.akamai.com/ivm/reference/concepts)
22. [by Akamai Technologies - Image Manager](https://marketplace.microsoft.com/en-us/product/akamai-technologies.imageman?tab=Overview)
23. [Optimize your images](https://techdocs.akamai.com/ivm/docs/optimize-images)
24. [Automatic](https://docs.imgix.com/en-US/apis/rendering/automatic)
25. [New Auto Parameter: Compression](https://www.imgix.com/blog/auto-compress)
26. [A Hitchhiker's Guide to Structural Similarity](https://arxiv.org/pdf/2101.06354)
27. [Reducing Storage in Large-Scale Photo Sharing Services using Recompression](https://arxiv.org/pdf/1912.11145)
28. [GitHub - a-kaibu/jxl-encoder: GPU butteraugli + HIP support for jxl-encoder · GitHub](https://github.com/a-kaibu/jxl-encoder)
29. [Measuring Image Encoder Consistency](https://halide.cx/blog/consistency/)
30. [The Latest Advancements in JPEG XL](https://cloudinary.com/blog/the-latest-advancements-in-jpeg-xl)
31. [README - webm/libwebp - Git at Google](https://chromium.googlesource.com/webm/libwebp/+/0.4.4/README)
32. [man/cwebp.1 - webm/libwebp - Git at Google](https://chromium.googlesource.com/webm/libwebp/+/refs/heads/0.4.4/man/cwebp.1)
33. [WebP tools](https://chromium.googlesource.com/webm/libwebp/+/HEAD/doc/tools.md)
34. [developers.google.com](https://developers.google.com/speed/webp/docs/cwebp?authuser=3)
35. [libwebp/doc/tools.md at main · webmproject/libwebp](https://github.com/webmproject/libwebp/blob/main/doc/tools.md)
36. [developers.google.com](https://developers.google.com/speed/webp/docs/cwebp?hl=zh-cn)
37. [huggingface.co](https://huggingface.co/MNghia/soft_gripper_envs/blob/main/include/webp/encode.h)
38. [cwebp package - github.com/ideamans/libnextimage/golang/cwebp - Go Packages](https://pkg.go.dev/github.com/ideamans/libnextimage/golang/cwebp)
39. [GitHub - Ludicon/ic-metrics: Image quality metrics: SSIMULACRA2, SSIM, and MS-SSIM. · GitHub](https://github.com/Ludicon/ic-metrics)
40. [Evaluation of Objective Image Quality Metrics for High-Fidelity Image Compression](https://arxiv.org/html/2509.13150v1)
41. [Etsy Engineering](https://www.etsy.com/codeascraft/reducing-image-file-size-at-etsy)
42. [examples/img2webp.c - webm/libwebp - Git at Google](https://chromium.googlesource.com/webm/libwebp/+/master/examples/img2webp.c)
43. [WebP Container API Documentation](https://developers.google.com/speed/webp/docs/container-api)
44. [Frequently Asked Questions](https://developers.google.com/speed/webp/faq)
