using UnityEngine;

namespace ChopChopGames.UGM.Common.Storage
{
    /// <summary>
    /// IUGMStorage의 PlayerPrefs 기반 구현체. 런타임 게임 데이터에 사용.
    /// 플랫폼별 PlayerPrefs 위치는 Unity 매뉴얼 참고. 빌드된 게임에서 동작.
    /// </summary>
    public sealed class PlayerPrefsStorage : IUGMStorage
    {
        private readonly string _prefix;

        /// <summary>
        /// 키 충돌 방지를 위한 prefix. 기본값 "ugm." 추천.
        /// 모듈별로 prefix를 다르게 두면 모듈 간 키 격리 가능.
        /// </summary>
        public PlayerPrefsStorage(string prefix = "ugm.")
        {
            _prefix = prefix ?? string.Empty;
        }

        private string K(string key) => _prefix + key;

        public bool HasKey(string key) => PlayerPrefs.HasKey(K(key));

        public string GetString(string key, string defaultValue = "") => PlayerPrefs.GetString(K(key), defaultValue);
        public void SetString(string key, string value) => PlayerPrefs.SetString(K(key), value);

        public int GetInt(string key, int defaultValue = 0) => PlayerPrefs.GetInt(K(key), defaultValue);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(K(key), value);

        public float GetFloat(string key, float defaultValue = 0f) => PlayerPrefs.GetFloat(K(key), defaultValue);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(K(key), value);

        public void DeleteKey(string key) => PlayerPrefs.DeleteKey(K(key));
        public void Save() => PlayerPrefs.Save();
    }
}
