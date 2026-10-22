using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OtimizaPC.Core.Updates
{
    public sealed class UpdateInfo
    {
        public Version Version { get; set; }

        public string Notes { get; set; }

        public string PageUrl { get; set; }

        public string InstallerName { get; set; }

        public string InstallerUrl { get; set; }

        public string ChecksumUrl { get; set; }
    }

    [DataContract]
    internal sealed class GitHubRelease
    {
        [DataMember(Name = "tag_name")] public string TagName { get; set; }

        [DataMember(Name = "body")] public string Body { get; set; }

        [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }

        [DataMember(Name = "draft")] public bool Draft { get; set; }

        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }

        [DataMember(Name = "assets")] public List<GitHubAsset> Assets { get; set; }
    }

    [DataContract]
    internal sealed class GitHubAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }

        [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
    }

    /// <summary>
    /// Atualização via GitHub Releases. Cada versão publica "NutriOtimiza-Setup-{versão}-{x64|x86}.exe" e o
    /// respectivo ".sha256"; o instalador só é executado se o hash conferir.
    /// </summary>
    public static class UpdateService
    {
        private static readonly Regex RepositoryPattern = new Regex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

        public static string Architecture => Environment.Is64BitProcess ? "x64" : "x86";

        public static bool IsValidRepository(string repository) =>
            !string.IsNullOrWhiteSpace(repository) && RepositoryPattern.IsMatch(repository.Trim());

        /// <summary>Consulta a última versão publicada; null quando não há versão mais nova.</summary>
        public static async Task<UpdateInfo> CheckAsync(string repository, CancellationToken cancellationToken)
        {
            if (!IsValidRepository(repository))
                throw new InvalidOperationException("Repositório de atualizações não configurado.");

            EnableModernTls();
            using (var client = CreateClient())
            using (cancellationToken.Register(client.CancelAsync))
            {
                string json;
                try
                {
                    json = await client.DownloadStringTaskAsync($"https://api.github.com/repos/{repository.Trim()}/releases/latest").ConfigureAwait(false);
                }
                catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException("Repositório não encontrado ou ainda sem versões publicadas.", ex);
                }
                catch (WebException ex) when (ex.Status != WebExceptionStatus.RequestCanceled)
                {
                    throw new InvalidOperationException("Sem conexão com o GitHub. Verifique a internet e tente de novo.", ex);
                }
                return ParseRelease(json, Architecture, CurrentVersion());
            }
        }

        internal static UpdateInfo ParseRelease(string json, string architecture, Version current)
        {
            GitHubRelease release;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                release = (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream);

            if (release == null || release.Draft || release.Prerelease) return null;
            var version = ParseVersion(release.TagName);
            if (version == null || version <= current) return null;

            var assets = release.Assets ?? new List<GitHubAsset>();
            var installer = assets.FirstOrDefault(a =>
                a.Name != null
                && a.Name.StartsWith(AppInfo.Name + "-Setup-", StringComparison.OrdinalIgnoreCase)
                && a.Name.EndsWith($"-{architecture}.exe", StringComparison.OrdinalIgnoreCase));
            if (installer == null) return null;

            var checksum = assets.FirstOrDefault(a => string.Equals(a.Name, installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
            return new UpdateInfo
            {
                Version = version,
                Notes = release.Body,
                PageUrl = release.HtmlUrl,
                InstallerName = installer.Name,
                InstallerUrl = installer.DownloadUrl,
                ChecksumUrl = checksum?.DownloadUrl,
            };
        }

        /// <summary>Aceita "v1.2.3", "1.2.3" e "NutriOtimiza 1.2".</summary>
        internal static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var match = Regex.Match(tag, @"\d+(\.\d+){1,3}");
            return match.Success && Version.TryParse(match.Value, out var version) ? Normalize(version) : null;
        }

        public static Version CurrentVersion() => Normalize(typeof(UpdateService).Assembly.GetName().Version);

        // 1.2 e 1.2.0.0 são a mesma versão
        private static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

        /// <summary>Baixa o instalador e confere o SHA-256 publicado. Retorna o caminho do arquivo verificado.</summary>
        public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(update.ChecksumUrl))
                throw new InvalidOperationException("Esta versão não publicou o arquivo de verificação (.sha256); a atualização não é segura.");

            EnableModernTls();
            var folder = Path.Combine(Path.GetTempPath(), AppInfo.Name + "-atualizacao");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, Path.GetFileName(update.InstallerName));

            using (var client = CreateClient())
            using (cancellationToken.Register(client.CancelAsync))
            {
                var expected = ParseChecksum(await client.DownloadStringTaskAsync(update.ChecksumUrl).ConfigureAwait(false));
                client.DownloadProgressChanged += (_, e) => progress?.Report(e.ProgressPercentage);
                await client.DownloadFileTaskAsync(update.InstallerUrl, target).ConfigureAwait(false);

                var actual = ComputeSha256(target);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(target);
                    throw new InvalidOperationException("O arquivo baixado não confere com o hash publicado. A atualização foi cancelada por segurança.");
                }
            }

            Log.Info($"Atualização {update.Version} baixada e verificada: {target}");
            return target;
        }

        /// <summary>Executa o instalador em modo silencioso; ele fecha este app, atualiza e o abre de novo.</summary>
        public static void LaunchInstaller(string installerPath)
        {
            History.Record("Atualização", "Instalando " + Path.GetFileName(installerPath));
            Process.Start(new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
        }

        /// <summary>Formato do sha256sum: "hash  nome-do-arquivo" (ou só o hash).</summary>
        internal static string ParseChecksum(string content)
        {
            var hash = (content ?? string.Empty).Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (hash == null || !Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$"))
                throw new InvalidOperationException("Arquivo de verificação inválido.");
            return hash.ToLowerInvariant();
        }

        public static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }

        private static WebClient CreateClient()
        {
            var client = new WebClient { Encoding = Encoding.UTF8 };
            // A API do GitHub exige User-Agent
            client.Headers[HttpRequestHeader.UserAgent] = $"{AppInfo.Name}/{AppInfo.Version}";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        /// <summary>No Windows 8.1 / Server 2012 R2 o .NET pode não oferecer TLS 1.2 por padrão; o GitHub exige.</summary>
        public static void EnableModernTls()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
    }
}
