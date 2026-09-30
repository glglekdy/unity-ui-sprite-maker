# UI Sprite Maker

Unity 에디터 안에서 UI용 스프라이트를 바로 만들어주는 툴입니다.
크기, 모서리 반경, 그라데이션, 드롭 섀도, 글로우, 스트로크를 조절하면서 미리 보고,
**9-slice 보더가 설정된 PNG 스프라이트**로 구워서 UGUI `Image`에 바로 쓸 수 있습니다.

Figma처럼 **레이어**도 쌓을 수 있습니다. 스프라이트 안에 도형·이미지·텍스트·그룹을 넣고,
레이어마다 불투명도, 블렌드 모드, 회전, 마스크(Clip), 그림자/글로우/스트로크를 줄 수 있습니다.
결과는 **한 장의 PNG**로 굽거나, 텍스트는 TextMeshPro·이미지는 `Image`로 분리된 **UGUI 프리팹**으로 내보낼 수 있습니다.

- Unity 6 (6000.0+) / UGUI
- 에디터 전용 패키지 — 빌드에 런타임 비용이나 코드가 포함되지 않습니다.

## 설치

Package Manager → **+** → *Install package from git URL…*

```
https://github.com/glglekdy/unity-ui-sprite-maker.git
```

또는 로컬 경로로 설치하려면 `Packages/manifest.json`에 다음을 추가합니다.

```json
"com.glglekdy.ui-sprite-maker": "file:C:/path/to/unity-ui-sprite-maker"
```

## 사용법

1. **Tools › UI Sprite Maker** 를 엽니다. 창은 세 부분입니다.
   - **왼쪽 — 레이어**: 맨 위가 가장 앞에 그려지는 레이어입니다. `+ Shape / + Image / + Text / + Group` 으로 추가하고,
     행을 드래그해서 순서나 부모를 바꿉니다. 눈 아이콘은 표시/숨김, 자물쇠는 잠금(캔버스에서 선택 안 됨),
     더블클릭은 이름 바꾸기, 우클릭은 복제·그룹·순서·삭제 메뉴입니다.
   - **가운데 — 캔버스**: 클릭으로 선택, 드래그로 이동, 네 모서리와 네 변의 핸들로 크기 조절,
     모서리 바깥쪽을 드래그하면 회전합니다. 부모의 가장자리·가운데에 스냅됩니다.
   - **오른쪽 — 속성**: 선택한 레이어(아무것도 선택하지 않으면 Frame)의 속성입니다.
2. Frame(스프라이트 바탕 도형)과 각 레이어의 속성을 조절합니다.
   | 항목 | 설정 |
   |---|---|
   | Shape | 크기(UI 단위), 모서리 반경(전체 또는 모서리별), 출력 배율 @1x–@4x |
   | Fill | 단색 / 선형 그라데이션(각도) / 방사형 그라데이션(중심, 반경) |
   | Stroke | 두께, 위치(Inside / Center / Outside), 단색 또는 그라데이션 |
   | Drop Shadows | 여러 개 가능 — 색, 오프셋(+Y = 아래), 블러, 스프레드 |
   | Outer Glow | 색, 크기, 스프레드, 강도 |
   | Inner Shadow / Inner Glow | 색, 오프셋, 블러, 초크 |
   | Nine Slice | 9-slice 보더 자동 설정 여부 |
   | Clip | Frame 밖으로 나간 레이어를 잘라냄 |

   레이어 공통 속성은 다음과 같습니다.
   | 항목 | 설정 |
   |---|---|
   | Transform | 위치(부모 박스의 왼쪽 위 기준, 아래로 +), 크기, 회전(시계 방향) |
   | Layer | 불투명도, 블렌드 모드(Normal/Multiply/Screen/Overlay/Darken/Lighten/Add), 표시, 잠금, Clip Content, Prefab Export |
   | Constraints | 부모 크기가 바뀔 때 따라가는 방식(Left/Right/Center/Stretch/Scale). 프리팹의 앵커로도 쓰입니다 |
   | Effects | 스트로크, 드롭 섀도, 글로우, 이너 섀도/글로우. 도형뿐 아니라 텍스트와 이미지에도 적용됩니다 |

   레이어 종류별로는 다음 속성이 있습니다.
   - **Shape**: 모서리 반경, 채우기. 안에 자식 레이어를 넣을 수 있습니다.
   - **Image**: Sprite 또는 Texture2D, 틴트, 맞춤(Stretch/Contain/Cover/Sliced).
   - **Text**: 폰트(.ttf/.otf), 크기, 색 또는 그라데이션, 정렬, 줄 간격, 자간, 줄바꿈.
     폰트를 비워 두면 Inter(라틴 문자 전용)를 씁니다. **한글은 한글 폰트를 지정해야 합니다.**
   - **Group**: 자식을 묶는 박스. 불투명도·블렌드·효과를 주면 그룹 전체가 한 덩어리로 합성됩니다.
3. 가운데에서 미리보기를 확인합니다. 초록색 선은 9-slice 보더입니다.
4. **Export PNG…** 로 저장합니다. 이후 **Overwrite** 로 같은 파일을 다시 구울 수 있습니다.
5. Hierarchy에서 `Image`가 있는 오브젝트를 선택하고 **Save & Apply to Selected Image** 를 누르면
   스프라이트 지정, `Image Type = Sliced`, RectTransform 크기, Raycast Padding(그림자 영역 클릭 제외)까지 한 번에 설정됩니다.
