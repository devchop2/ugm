using System;
using UnityEngine;
using UnityEngine.Networking;

namespace ChopChopGames.UGM.EditorTools
{
    /// <summary>
    /// GitHub raw URL에서 registry.json을 받아와 Registry 객체로 역직렬화한다.
    /// 캐시 우회를 위해 timestamp 쿼리를 자동으로 붙인다.
    /// </summary>
    public static class RegistryClient
    {
        public static void FetchAsync(string registryUrl, Action<Registry> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrEmpty(registryUrl))
            {
                onError?.Invoke("REGISTRY_URL 이 비어있습니다.");
                return;
            }

            var url = AppendCacheBuster(registryUrl);
            var req = UnityWebRequest.Get(url);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        onError?.Invoke($"{req.error} (HTTP {req.responseCode})");
                        return;
                    }

                    var json = req.downloadHandler.text;
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        onError?.Invoke("응답 본문이 비어있습니다.");
                        return;
                    }

                    Registry registry;
                    try
                    {
                        registry = JsonUtility.FromJson<Registry>(json);
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"JSON 파싱 실패: {e.Message}");
                        return;
                    }

                    if (registry == null)
                    {
                        onError?.Invoke("Registry 가 null 입니다. JSON 형식을 확인하세요.");
                        return;
                    }

                    onSuccess?.Invoke(registry);
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        private static string AppendCacheBuster(string url)
        {
            var separator = url.Contains("?") ? "&" : "?";
            return url + separator + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
