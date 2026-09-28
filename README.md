# UI Sprite Maker

Unity 에디터 안에서 UI용 스프라이트를 바로 만들어주는 툴입니다.
크기, 모서리 반경, 그라데이션, 드롭 섀도, 글로우, 스트로크를 조절하면서 미리 보고,
**9-slice 보더가 설정된 PNG 스프라이트**로 구워서 UGUI `Image`에 바로 쓸 수 있습니다.

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

1. **Tools › UI Sprite Maker** 를 엽니다.
2. 왼쪽 패널에서 스타일을 조절합니다.
   | 항목 | 설정 |
   |---|---|
   | Shape | 크기(UI 단위), 모서리 반경(전체 또는 모서리별), 출력 배율 @1x–@4x |
   | Fill | 단색 / 선형 그라데이션(각도) / 방사형 그라데이션(중심, 반경) |
   | Stroke | 두께, 위치(Inside / Center / Outside), 단색 또는 그라데이션 |
   | Drop Shadows | 여러 개 가능 — 색, 오프셋(+Y = 아래), 블러, 스프레드 |
   | Outer Glow | 색, 크기, 스프레드, 강도 |
   | Inner Shadow / Inner Glow | 색, 오프셋, 블러, 초크 |
   | Nine Slice | 9-slice 보더 자동 설정 여부 |
3. 오른쪽에서 미리보기를 확인합니다. 초록색 선은 9-slice 보더입니다.
4. **Export PNG…** 로 저장합니다. 이후 **Overwrite** 로 같은 파일을 다시 구울 수 있습니다.
5. Hierarchy에서 `Image`가 있는 오브젝트를 선택하고 **Save & Apply to Selected Image** 를 누르면
   스프라이트 지정, `Image Type = Sliced`, RectTransform 크기, Raycast Padding(그림자 영역 클릭 제외)까지 한 번에 설정됩니다.

### 다시 편집하기

구운 스프라이트에는 스타일 정보가 함께 저장됩니다.
Project 창에서 PNG를 우클릭 → **UI Sprite Maker › Edit in Sprite Maker** 로 그대로 불러와 수정할 수 있습니다.

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

- **C# API**: `UISpriteMakerApi.Bake(specJson, "Assets/UI/X.png")`, `RenderPng`, `Validate`, `GetSpec`, `ApplyToImage`
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
- 텍스처는 그라데이션 품질을 위해 비압축으로 임포트됩니다. 필요하면 임포트 설정에서 바꾸세요.

## 테스트

사용하는 프로젝트의 `Packages/manifest.json`에 다음을 추가하면 Test Runner(EditMode)에 테스트가 표시됩니다.

```json
"testables": ["com.glglekdy.ui-sprite-maker"]
```
