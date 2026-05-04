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
    /// 1. 시스템 %TEMP%/UGMCache/ 폴더에 zip 다운로드 (Unity Library 영역 바깥)
    /// 2. ZipArchive로 같은 위치에 압축 해제
    /// 3. 압축 해제된 폴더에서 package.json을 찾아 진짜 패키지 루트 식별
    ///    (GitHub Release zip은 보통 모듈 루트에 package.json, GitHub auto-archive는 wrapper 폴더 안에)
    /// 4. 기존 Packages/&lt;name&gt;/ 가 있으면 삭제
    /// 5. 추출된 패키지 내용을 Packages/&lt;name&gt;/ 으로 Copy + Delete (Move 대신 — Windows 파일 잠금 회피)
    /// 6. AssetDatabase.Refresh
    ///
    /// v1과의 차이: 옛 ModuleImporter는 module.files 배열 순회하며 raw URL로 파일을 받아
    /// Assets/UGM/&lt;id&gt;/ 에 복사했다. 이 방식은 UPM 격리·갱신·제거가 깔끔하지 않아 폐기.
    /// </summary>
    public static class ModuleImporter
    {
        private const string PackagesFolder = "Packages";

        /// <summary>
        /// 시스템 임시 폴더 안에 UGM 작업용 sub. Library/ 안에 두면 Unity가 스캔·잠금해서
        /// Directory.Move/Delete가 "Access denied"로 실패하는 이슈가 발생함.
        /// </summary>
        private static string GetCacheFolder()
        {
            return Path.Combine(Path.GetTempPath(), "UGMCache");
        }

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

            var cacheFolder = GetCacheFolder();
            Directory.CreateDirectory(cacheFolder);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var zipPath = Path.Combine(cacheFolder, $"{module.name}-{module.version}-{ts}.zip");
            var extractRoot = Path.Combine(cacheFolder, $"{module.name}-{module.version}-{ts}");

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

                    // 기존 설치본 삭제 (있으면)
                    if (Directory.Exists(dest))
                    {
                        try
                        {
                            Directory.Delete(dest, recursive: true);
                        }
                        catch (Exception ex)
                        {
                            EditorUtility.ClearProgressBar();
                            var err = $"기존 패키지 폴더를 정리하지 못했습니다: {ex.Message}\n경로: {dest}\nUnity가 파일을 잠그고 있을 수 있습니다. 잠시 후 재시도하세요.";
                            Debug.LogError($"[UGM] {err}");
                            EditorUtility.DisplayDialog("UGM", err, "확인");
                            SafeDelete(zipPath);
                            SafeDeleteDir(extractRoot);
                            onComplete?.Invoke(false, err);
                            return;
                        }
                    }

                    // Copy + Delete (Move 대신) — Windows 파일 잠금에 더 강함.
                    // Move는 같은 볼륨에서도 일부 파일이 잠겨 있으면 통째로 실패하지만,
                    // Copy는 파일별로 진행하고 마지막에 원본을 지우므로 성공률이 높다.
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
                        // 부분 복사된 dest는 손상 가능성 있어 삭제 시도
                        SafeDeleteDir(dest);
                        onComplete?.Invoke(false, err);
                        return;
                    }

                    // 임시 폴더·zip 정리 (실패해도 동작에는 영향 없음 — %TEMP%니 OS가 청소)
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

        /// <summary>
        /// 재귀 디렉터리 복사. Directory.Move의 Windows 잠금 이슈를 회피하기 위해 사용.
        /// </summary>
        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var subName = Path.GetFileName(subDir);
                var destSub = Path.Combine(destDir, subName);
                CopyDirectoryRecursive(subDir, destSub);
            }
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
