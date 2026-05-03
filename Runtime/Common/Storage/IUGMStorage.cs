namespace ChopChopGames.UGM.Common.Storage
{
    /// <summary>
    /// UGM 모듈들이 키-값 데이터를 저장할 때 쓸 수 있는 추상 인터페이스.
    /// 구현체는 PlayerPrefs / EditorPrefs / 파일 / 메모리 등 자유.
    /// 모듈은 IUGMStorage에만 의존하고, 어떤 구현체를 쓸지는 게임 측에서 결정.
    /// </summary>
    public interface IUGMStorage
    {
        bool HasKey(string key);

        string GetString(string key, string defaultValue = "");
        void SetString(string key, string value);

        int GetInt(string key, int defaultValue = 0);
        void SetInt(string key, int value);

        float GetFloat(string key, float defaultValue = 0f);
        void SetFloat(string key, float value);

        void DeleteKey(string key);
        void Save();
    }
}
