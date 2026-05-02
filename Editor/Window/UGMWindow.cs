using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UGM.Editor
{
    /// <summary>
    /// UGM 메인 EditorWindow. Package Manager 처럼 좌측 모듈 목록 + 우측 상세 패널 구조.
    /// </summary>
    public class UGMWindow : EditorWindow
    {
        // ugm-modules 저장소의 registry.json raw URL.
        // 브랜치명(main/master)은 첫 push 한 브랜치에 맞춰 변경하세요.
        private const string REGISTRY_URL =
            "https://raw.githubusercontent.com/devchop2/ugm-modules/main/registry.json";

        private readonly List<ModuleManifest> _modules = new List<ModuleManifest>();
        private ModuleManifest _selected;

        private ListView _listView;
        private VisualElement _detail;
        private Label _statusLabel;

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

            _statusLabel = new Label("준비 중...");
            _statusLabel.style.marginLeft = 12;
            _statusLabel.style.opacity = 0.7f;
            toolbar.Add(_statusLabel);

            root.Add(toolbar);

            // 좌우 분할
            var split = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;

            // 좌측: 모듈 목록
            var leftPane = new VisualElement();
            _listView = new ListView
            {
                fixedItemHeight = 28,
                selectionType = SelectionType.Single,
                makeItem = MakeListItem,
                bindItem = BindListItem,
                itemsSource = _modules
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
            var label = new Label
            {
                style =
                {
                    paddingLeft = 10,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            return label;
        }

        private void BindListItem(VisualElement el, int index)
        {
            if (index < 0 || index >= _modules.Count) return;
            ((Label)el).text = $"{_modules[index].displayName}  v{_modules[index].version}";
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

        private void LoadRegistry()
        {
            _statusLabel.text = "레지스트리 로드 중...";
            RegistryClient.FetchAsync(REGISTRY_URL,
                onSuccess: registry =>
                {
                    _modules.Clear();
                    if (registry?.modules != null) _modules.AddRange(registry.modules);
                    _listView.RefreshItems();
                    _statusLabel.text = $"{_modules.Count}개 모듈";
                    if (_modules.Count == 0) ShowEmptyDetail();
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

            var subline = new Label($"id: {_selected.id}    ·    v{_selected.version}")
            {
                style = { opacity = 0.6f, marginBottom = 10 }
            };
            _detail.Add(subline);

            var desc = new Label(string.IsNullOrEmpty(_selected.description) ? "(설명 없음)" : _selected.description)
            {
                style = { whiteSpace = WhiteSpace.Normal, marginBottom = 16 }
            };
            _detail.Add(desc);

            // 의존성 / SDK 표시 (있을 때만)
            if (_selected.dependencies != null && _selected.dependencies.Count > 0)
            {
                _detail.Add(new Label($"의존 모듈: {string.Join(", ", _selected.dependencies)}")
                {
                    style = { opacity = 0.8f, marginBottom = 4 }
                });
            }
            if (_selected.sdks != null && _selected.sdks.Count > 0)
            {
                _detail.Add(new Label($"필요 SDK: {string.Join(", ", _selected.sdks)}")
                {
                    style = { opacity = 0.8f, marginBottom = 4 }
                });
            }

            var fileCount = _selected.files?.Count ?? 0;
            _detail.Add(new Label($"포함 파일: {fileCount}개")
            {
                style = { opacity = 0.8f, marginBottom = 16 }
            });

            var importBtn = new Button(ImportSelected) { text = "Import" };
            importBtn.style.width = 140;
            importBtn.style.height = 28;
            _detail.Add(importBtn);
        }

        private void ImportSelected()
        {
            if (_selected == null) return;
            var moduleToImport = _selected;
            ModuleImporter.ImportAsync(REGISTRY_URL, moduleToImport, ok =>
            {
                if (ok)
                {
                    EditorUtility.DisplayDialog("UGM",
                        $"'{moduleToImport.displayName}' 임포트 완료.\nAssets/UGM/{moduleToImport.id}/ 를 확인하세요.",
                        "확인");
                }
            });
        }
    }
}
