namespace ChopChopGames.UGM.Common
{
    /// <summary>
    /// 사용자 프로젝트에서 본 모듈의 설치 상태.
    /// UGMWindow가 카탈로그 항목 옆에 표시할 때 사용.
    /// </summary>
    public enum ModuleState
    {
        /// <summary>설치되지 않음.</summary>
        NotInstalled = 0,

        /// <summary>설치되어 있고 카탈로그의 최신 버전과 일치.</summary>
        Installed = 1,

        /// <summary>설치는 되어 있으나 카탈로그에 더 새 버전이 있음.</summary>
        Outdated = 2,

        /// <summary>설치 중 또는 다운로드 진행 중.</summary>
        Installing = 3,
    }
}
