using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace UGM.Editor
{
    /// <summary>
    /// ModuleManifest.files 에 명시된 파일들을 raw URL로 다운로드해서
    /// Assets/UGM/&lt;module.id&gt;/ 아래에 복사한다.
    /// M1 단계에서는 충돌 검사 없이 단순 덮어쓰기 한다. (M3 이후 충돌 처리 추가 예정)
    /// </summary>
    public static class ModuleImporter
    {
        private const string DestRoot = "Assets/UGM";

        public static void ImportAsync(string registryUrl, ModuleManifest module, Action<bool> onComplete)
        {
            if (module == null)
            {
                onComplete?.Invoke(false);
                return;
            }
            if (module.files == null || module.files.Count == 0)
            {
                EditorUtility.DisplayDialog("UGM",
                    $"'{module.displayName}' 의 files 가 비어있습니다.\nmodule.json 또는 registry.json을 확인하세요.",
                    "확인");
                onComplete?.Invoke(false);
                return;
            }

            // registryUrl 의 디렉터리를 base 로 사용한다.
            // ex) https://raw.githubusercontent.com/USER/ugm-modules/main/registry.json
            //  → https://raw.githubusercontent.com/USER/ugm-modules/main/
            var lastSlash = registryUrl.LastIndexOf('/');
            if (lastSlash < 0)
            {
                EditorUtility.DisplayDialog("UGM", "REGISTRY_URL 형식이 올바르지 않습니다.", "확인");
                onComplete?.Invoke(false);
                return;
            }
            var baseUrl = registryUrl.Substring(0, lastSlash + 1);

            var destFolder = Path.Combine(DestRoot, SanitizeId(module.id));
            Directory.CreateDirectory(destFolder);

            DownloadNext(baseUrl, module, 0, destFolder, onComplete);
        }

        private static void DownloadNext(string baseUrl, ModuleManifest module, int index, string destFolder, Action<bool> onComplete)
        {
            if (index >= module.files.Count)
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                onComplete?.Invoke(true);
                return;
            }

            var relative = module.files[index].Replace('\\', '/').TrimStart('/');
            var path = (module.path ?? string.Empty).TrimEnd('/');
            var url = baseUrl + (string.IsNullOrEmpty(path) ? string.Empty : path + "/") + relative;
            url = AppendCacheBuster(url);

            var progress = (float)index / module.files.Count;
            var cancelled = EditorUtility.DisplayCancelableProgressBar(
                "UGM Import",
                $"{module.displayName} ({index + 1}/{module.files.Count}): {relative}",
                progress);
            if (cancelled)
            {
                EditorUtility.ClearProgressBar();
                onComplete?.Invoke(false);
                return;
            }

            var req = UnityWebRequest.Get(url);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("UGM",
                            $"파일 다운로드 실패: {relative}\n{req.error} (HTTP {req.responseCode})\n\nURL: {url}",
                            "확인");
                        onComplete?.Invoke(false);
                        return;
                    }

                    try
                    {
                        var destPath = Path.Combine(destFolder, relative);
                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                        File.WriteAllBytes(destPath, req.downloadHandler.data);
                    }
                    catch (Exception e)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("UGM",
                            $"파일 저장 실패: {relative}\n{e.Message}",
                            "확인");
                        onComplete?.Invoke(false);
                        return;
                    }
                }
                finally
                {
                    req.Dispose();
                }

                DownloadNext(baseUrl, module, index + 1, destFolder, onComplete);
            };
        }

        private static string AppendCacheBuster(string url)
        {
            var separator = url.Contains("?") ? "&" : "?";
            return url + separator + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static string SanitizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = new List<char>(id.Length);
            foreach (var c in id) chars.Add(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return new string(chars.ToArray());
        }
    }
}
