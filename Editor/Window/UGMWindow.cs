using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChopChopGames.UGM.EditorTools
{
    /// <summary>
    /// UGM 메인 EditorWindow. Package Manager 처럼 좌측 모듈 목록 + 우측 상세 패널 구조.
    /// 검색바로 필터링 가능.
    ///
    /// [v0.2.0+] Install 버튼은 module 의 gitUrl 이 있으면 Unity Package Manager 의 git URL 방식
    /// (manifest.json 의 dependencies 에 등록 → git fetch) 으로 설치합니다.
    /// gitUrl 이 없는 옛 모듈은 zip 다운로드 방식(Packages/ 임베디드) 으로 fallback.
    /// 임베디드된 옛 설치본은 "Migrate to git URL" 버튼으로 안전하게 전환 가능.
    /// </summary>
    public class UGMWindow : EditorWindow
    {
        // ugm-modules 저장소의 registry.json raw URL.
        // 브랜치명(main/master)은 첫 push 한 브랜치에 맞춰 변경하세요.
        private const string REGISTRY_URL =
            "https://raw.githubusercontent.com/devchop2/ugm-modules/main/registry.json";

        private readonly List<ModuleManifest> _allModules = new List<ModuleManifest>();
        private readonly List<ModuleManifest> _filtered = new List<ModuleManifest>();
        private ModuleManifest _selected;
        private string _searchText = string.Empty;

        private ListView _listView;
        private VisualElement _detail;
        private Label _statusLabel;
        private ToolbarSearchField _searchField;

        [MenuItem("Window/UGM/Open")]
        public static void Open()
        {
            var window = GetWindow<UGMWindow>("UGM");
            window.minSize = new Vector2(720, 420);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            // 상단 툴바
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 6;
            toolbar.style.paddingRight = 6;
            toolbar.style.paddingTop = 4;
            toolbar.style.paddingBottom = 4;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = new Color(0, 0, 0, 0.2f);

            var refreshButton = new Button(LoadRegistry) { text = "↻ 새로고침" };
            toolbar.Add(refreshButton);

            _searchField = new ToolbarSearchField();
            _searchField.style.flexGrow = 1;
            _searchField.style.marginLeft = 8;
            _searchField.style.marginRight = 8;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchText = evt.newValue ?? string.Empty;
                ApplyFilter();
            });
            toolbar.Add(_searchField);

            _statusLabel = new Label("준비 중...");
            _statusLabel.style.opacity = 0.7f;
            toolbar.Add(_statusLabel);

            root.Add(toolbar);

            // 좌우 분할
            var split = new TwoPaneSplitView(0, 280, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;

            // 좌측: 모듈 목록
            var leftPane = new VisualElement();
            _listView = new ListView
            {
                fixedItemHeight = 32,
                selectionType = SelectionType.Single,
                makeItem = MakeListItem,
                bindItem = BindListItem,
                itemsSource = _filtered
            };
            _listView.selectionChanged += OnSelectionChanged;
            _listView.style.flexGrow = 1;
            leftPane.Add(_listView);

            // 우측: 상세 패널
            _detail = new VisualElement();
            _detail.style.paddingLeft = 14;
            _detail.style.paddingRight = 14;
            _detail.style.paddingTop = 12;
            _detail.style.paddingBottom = 12;

            split.Add(leftPane);
            split.Add(_detail);
            root.Add(split);

            ShowEmptyDetail();
            LoadRegistry();
        }

        private static VisualElement MakeListItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.paddingLeft = 10;
            row.style.paddingTop = 4;

            var title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(title);

            var subline = new Label { name = "subline" };
            subline.style.opacity = 0.65f;
            subline.style.fontSize = 10;
            row.Add(subline);

            return row;
        }

        private void BindListItem(VisualElement el, int index)
        {
            if (index < 0 || index >= _filtered.Count) return;
            var m = _filtered[index];

            var titleLabel = el.Q<Label>("title");
            var sublineLabel = el.Q<Label>("subline");

            titleLabel.text = $"{m.displayName}  v{m.version}";

            var info = ModuleImporter.CheckInstalled(m);
            string status;
            if (m.IsLegacyV1) status = "[legacy v1]";
            else if (!info.Installed) status = "Not installed";
            else if (info.InstalledVersion == m.version) status = $"Installed (v{info.InstalledVersion})";
            else status = $"Outdated (installed: v{info.InstalledVersion})";

            sublineLabel.text = $"{m.id}  ·  {status}";
        }

        private void OnSelectionChanged(IEnumerable<object> selected)
        {
            _selected = null;
            foreach (var item in selected)
            {
                _selected = item as ModuleManifest;
                break;
            }
            RenderDetail();
        }

        private void ApplyFilter()
        {
            _filtered.Clear();
            if (string.IsNullOrEmpty(_searchText))
            {
                _filtered.AddRange(_allModules);
            }
            else
            {
                var q = _searchText.ToLowerInvariant();
                foreach (var m in _allModules)
                {
                    if (m == null) continue;
                    if ((m.id ?? "").ToLowerInvariant().Contains(q)
                        || (m.displayName ?? "").ToLowerInvariant().Contains(q)
                        || (m.description ?? "").ToLowerInvariant().Contains(q)
                        || (m.name ?? "").ToLowerInvariant().Contains(q))
                    {
                        _filtered.Add(m);
                    }
                }
            }
            _listView?.RefreshItems();
            _statusLabel.text = $"{_filtered.Count}/{_allModules.Count}개 모듈";
        }

        private void LoadRegistry()
        {
            _statusLabel.text = "레지스트리 로드 중...";
            RegistryClient.FetchAsync(REGISTRY_URL,
                onSuccess: registry =>
                {
                    _allModules.Clear();
                    if (registry?.modules != null) _allModules.AddRange(registry.modules);
                    ApplyFilter();
                    if (_allModules.Count == 0) ShowEmptyDetail();
                },
                onError: error =>
                {
                    _statusLabel.text = "로드 실패";
                    Debug.LogError($"[UGM] 레지스트리 로드 실패: {error}\nURL: {REGISTRY_URL}");
                    EditorUtility.DisplayDialog("UGM",
                        $"레지스트리 로드 실패\n\n{error}\n\nUGMWindow.cs 의 REGISTRY_URL 이 올바른지 확인하세요.",
                        "확인");
                });
        }

        private void ShowEmptyDetail()
        {
            _detail.Clear();
            var hint = new Label("좌측 목록에서 모듈을 선택하세요.\n\n등록된 모듈이 없다면 ugm-modules 저장소의 registry.json 을 확인하세요.")
            {
                style = { whiteSpace = WhiteSpace.Normal, opacity = 0.6f }
            };
            _detail.Add(hint);
        }

        private void RenderDetail()
        {
            _detail.Clear();
            if (_selected == null) { ShowEmptyDetail(); return; }

            var title = new Label(_selected.displayName)
            {
                style =
                {
                    fontSize = 18,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 2
                }
            };
            _detail.Add(title);

            var idLine = string.IsNullOrEmpty(_selected.name)
                ? $"id: {_selected.id}    ·    v{_selected.version}"
                : $"{_selected.name}    ·    v{_selected.version}";
            var subline = new Label(idLine)
            {
                style = { opacity = 0.6f, marginBottom = 10 }
            };
            _detail.Add(subline);

            var desc = new Label(string.IsNullOrEmpty(_selected.description) ? "(설명 없음)" : _selected.description)
            {
                style = { whiteSpace = WhiteSpace.Normal, marginBottom = 16 }
            };
            _detail.Add(desc);

            // 의존성 표시 (있을 때만)
            if (_selected.dependencies != null && _selected.dependencies.Count > 0)
            {
                _detail.Add(new Label($"의존 모듈: {string.Join(", ", _selected.dependencies)}")
                {
                    style = { opacity = 0.8f, marginBottom = 4 }
                });
            }

            // 설치 상태
            var info = ModuleImporter.CheckInstalled(_selected);
            string statusText;
            if (_selected.IsLegacyV1) statusText = "이 모듈은 옛 v1 설계라 자동 설치 불가 (마이그레이션 필요)";
            else if (!info.Installed) statusText = "현재 상태: 설치되지 않음";
            else
            {
                string srcLabel = info.InstallSource == ModuleImporter.InstallSource.Embedded ? " [임베디드 — git URL 전환 권장]" :
                                  info.InstallSource == ModuleImporter.InstallSource.Git ? " [git URL]" :
                                  info.InstallSource == ModuleImporter.InstallSource.Registry ? " [registry]" :
                                  info.InstallSource == ModuleImporter.InstallSource.LocalPath ? " [local]" : "";
                if (info.InstalledVersion == _selected.version)
                    statusText = $"현재 상태: 설치됨 (v{info.InstalledVersion}){srcLabel}";
                else
                    statusText = $"현재 상태: 업데이트 가능 (설치된 v{info.InstalledVersion} → 카탈로그 v{_selected.version}){srcLabel}";
            }

            _detail.Add(new Label(statusText)
            {
                style = { opacity = 0.85f, marginBottom = 12, whiteSpace = WhiteSpace.Normal }
            });

            // Documentation 링크
            if (!string.IsNullOrEmpty(_selected.documentationUrl))
            {
                var docBtn = new Button(() => Application.OpenURL(_selected.documentationUrl)) { text = "📖 Documentation" };
                docBtn.style.width = 160;
                docBtn.style.marginBottom = 8;
                _detail.Add(docBtn);
            }

            // Install / Update / 설치됨 버튼
            if (_selected.IsLegacyV1)
            {
                var disabled = new Button { text = "Install (legacy 비활성)" };
                disabled.SetEnabled(false);
                disabled.style.width = 200;
                _detail.Add(disabled);
            }
            else
            {
                string btnLabel;
                if (!info.Installed) btnLabel = "Install";
                else if (info.InstallSource == ModuleImporter.InstallSource.Embedded && _selected.HasGitUrl)
                    btnLabel = "Migrate to git URL";
                else if (info.InstalledVersion != _selected.version) btnLabel = $"Update to v{_selected.version}";
                else btnLabel = "Reinstall";

                var installBtn = new Button(InstallSelected) { text = btnLabel };
                installBtn.style.width = 200;
                installBtn.style.height = 28;
                _detail.Add(installBtn);
            }
        }

        private void InstallSelected()
        {
            if (_selected == null) return;
            var moduleToInstall = _selected;
            ModuleImporter.InstallAsync(moduleToInstall, (ok, info) =>
            {
                if (ok)
                {
                    EditorUtility.DisplayDialog("UGM",
                        $"'{moduleToInstall.displayName}' v{moduleToInstall.version} 설치 완료.\n위치: {info}",
                        "확인");
                    // 리스트 갱신해서 상태 표시 업데이트
                    _listView?.RefreshItems();
                    RenderDetail();
                }
                // 실패는 ModuleImporter 가 다이얼로그를 이미 띄움
            });
        }
    }
}
