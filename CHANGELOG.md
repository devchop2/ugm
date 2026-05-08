# Changelog

## [0.2.0] - 2026-05-08
### Added
- **Git URL 기반 모듈 설치 (Unity Package Manager 표준 방식)**
  - `ModuleManifest.gitUrl` 필드 추가 — 비어있지 않으면 `Client.Add(gitUrl)` 로 manifest.json 의 dependencies 에 등록
  - 결과적으로 `Library/PackageCache/<name>@<hash>/` 에 git fetch + manifest 등록 → 향후 reinstall/update 자동 동작
- **임베디드 → git URL 자동 마이그레이션**
  - `Packages/<name>/` 임베디드 폴더 발견 시 사용자 확인 후 폴더 삭제 + git URL 등록
  - UGMWindow 의 Install 버튼 라벨이 자동으로 "Migrate to git URL" 로 표시
- **InstallSource 표시**
  - UGMWindow 상세 패널에 설치 방식 (Embedded / Git / Registry / LocalPath) 표시

### Changed
- `ModuleImporter.InstallAsync` 가 `gitUrl` 우선, 없으면 `downloadUrl(zip)` fallback
- `ModuleImporter.CheckInstalled` 가 임베디드 폴더 + UPM PackageInfo 둘 다 검사
- 옛 zip 다운로드 방식은 backward compat 으로 유지 (gitUrl 미정의 모듈용)

### Migration Notes
- 기존에 임베디드로 설치된 모듈은 UGM 창에서 해당 모듈 선택 → "Migrate to git URL" 버튼 클릭으로 안전하게 전환 가능
- registry.json 갱신 필요: 모듈마다 `gitUrl` 필드 추가 권장 (없으면 옛 zip 방식 fallback)

## [0.1.0] - 2026-05-03
### Added
- 초기 UGM 카탈로그 시스템
- ModuleImporter (zip 기반 임베디드 설치)
- UGMWindow EditorWindow
- registry.json schemaVersion 2
