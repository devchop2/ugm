using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ChopChopGames.UGM.EditorTools
{
    /// <summary>
    /// ModuleManifest.downloadUrl 의 zip을 받아 사용자 프로젝트의 Packages/&lt;module.name&gt;/ 으로 추출해
    /// UPM 임베디드 패키지로 등록한다.
    ///
    /// 흐름:
    /// 1. 임시 폴더에 zip 다운로드 (Library/UGMCache/&lt;timestamp&gt;.zip)
    /// 2. ZipArchive로 임시 폴더에 압축 해제
    /// 3. 압축 해제된 폴더에서 package.json을 찾아 진짜 패키지 루트 식별
    ///    (GitHub Release zip은 보통 모듈 루트에 package.json이 있음)
    /// 4. 기존 Packages/&lt;name&gt;/ 가 있으면 백업 후 삭제
    /// 5. 임시 추출 폴더 → Packages/&lt;name&gt;/ 으로 원자적 이동
    /// 6. AssetDatabase.Refresh
    ///
    /// v1과의 차이: 옛 ModuleImporter는 module.files 배열 순회하며 raw URL로 파일을 받아
    /// Assets/UGM/&lt;id&gt;/ 에 복사했다. 이 방식은 UPM 격리·갱신·제거가 깔끔하지 않아 폐기.
    /// </summary>
    public static class ModuleImporter
    {
        private const string PackagesFolder = "Packages";
        private const string CacheRelative = "Library/UGMCache";

        public static void InstallAsync(ModuleManifest module, Action<bool, string> onComplete)
        {
            if (module == null)
            {
                onComplete?.Invoke(false, "module is null");
                return;
            }
            if (module.IsLegacyV1)
            {
                EditorUtility.DisplayDialog("UGM",
                    $"'{module.displayName}' 은 옛 v1 설계 모듈이라 자동 설치할 수 없습니다.\nUPM 패키지 형식으로 마이그레이션이 필요합니다.",
                    "확인");
                onComplete?.Invoke(false, "legacy v1 module");
                return;
            }
            if (string.IsNullOrEmpty(module.name))
            {
                onComplete?.Invoke(false, "module.name 이 비어있습니다 (UPM 패키지명 필요)");
                return;
            }
            if (string.IsNullOrEmpty(module.downloadUrl))
            {
                onComplete?.Invoke(false, "module.downloadUrl 이 비어있습니다");
                return;
            }

            // 캐시 폴더 준비
            Directory.CreateDirectory(CacheRelative);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var zipPath = Path.Combine(CacheRelative, $"{module.name}-{module.version}-{ts}.zip");
            var extractRoot = Path.Combine(CacheRelative, $"{module.name}-{module.version}-{ts}");

            EditorUtility.DisplayProgressBar("UGM Install", $"{module.displayName}: 다운로드 중...", 0.1f);

            var req = UnityWebRequest.Get(module.downloadUrl);
            req.downloadHandler = new DownloadHandlerFile(zipPath);
            var op = req.SendWebRequest();

            op.completed += _ =>
            {
                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        EditorUtility.ClearProgressBar();
                        var err = $"다운로드 실패: {req.error} (HTTP {req.responseCode})\nURL: {module.downloadUrl}";
                        Debug.LogError($"[UGM] {err}");
                        EditorUtility.DisplayDialog("UGM", err, "확인");
                        SafeDelete(zipPath);
                        onComplete?.Invoke(false, err);
                        return;
                    }

                    EditorUtility.DisplayProgressBar("UGM Install", $"{module.displayName}: 압축 해제 중...", 0.5f);

                    try
                    {
                        Directory.CreateDirectory(extractRoot);
                        ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);
                    }
                    catch (Exception ex)
                    {
                        EditorUtility.ClearProgressBar();
                        var err = $"압축 해제 실패: {ex.Message}";
                        Debug.LogError($"[UGM] {err}");
                        EditorUtility.DisplayDialog("UGM", err, "확인");
                        SafeDelete(zipPath);
                        SafeDeleteDir(extractRoot);
                        onComplete?.Invoke(false, err);
                        return;
                    }

                    // 추출된 폴더 안에서 package.json 위치 찾기 (GitHub Release zip은 보통 루트에)
                    var pkgRoot = FindPackageRoot(extractRoot);
                    if (string.IsNullOrEmpty(pkgRoot))
                    {
                        EditorUtility.ClearProgressBar();
                        var err = $"zip 안에서 package.json을 찾을 수 없습니다: {extractRoot}";
                        Debug.LogError($"[UGM] {err}");
                        EditorUtility.DisplayDialog("UGM", err, "확인");
                        SafeDelete(zipPath);
                        SafeDeleteDir(extractRoot);
                        onComplete?.Invoke(false, err);
                        return;
                    }

                    EditorUtility.DisplayProgressBar("UGM Install", $"{module.displayName}: 설치 중...", 0.85f);

                    var dest = Path.Combine(PackagesFolder, module.name).Replace('\\', '/');

                    // 기존 설치본 백업·삭제
                    if (Directory.Exists(dest))
                    {
                        var backup = dest + ".bak-" + ts;
                        try
                        {
                            Directory.Move(dest, backup);
                            Debug.Log($"[UGM] 기존 설치본 백업: {backup}");
                            // 백업 즉시 삭제 (롤백 필요시 위쪽에서 catch로 처리)
                            try { Directory.Delete(backup, recursive: true); } catch { /* 무시 */ }
                        }
                        catch (Exception ex)
                        {
                            EditorUtility.ClearProgressBar();
                            var err = $"기존 패키지 폴더를 정리하지 못했습니다: {ex.Message}";
                            Debug.LogError($"[UGM] {err}");
                            EditorUtility.DisplayDialog("UGM", err, "확인");
                            onComplete?.Invoke(false, err);
                            return;
                        }
                    }

                    try
                    {
                        Directory.Move(pkgRoot, dest);
                    }
                    catch (Exception ex)
                    {
                        EditorUtility.ClearProgressBar();
                        var err = $"패키지 이동 실패: {ex.Message}";
                        Debug.LogError($"[UGM] {err}");
                        EditorUtility.DisplayDialog("UGM", err, "확인");
                        onComplete?.Invoke(false, err);
                        return;
                    }

                    // 임시 폴더·zip 정리
                    SafeDelete(zipPath);
                    SafeDeleteDir(extractRoot);

                    EditorUtility.ClearProgressBar();
                    AssetDatabase.Refresh();

                    Debug.Log($"[UGM] '{module.displayName}' v{module.version} 설치 완료: {dest}");
                    onComplete?.Invoke(true, dest);
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        /// <summary>
        /// 추출된 폴더 안에서 package.json을 가진 폴더를 찾아 반환.
        /// GitHub auto-archive는 보통 'reponame-tag/' 같은 1단계 wrapper 폴더가 있고 그 안에 package.json이 있다.
        /// 사용자가 명시적으로 만든 zip은 보통 루트에 바로 package.json.
        /// </summary>
        private static string FindPackageRoot(string extractRoot)
        {
            if (File.Exists(Path.Combine(extractRoot, "package.json")))
                return extractRoot;

            try
            {
                foreach (var dir in Directory.GetDirectories(extractRoot))
                {
                    if (File.Exists(Path.Combine(dir, "package.json")))
                        return dir;
                }
            }
            catch { /* 무시 */ }

            return null;
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 무시 */ }
        }

        private static void SafeDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* 무시 */ }
        }

        /// <summary>
        /// 사용자 프로젝트에 본 모듈이 설치돼 있는지, 어떤 버전인지 확인.
        /// </summary>
        public static InstallInfo CheckInstalled(ModuleManifest module)
        {
            if (module == null || string.IsNullOrEmpty(module.name))
                return InstallInfo.None;

            var dir = Path.Combine(PackagesFolder, module.name);
            var pkgJson = Path.Combine(dir, "package.json");
            if (!Directory.Exists(dir) || !File.Exists(pkgJson))
                return InstallInfo.None;

            try
            {
                var json = File.ReadAllText(pkgJson);
                var pkg = JsonUtility.FromJson<MinimalPackageJson>(json);
                return new InstallInfo
                {
                    Installed = true,
                    InstalledVersion = pkg?.version ?? "?",
                };
            }
            catch
            {
                return new InstallInfo { Installed = true, InstalledVersion = "?" };
            }
        }

        public struct InstallInfo
        {
            public bool Installed;
            public string InstalledVersion;
            public static InstallInfo None => new InstallInfo { Installed = false, InstalledVersion = null };
        }

        [Serializable]
        private class MinimalPackageJson
        {
            public string version;
        }
    }
}
