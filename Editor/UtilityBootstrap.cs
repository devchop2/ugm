using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ChopChopGames.UGM.EditorTools
{
    /// <summary>
    /// UGM 코어 첫 로드 시점에 UGM Utility 패키지(`com.chopchopgames.ugm.utility`)가 미설치 상태면
    /// 사용자에게 설치 권유 다이얼로그를 띄운다.
    ///
    /// 흐름:
    /// 1. [InitializeOnLoad] + EditorApplication.delayCall — 컴파일 직후 1회만 실행
    /// 2. utility 설치 여부 검사 (Packages 폴더 + manifest.json 등록 둘 다 확인)
    /// 3. 미설치 + 사용자가 이전에 거절 안 함 → 다이얼로그
    /// 4. "설치" → UnityEditor.PackageManager.Client.Add(git URL), 진행 모니터링 후 결과 알림
    /// 5. "나중에" → EditorPrefs에 기록해서 다시 묻지 않음
    ///
    /// 거절 상태를 다시 묻고 싶으면: Window > UGM > Reset Utility Bootstrap Prompt
    ///
    /// UPM 의존(package.json의 dependencies)으로는 git URL 패키지끼리 자동 해결이 안 되기 때문에
    /// 이 Bootstrap이 그 갭을 메운다.
    /// </summary>
    [InitializeOnLoad]
    public static class UtilityBootstrap
    {
        private const string UTILITY_PACKAGE_NAME = "com.chopchopgames.ugm.utility";
        private const string UTILITY_GIT_URL = "https://github.com/devchop2/ugm-mod-utility.git";
        private const string DECLINED_KEY = "ugm.utility.bootstrap.declined";
        private const string LOG_PREFIX = "[UGM Bootstrap]";

        // 도메인 리로드 단위로만 한 번 실행 (의도적; 도메인 리로드 시 자동 리셋되므로 OK)
        private static bool _checkedThisDomain;
        private static AddRequest _addRequest;

        static UtilityBootstrap()
        {
            // [InitializeOnLoad]는 너무 일찍 호출되어 EditorPrefs·Package API가 불안정할 수 있음.
            // delayCall로 첫 에디터 idle 시점까지 미루는 게 안전.
            EditorApplication.delayCall += CheckAndPromptOnce;
        }

        private static void CheckAndPromptOnce()
        {
            if (_checkedThisDomain) return;
            _checkedThisDomain = true;

            // 이미 설치돼 있으면 종료
            if (IsUtilityInstalled())
            {
                // Debug.Log($"{LOG_PREFIX} Utility 이미 설치됨, 다이얼로그 생략.");
                return;
            }

            // 사용자가 이전에 "나중에"를 눌렀으면 묻지 않음
            if (EditorPrefs.GetBool(DECLINED_KEY, false))
            {
                return;
            }

            // 다이얼로그
            var choice = EditorUtility.DisplayDialog(
                "UGM Utility 설치",
                "UGM 모듈 다수가 의존하는 'UGM Utility' 패키지가 아직 설치되지 않았습니다.\n\n" +
                "포함된 기능: Logger, EventBus, ObjectPool, SaveLoad, AESCipher, Singleton 등\n\n" +
                "지금 설치하시겠어요?",
                "설치",
                "나중에");

            if (choice)
            {
                StartInstall();
            }
            else
            {
                EditorPrefs.SetBool(DECLINED_KEY, true);
                Debug.Log($"{LOG_PREFIX} Utility 설치 거절됨. 다시 묻지 않습니다.\n" +
                    $"수동 설치: Window > Package Manager > + > Add package from git URL → {UTILITY_GIT_URL}\n" +
                    $"이 메시지를 다시 보고 싶으면: Window > UGM > Reset Utility Bootstrap Prompt");
            }
        }

        private static bool IsUtilityInstalled()
        {
            // 검사 1: Packages/<name>/ 폴더 존재 (임베디드 또는 file: 경로 설치)
            if (Directory.Exists($"Packages/{UTILITY_PACKAGE_NAME}"))
                return true;

            // 검사 2: manifest.json에 등록됨 (git URL 등 외부 등록 케이스)
            const string manifestPath = "Packages/manifest.json";
            if (File.Exists(manifestPath))
            {
                try
                {
                    var content = File.ReadAllText(manifestPath);
                    if (content.Contains($"\"{UTILITY_PACKAGE_NAME}\""))
                        return true;
                }
                catch
                {
                    // manifest.json 읽기 실패는 무시 (보수적으로 미설치 처리)
                }
            }

            return false;
        }

        private static void StartInstall()
        {
            Debug.Log($"{LOG_PREFIX} Utility 설치 시작: {UTILITY_GIT_URL}");
            _addRequest = Client.Add(UTILITY_GIT_URL);
            EditorApplication.update += MonitorInstall;
        }

        private static void MonitorInstall()
        {
            if (_addRequest == null || !_addRequest.IsCompleted) return;

            EditorApplication.update -= MonitorInstall;

            if (_addRequest.Status == StatusCode.Success)
            {
                var pkg = _addRequest.Result;
                Debug.Log($"{LOG_PREFIX} Utility 설치 완료: {pkg.packageId} (v{pkg.version})");
                EditorUtility.DisplayDialog(
                    "설치 완료",
                    $"UGM Utility v{pkg.version} 설치가 완료되었습니다.\n" +
                    $"이제 모듈들이 ChopChopGames.UGM.Utility namespace를 통해 공통 유틸을 사용할 수 있습니다.",
                    "확인");
            }
            else
            {
                var errMsg = _addRequest.Error?.message ?? "(unknown)";
                Debug.LogError($"{LOG_PREFIX} Utility 설치 실패: {errMsg}");
                EditorUtility.DisplayDialog(
                    "설치 실패",
                    $"UGM Utility 설치에 실패했습니다.\n\n오류: {errMsg}\n\n" +
                    $"수동으로 Package Manager에서 git URL을 추가하세요:\n{UTILITY_GIT_URL}",
                    "확인");
            }

            _addRequest = null;
        }

        /// <summary>
        /// 사용자가 "나중에"를 눌러서 다이얼로그가 더 이상 안 뜨는 상태를 리셋.
        /// 다음 에디터 도메인 리로드 시 (또는 Unity 재시작 시) 다이얼로그가 다시 나옴.
        /// </summary>
        [MenuItem("Window/UGM/Reset Utility Bootstrap Prompt")]
        private static void ResetDeclinedState()
        {
            EditorPrefs.DeleteKey(DECLINED_KEY);
            Debug.Log($"{LOG_PREFIX} 거절 상태 리셋됨. 도메인 리로드 시 다이얼로그가 다시 나옵니다.");
        }
    }
}
