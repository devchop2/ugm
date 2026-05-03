using System;
using System.Collections.Generic;

namespace ChopChopGames.UGM.EditorTools
{
    /// <summary>
    /// registry.json 의 단일 모듈 항목 (schemaVersion 2).
    ///
    /// v1과의 차이:
    /// - 옛 path/files 배열 제거 — UPM 패키지 전체를 zip으로 받아 Packages/ 에 푸는 방식이라 불필요.
    /// - name (UPM 패키지 식별자) 추가 — Packages/ 아래 폴더명·UPM 의존성 해석에 사용.
    /// - downloadUrl 추가 — GitHub Release zip URL.
    /// </summary>
    [Serializable]
    public class ModuleManifest
    {
        /// <summary>UGM 카탈로그용 짧은 식별자. URL slug처럼 쓰임. 예: "googlesheettable"</summary>
        public string id;

        /// <summary>UPM 패키지 이름. Packages/ 안 폴더명. 예: "com.chopchopgames.ugm.googlesheettable"</summary>
        public string name;

        /// <summary>창에 표시되는 사람 친화적 이름. 예: "Google Sheet Table"</summary>
        public string displayName;

        /// <summary>SemVer 버전. 예: "0.1.0"</summary>
        public string version;

        /// <summary>한국어/영어 자유 — 모듈 설명. 길어도 됨.</summary>
        public string description;

        /// <summary>GitHub Release zip 등 직접 다운로드 가능한 URL. UnityWebRequest로 받음.</summary>
        public string downloadUrl;

        /// <summary>v1 호환용 — legacy 모듈은 raw 폴더 경로를 가짐. 새 모듈은 null.</summary>
        public string legacyPath;

        /// <summary>모듈의 README 또는 GitHub 페이지 URL. UI에서 외부 링크로 노출.</summary>
        public string documentationUrl;

        /// <summary>
        /// 본 모듈이 의존하는 다른 UGM 모듈의 id 목록 (정보용).
        /// 실제 UPM 의존 해석은 다운로드된 패키지의 package.json이 담당하므로 여기는 UI 표시 용도.
        /// </summary>
        public List<string> dependencies = new List<string>();

        /// <summary>
        /// schemaVersion 2 항목인지 빠르게 판별. v1 항목은 name/downloadUrl이 비어있고 legacyPath만 채워짐.
        /// </summary>
        public bool IsLegacyV1 =>
            string.IsNullOrEmpty(name) || string.IsNullOrEmpty(downloadUrl);
    }

    /// <summary>
    /// registry.json 최상위 모델.
    /// </summary>
    [Serializable]
    public class Registry
    {
        public int schemaVersion = 2;
        public string updatedAt;
        public List<ModuleManifest> modules = new List<ModuleManifest>();
    }
}
