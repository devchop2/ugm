using System;
using System.Collections.Generic;

namespace UGM.Editor
{
    /// <summary>
    /// registry.json 의 단일 모듈 항목.
    /// </summary>
    [Serializable]
    public class ModuleManifest
    {
        public string id;
        public string displayName;
        public string version;
        public string description;
        /// <summary>
        /// ugm-modules 저장소 내부의 모듈 폴더 경로. 예: "modules/PlayerController"
        /// </summary>
        public string path;
        /// <summary>
        /// 모듈 폴더(`path`)를 기준으로 한 상대 파일 경로 목록. 예: "Scripts/Hello.cs"
        /// </summary>
        public List<string> files = new List<string>();
        /// <summary>
        /// 함께 임포트되어야 하는 다른 UGM 모듈 id 목록.
        /// </summary>
        public List<string> dependencies = new List<string>();
        /// <summary>
        /// 필요한 외부 SDK 식별자 목록. (M5 이후 사용)
        /// </summary>
        public List<string> sdks = new List<string>();
    }

    /// <summary>
    /// registry.json 최상위 모델.
    /// </summary>
    [Serializable]
    public class Registry
    {
        public int schemaVersion = 1;
        public string updatedAt;
        public List<ModuleManifest> modules = new List<ModuleManifest>();
    }
}
