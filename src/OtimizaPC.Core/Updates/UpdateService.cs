using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

        public long? InstallerSize { get; set; }
    }

    public sealed class StagedInstaller
    {
        internal StagedInstaller(string path, Version version, string architecture, string checksum, long? size)
        {
            Path = path;
            Version = version;
            Architecture = architecture;
            Checksum = checksum;
            Size = size;
        }

        internal string Path { get; }
        public Version Version { get; }
        internal string Architecture { get; }
        internal string Checksum { get; }
        internal long? Size { get; }
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

        [DataMember(Name = "size")] public long? Size { get; set; }

        [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
    }

    /// <summary>
    /// Atualização via GitHub Releases. Cada versão publica "NutriOtimiza-Setup-{versão}-{x64|x86}.exe" e o
    /// respectivo ".sha256"; o instalador só é executado se o hash conferir.
    /// </summary>
    public static class UpdateService
    {
        private static readonly Regex RepositoryPattern = new Regex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

        private static readonly Regex InstallerNamePattern = new Regex(
            @"\A" + Regex.Escape(AppInfo.Name) + @"-Setup-([0-9]+(?:\.[0-9]+){1,3})-(x64|x86)\.exe\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
            var installer = assets.FirstOrDefault(a => IsInstallerNameValid(a.Name, version, architecture));
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
                InstallerSize = installer.Size,
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

        internal static bool IsInstallerNameValid(string name, Version version, string architecture)
        {
            var match = InstallerNamePattern.Match(name ?? string.Empty);
            return match.Success && version != null
                && string.Equals(match.Groups[2].Value, architecture, StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(match.Groups[1].Value, out var fileVersion)
                && Normalize(fileVersion) == Normalize(version);
        }

        private static void ValidateInstallerName(string name, Version version, string architecture)
        {
            if (!IsInstallerNameValid(name, version, architecture))
                throw new InvalidOperationException("O nome do instalador não corresponde à versão ou à arquitetura esperada.");
        }

        /// <summary>Baixa e verifica o instalador em uma pasta exclusiva da atualização.</summary>
        public static async Task<StagedInstaller> DownloadAsync(UpdateInfo update, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var version = update.Version;
            var name = update.InstallerName;
            var size = update.InstallerSize;
            var architecture = Architecture;
            ValidateInstallerName(name, version, architecture);
            if (string.IsNullOrEmpty(update.ChecksumUrl))
                throw new InvalidOperationException("Esta versão não publicou o arquivo de verificação (.sha256); a atualização não é segura.");

            cancellationToken.ThrowIfCancellationRequested();
            EnableModernTls();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
                throw new InvalidOperationException("Não foi possível localizar a pasta de dados do usuário para baixar a atualização.");

            var folder = Path.Combine(localAppData, AppInfo.Name + "-atualizacao-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(folder, name);
            var security = new DirectorySecurity();
            using (var identity = WindowsIdentity.GetCurrent())
            {
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            }

            Directory.CreateDirectory(folder, security);
            try
            {
                using (var client = CreateClient())
                {
                    string expected;
                    var checksumDownload = client.DownloadStringTaskAsync(update.ChecksumUrl);
                    using (cancellationToken.Register(client.CancelAsync))
                        expected = ParseChecksum(await checksumDownload.ConfigureAwait(false));

                    cancellationToken.ThrowIfCancellationRequested();
                    client.DownloadProgressChanged += (_, e) => progress?.Report(e.ProgressPercentage);
                    var installerDownload = client.DownloadFileTaskAsync(update.InstallerUrl, target);
                    using (cancellationToken.Register(client.CancelAsync))
                        await installerDownload.ConfigureAwait(false);

                    var installer = new StagedInstaller(target, version, architecture, expected, size);
                    using (var stream = OpenInstallerForValidation(target))
                        ValidateInstallerContents(stream, expected, size);

                    cancellationToken.ThrowIfCancellationRequested();
                    Log.Info($"Atualização {version} baixada e verificada: {target}");
                    return installer;
                }
            }
            catch
            {
                DeleteStaging(target);
                throw;
            }
        }

        /// <summary>
        /// Revalida e executa o instalador em modo silencioso. Com <paramref name="relaunch"/>, o app reabre ao final.
        /// </summary>
        public static void LaunchInstaller(StagedInstaller installer, bool relaunch)
        {
            try
            {
                var name = Path.GetFileName(installer.Path);
                if (installer.Architecture != Architecture || (installer.Architecture == "x64" && !Environment.Is64BitOperatingSystem))
                    throw new InvalidOperationException("A arquitetura do instalador é incompatível com a deste aplicativo ou do Windows.");

                ValidateInstallerName(name, installer.Version, installer.Architecture);
                var arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS" + (relaunch ? string.Empty : " /NOLAUNCH");
                var startInfo = new ProcessStartInfo(installer.Path, arguments) { UseShellExecute = true };
                using (var stream = OpenInstallerForValidation(installer.Path))
                {
                    ValidateInstallerContents(stream, installer.Checksum, installer.Size);
                    // O handle bloqueia escrita e exclusão do arquivo durante a validação e a partida.
                    // O Windows ainda resolve o caminho para executar e o instalador não tem assinatura.
                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null)
                            throw new InvalidOperationException("Não foi possível iniciar o instalador da atualização.");
                    }
                }

                History.Record("Atualização", "Instalando " + name + (relaunch ? string.Empty : " ao fechar o app"));
            }
            catch
            {
                DiscardInstaller(installer);
                throw;
            }
        }

        public static void DiscardInstaller(StagedInstaller installer)
        {
            if (installer != null)
                DeleteStaging(installer.Path);
        }

        private static void DeleteStaging(string path)
        {
            var folder = Path.GetDirectoryName(path);
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0)
                    File.Delete(path);

                Directory.Delete(folder);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Falha ao limpar a pasta da atualização: " + ex.Message);
            }
        }

        internal static FileStream OpenInstallerForValidation(string path)
        {
            ValidateInstallerPath(path);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                ValidateInstallerPath(path);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private static void ValidateInstallerPath(string path)
        {
            if ((File.GetAttributes(Path.GetDirectoryName(path)) & FileAttributes.ReparsePoint) != 0
                || (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new InvalidOperationException("O caminho do instalador contém um link ou não aponta para um arquivo comum.");
        }

        internal static void ValidateInstallerContents(Stream stream, string expectedChecksum, long? expectedSize)
        {
            if (expectedSize.HasValue && stream.Length != expectedSize.Value)
                throw new InvalidOperationException("O tamanho do instalador não confere com o publicado. A atualização foi cancelada.");

            if (!string.Equals(expectedChecksum, ComputeSha256(stream), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O arquivo baixado não confere com o hash publicado. A atualização foi cancelada por segurança.");
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
            using (var stream = File.OpenRead(path))
                return ComputeSha256(stream);
        }

        private static string ComputeSha256(Stream stream)
        {
            using (var sha = SHA256.Create())
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
