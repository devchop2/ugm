using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace ChopChopGames.UGM.EditorTools
{
    /// <summary>
    /// 모듈을 사용자 프로젝트에 설치하는 핵심 엔트리포인트.
    ///
    /// v0.2.0+ 기본 동작 (권장):
    ///   - registry 의 ModuleManifest.gitUrl 이 있으면 Unity Package Manager API (Client.Add)
    ///     로 manifest.json 의 dependencies 에 git URL 을 추가해 설치한다.
    ///   - 결과적으로 Library/PackageCache/&lt;name&gt;@&lt;hash&gt;/ 에 git fetch 되고
    ///     manifest.json 에 영구 등록되어 차후 reinstall/update 가 정상 동작.
    ///
    /// 임베디드(embedded) 마이그레이션:
    ///   - Packages/&lt;name&gt;/ 에 임베디드된 옛 설치본이 발견되면 사용자에게 안내 후
    ///     폴더 삭제 → git URL 등록 흐름을 자동 진행한다.
    ///
    /// Legacy fallback:
    ///   - gitUrl 이 비어있고 downloadUrl(zip) 만 있는 모듈은 옛 zip 추출 방식으로 설치 (임베디드).
    ///     새 모듈은 반드시 gitUrl 을 채울 것을 권장.
    /// </summary>
    public static class ModuleImporter
    {
        private const string PackagesFolder = "Packages";

        // ----------------------------------------------------------
        // Public install entry
        // ----------------------------------------------------------
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

            // 우선 git URL 방식 시도. fallback: zip 다운로드.
            if (module.HasGitUrl)
            {
                InstallViaGitUrl(module, onComplete);
            }
            else if (!string.IsNullOrEmpty(module.downloadUrl))
            {
                Debug.LogWarning(
                    $"[UGM] '{module.displayName}' 은 gitUrl 이 정의되지 않아 옛 zip 방식(임베디드 설치) 으로 설치합니다.\n" +
                    $"  registry.json 에 gitUrl 추가를 권장 (auto-update 가능 + manifest.json 등록).");
                InstallViaZip(module, onComplete);
            }
            else
            {
                onComplete?.Invoke(false, "module.gitUrl 또는 downloadUrl 이 모두 비어있습니다.");
            }
        }

        // ============================================================
        // Path A: git URL 기반 설치 (v0.2.0+ 권장)
        // ============================================================
        private static void InstallViaGitUrl(ModuleManifest module, Action<bool, string> onComplete)
        {
            // 임베디드 폴더가 남아있으면 git URL 보다 우선됨 → 사용자 동의 후 삭제 필요
            var embeddedDir = Path.Combine(PackagesFolder, module.name).Replace('\\', '/');
            if (Directory.Exists(embeddedDir))
            {
                bool yes = EditorUtility.DisplayDialog("UGM 마이그레이션 필요",
                    $"'{module.displayName}' 모듈이 임베디드 폴더 ({embeddedDir}) 에 설치되어 있습니다.\n" +
                    "git URL 방식으로 전환하려면 이 폴더를 먼저 삭제해야 합니다.\n\n" +
                    "삭제 후 manifest.json 에 git URL 을 등록하고 Unity 가 자동으로 git fetch 합니다.\n" +
                    "(이후부터는 update 도 정상 동작)\n\n" +
                    "임베디드 폴더를 삭제하고 git URL 로 전환할까요?",
                    "전환", "취소");
                if (!yes)
                {
                    onComplete?.Invoke(false, "사용자가 임베디드 → git URL 마이그레이션을 취소했습니다.");
                    return;
                }

                try
                {
                    Directory.Delete(embeddedDir, recursive: true);
                    // .meta 도 정리
                    var meta = embeddedDir + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }
                catch (Exception ex)
                {
                    var err = $"임베디드 폴더 삭제 실패: {ex.Message}\n경로: {embeddedDir}\nUnity 가 파일을 잠그고 있을 수 있습니다.";
                    Debug.LogError($"[UGM] {err}");
                    EditorUtility.DisplayDialog("UGM", err, "확인");
                    onComplete?.Invoke(false, err);
                    return;
                }
            }

            EditorUtility.DisplayProgressBar("UGM Install", $"{module.displayName}: git fetch 중...", 0.3f);
            Debug.Log($"[UGM] '{module.displayName}' git URL 등록: {module.gitUrl}");

            var addRequest = Client.Add(module.gitUrl);
            EditorApplication.update += OnAddProgress;

            void OnAddProgress()
            {
                if (!addRequest.IsCompleted) return;
                EditorApplication.update -= OnAddProgress;
                EditorUtility.ClearProgressBar();

                if (addRequest.Status == StatusCode.Success && addRequest.Result != null)
                {
                    var info = addRequest.Result;
                    Debug.Log($"[UGM] '{module.displayName}' git URL 설치 완료 — name:{info.name}, version:{info.version}, source:{info.source}");
                    onComplete?.Invoke(true, $"git: {info.packageId}");
                }
                else
                {
                    var err = addRequest.Error?.message ?? "(unknown UPM error)";
                    Debug.LogError($"[UGM] git URL 설치 실패: {err}\n  URL: {module.gitUrl}");
                    EditorUtility.DisplayDialog("UGM",
                        $"'{module.displayName}' git URL 설치 실패\n\n사유: {err}\nURL: {module.gitUrl}\n\n" +
                        "GitHub repo 가 public 인지, 인증이 필요한지 확인하세요.",
                        "확인");
                    onComplete?.Invoke(false, err);
                }
            }
        }

        // ============================================================
        // Path B: zip 다운로드 → Packages/ 임베디드 (legacy fallback)
        // ============================================================
        private static void InstallViaZip(ModuleManifest module, Action<bool, string> onComplete)
        {
            var cacheFolder = GetCacheFolder();
            Directory.CreateDirectory(cacheFolder);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var zipPath = Path.Combine(cacheFolder, $"{module.name}-{module.version}-{ts}.zip");
            var extractRoot = Path.Combine(cacheFolder, $"{module.name}-{module.version}-{ts}");

            EditorUtility.DisplayProgressBar("UGM Install", $"{module.displayName}: 다운로드 중... (legacy zip)", 0.1f);

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
                        var err = $"zip 다운로드 실패: {req.error} (HTTP {req.responseCode})\nURL: {module.downloadUrl}";
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
                    if (Directory.Exists(dest))
                    {
                        try { Directory.Delete(dest, recursive: true); }
                        catch (Exception ex)
                        {
                            EditorUtility.ClearProgressBar();
                            var err = $"기존 패키지 폴더 정리 실패: {ex.Message}\n경로: {dest}";
                            Debug.LogError($"[UGM] {err}");
                            EditorUtility.DisplayDialog("UGM", err, "확인");
                            SafeDelete(zipPath);
                            SafeDeleteDir(extractRoot);
                            onComplete?.Invoke(false, err);
                            return;
                        }
                    }

                    try
                    {
                        Directory.CreateDirectory(dest);
                        CopyDirectoryRecursive(pkgRoot, dest);
                    }
                    catch (Exception ex)
                    {
                        EditorUtility.ClearProgressBar();
                        var err = $"패키지 복사 실패: {ex.Message}\n원본: {pkgRoot}\n대상: {dest}";
                        Debug.LogError($"[UGM] {err}");
                        EditorUtility.DisplayDialog("UGM", err, "확인");
                        SafeDeleteDir(dest);
                        onComplete?.Invoke(false, err);
                        return;
                    }

                    SafeDelete(zipPath);
                    SafeDeleteDir(extractRoot);

                    EditorUtility.ClearProgressBar();
                    AssetDatabase.Refresh();

                    Debug.Log($"[UGM] '{module.displayName}' v{module.version} legacy zip 설치 완료: {dest}");
                    onComplete?.Invoke(true, dest);
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        // ============================================================
        // 설치 상태 검사
        // ============================================================
        /// <summary>
        /// 패키지가 설치되어 있는지, 설치 방식 / 버전을 확인.
        ///
        /// 검사 우선순위:
        /// 1. Packages/&lt;name&gt;/ 임베디드 폴더 (legacy 또는 마이그레이션 미완료)
        /// 2. UPM PackageInfo (git URL / registry / local 등)
        /// </summary>
        public static InstallInfo CheckInstalled(ModuleManifest module)
        {
            if (module == null || string.IsNullOrEmpty(module.name))
                return InstallInfo.None;

            // 1. 임베디드 폴더 우선 (UPM 보다 우선순위가 더 높음)
            var dir = Path.Combine(PackagesFolder, module.name);
            var pkgJson = Path.Combine(dir, "package.json");
            if (Directory.Exists(dir) && File.Exists(pkgJson))
            {
                try
                {
                    var json = File.ReadAllText(pkgJson);
                    var pkg = JsonUtility.FromJson<MinimalPackageJson>(json);
                    return new InstallInfo
                    {
                        Installed = true,
                        InstalledVersion = pkg?.version ?? "?",
                        InstallSource = InstallSource.Embedded,
                    };
                }
                catch
                {
                    return new InstallInfo { Installed = true, InstalledVersion = "?", InstallSource = InstallSource.Embedded };
                }
            }

            // 2. UPM PackageInfo
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(module.name);
                if (info != null)
                {
                    InstallSource src;
                    switch (info.source)
                    {
                        case PackageSource.Git: src = InstallSource.Git; break;
                        case PackageSource.LocalTarball:
                        case PackageSource.Local: src = InstallSource.LocalPath; break;
                        case PackageSource.Embedded: src = InstallSource.Embedded; break;
                        default: src = InstallSource.Registry; break;
                    }
                    return new InstallInfo
                    {
                        Installed = true,
                        InstalledVersion = info.version ?? "?",
                        InstallSource = src,
                    };
                }
            }
            catch { /* UPM API 호출 실패는 무시 */ }

            return InstallInfo.None;
        }

        public enum InstallSource { Unknown, Embedded, Git, Registry, LocalPath }

        public struct InstallInfo
        {
            public bool Installed;
            public string InstalledVersion;
            public InstallSource InstallSource;
            public static InstallInfo None => new InstallInfo { Installed = false, InstalledVersion = null, InstallSource = InstallSource.Unknown };
        }

        // ============================================================
        // 헬퍼
        // ============================================================
        private static string GetCacheFolder() => Path.Combine(Path.GetTempPath(), "UGMCache");

        private static string FindPackageRoot(string extractRoot)
        {
            if (File.Exists(Path.Combine(extractRoot, "package.json"))) return extractRoot;
            try
            {
                foreach (var dir in Directory.GetDirectories(extractRoot))
                    if (File.Exists(Path.Combine(dir, "package.json"))) return dir;
            }
            catch { /* 무시 */ }
            return null;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            foreach (var subDir in Directory.GetDirectories(sourceDir))
                CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 무시 */ }
        }

        private static void SafeDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* 무시 */ }
        }

        [Serializable]
        private class MinimalPackageJson
        {
            public string version;
        }
    }
}
