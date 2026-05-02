# Unity Game Maker (UGM)

Unity 안에서 모듈 카탈로그를 열고 원하는 모듈을 선택해 Assets/ 아래에 추가하는 시스템.

## 설치

Unity Package Manager → `+` → "Add package from git URL" → 다음 URL 입력:

```
https://github.com/devchop2/ugm.git
```

## 사용

설치 후 Unity 메뉴에서:

```
Window > UGM > Open
```

좌측 목록에서 모듈을 선택하고 우측의 **Import** 버튼을 누르면 `Assets/UGM/<모듈명>/` 아래에 파일이 추가됩니다.

## 레지스트리 설정

`Packages/com.chopchopgames.ugm/Editor/Window/UGMWindow.cs` 의 `REGISTRY_URL` 상수를 본인의 모듈 카탈로그 저장소 raw URL로 변경하세요.

```
https://raw.githubusercontent.com/devchop2/ugm-modules/main/registry.json
```