6. **Export Prefab…** 을 누르면 레이어를 UGUI 프리팹으로 내보냅니다. Frame과 구워지는 레이어는
   9-slice 스프라이트 `Image`가 되고, 텍스트는 `TextMeshProUGUI`, 이미지는 `Image` 오브젝트가 되어
   Constraints대로 앵커가 잡힙니다. 스프라이트는 프리팹 옆 `<이름>_Sprites/` 폴더에 저장됩니다.
   레이어별 **Prefab Export**(Auto/Bake/Object)로 구울지 분리할지 고를 수 있습니다.
   UGUI로 표현할 수 없는 레이어(블렌드 모드, 텍스트 효과 등)는 자동으로 구워지고 Console에 경고가 표시됩니다.

### 캔버스 단축키

| 조작 | 동작 |
|---|---|
| 클릭 / 더블클릭 / Ctrl+클릭 | 최상위 레이어 선택 / 한 단계 안쪽 선택 / 가장 안쪽 레이어 바로 선택 |
| 드래그 (+Shift) | 이동 (+가로·세로 한 방향으로 고정) |
| 핸들 드래그 (+Shift, +Alt) | 크기 조절 (+비율 유지, +가운데 기준) |
| 모서리 바깥 드래그 (+Shift) | 회전 (+15° 단위) |
| 방향키 (+Shift) | 1 (10) 단위 이동 |
| Delete · Ctrl+D · Ctrl+G | 삭제 · 복제 · 그룹으로 묶기 |
| Ctrl+] · Ctrl+[ | 앞으로 · 뒤로 |
| Esc | 부모 선택 |
| Ctrl+휠 | 확대/축소 |

### 다시 편집하기

구운 스프라이트와 프리팹에는 스타일 정보(레이어 포함)가 함께 저장됩니다.
Project 창에서 PNG나 프리팹을 우클릭 → **UI Sprite Maker › Edit in Sprite Maker** 로 그대로 불러와 수정할 수 있습니다.

### 프리셋

창 상단의 **Save As…** 로 현재 스타일을 `UISpriteStyle` 에셋으로 저장하고, **Load** 로 불러옵니다.
*Create › UI Sprite Maker › UI Sprite Style* 로 직접 만들 수도 있습니다.

## AI / 스크립트에서 사용하기

JSON 스펙 하나로 스프라이트를 정의하면 코드나 명령줄로 바로 구울 수 있습니다. 다른 AI 에이전트(Claude Code, Codex, Cursor 등)도 이 방식으로 쓸 수 있습니다.
전체 스펙과 사용법은 [AGENTS.md](AGENTS.md)에 있습니다.

```json
{ "output": "Assets/UI/Sprites/PrimaryButton.png",
  "size": [240, 72], "radius": 36, "scale": 2,
  "fill": { "type": "linear", "direction": "to top", "colors": ["#2F6BFF", "#6FA8FF"] },
  "shadows": [ { "color": "#1B3A8A66", "offset": [0, 6], "blur": 14 } ] }
```

레이어는 `"layers": [ ... ]` 에 도형·이미지·텍스트·그룹을 나열해서 표현합니다(AGENTS.md의 **Layers** 참고).
`"prefab": "Assets/UI/Prefabs/X.prefab"` 을 넣으면 CLI가 프리팹도 만듭니다.

- **C# API**: `UISpriteMakerApi.Bake(specJson, "Assets/UI/X.png")`, `BakePrefab`, `RenderPng`, `Validate`, `GetSpec`, `ApplyToImage`
- **배치모드 CLI** (에디터가 닫혀 있을 때):
  `Unity -batchmode -nographics -projectPath <프로젝트> -executeMethod UISpriteMaker.Editor.UISpriteMakerCli.Bake -spec sprites.json -preview <폴더>`
- **창의 Spec JSON › Copy / Paste**: 창에서 만든 스타일을 JSON으로 복사하거나, AI가 만든 JSON을 붙여넣어 불러옵니다.

이 패키지를 설치한 프로젝트에서 AI가 가이드를 찾도록 하려면, 그 프로젝트의 `CLAUDE.md`나 `AGENTS.md`에 다음을 추가하세요.

```md
## UI sprites
Make UI background sprites (rounded corners, gradients, shadows, glows) with the UI Sprite Maker package
(com.glglekdy.ui-sprite-maker) instead of drawing textures. Read its guide first:
Library/PackageCache/com.glglekdy.ui-sprite-maker*/AGENTS.md
```

## 참고

- 스프라이트에는 그림자/글로우를 위한 투명 여백이 포함됩니다. *Apply* 기능은 도형이 디자인한 크기로 보이도록
  RectTransform 크기를 여백 포함 크기로 맞춥니다.
- Pixels Per Unit은 `100 × 배율`로 설정되므로 @2x로 구워도 Canvas에서는 같은 크기로 보입니다.
- 그라데이션을 9-slice로 늘리면 가운데만 늘어나서 그라데이션이 고르지 않게 보일 수 있습니다(창에 경고 표시).
- 레이어가 들어간 PNG는 레이어를 피해서 9-slice로 늘어나는 영역을 잡습니다. 모든 영역을 레이어가 가리고 있으면
  늘릴 때 레이어가 찌그러지니(창에 경고 표시) 디자인한 크기 그대로 쓰거나 프리팹으로 내보내세요.
- 텍스트는 Unity FontEngine으로 직접 그리므로 배치모드(`-nographics`)에서도 구워집니다. 프리팹의 TextMeshPro 오브젝트는
  프로젝트에 *TMP Essential Resources* 가 임포트되어 있어야 만들어집니다(없으면 텍스트를 스프라이트에 굽습니다).
- 텍스처는 그라데이션 품질을 위해 비압축으로 임포트됩니다. 필요하면 임포트 설정에서 바꾸세요.

## 테스트

사용하는 프로젝트의 `Packages/manifest.json`에 다음을 추가하면 Test Runner(EditMode)에 테스트가 표시됩니다.

```json
"testables": ["com.glglekdy.ui-sprite-maker"]
```
