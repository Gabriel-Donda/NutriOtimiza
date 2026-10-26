using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace OtimizaPC.Core.Settings
{
    public enum ThemePreference
    {
        System,
        Light,
        Dark,
    }

    [DataContract]
    public sealed class AppSettings
    {
        /// <summary>Escuro por padrão: é o visual da marca Nutriport.</summary>
        [DataMember] public ThemePreference Theme { get; set; } = ThemePreference.Dark;

        [DataMember] public bool CheckUpdatesOnStartup { get; set; } = true;

        /// <summary>Baixa a nova versão em segundo plano e instala ao fechar o app, sem perguntar.</summary>
        [DataMember] public bool AutoInstallUpdates { get; set; } = true;

        /// <summary>Repositório do GitHub com as versões publicadas ("dono/nome"). Vazio = usa o repositório embutido no app.</summary>
        [DataMember] public string UpdateRepository { get; set; } = string.Empty;

        /// <summary>Repositório efetivo: o configurado ou, se vazio, o embutido no app.</summary>
        public string EffectiveUpdateRepository =>
            string.IsNullOrWhiteSpace(UpdateRepository) ? AppInfo.DefaultUpdateRepository : UpdateRepository.Trim();

        [DataMember] public DateTime? LastUpdateCheck { get; set; }

        /// <summary>Transições e efeitos de movimento. O app ainda os desliga sozinho com o Windows sem animações, em acesso remoto ou em PC fraco.</summary>
        [DataMember] public bool AnimationsEnabled { get; set; } = true;

        /// <summary>Identificadores dos componentes de sensores que o usuário recolheu.</summary>
        [DataMember] public List<string> CollapsedSensorGroups { get; set; } = new List<string>();

        /// <summary>Identificadores das seções de hardware que o usuário recolheu.</summary>
        [DataMember] public List<string> CollapsedHardwareSections { get; set; } = new List<string>();

        /// <summary>Identificadores dos grupos de limpeza que o usuário recolheu.</summary>
        [DataMember] public List<string> CollapsedCleanupGroups { get; set; } = new List<string>();

        // O DataContractJsonSerializer não executa inicializadores de propriedade: valores padrão para campos ausentes
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Theme = ThemePreference.Dark;
            CheckUpdatesOnStartup = true;
            AutoInstallUpdates = true;
            UpdateRepository = string.Empty;
            AnimationsEnabled = true;
            CollapsedSensorGroups = new List<string>();
            CollapsedHardwareSections = new List<string>();
            CollapsedCleanupGroups = new List<string>();
        }
    }

    /// <summary>Lê e grava as preferências em %LocalAppData%\OtimizaPC\settings.json.</summary>
    public sealed class SettingsStore
    {
        private readonly string _filePath;

        public SettingsStore(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(AppInfo.DataDirectory, "settings.json");
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new AppSettings();
                using (var stream = File.OpenRead(_filePath))
                {
                    var settings = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream) ?? new AppSettings();
                    if (settings.CollapsedSensorGroups == null) settings.CollapsedSensorGroups = new List<string>();
                    if (settings.CollapsedHardwareSections == null) settings.CollapsedHardwareSections = new List<string>();
                    if (settings.CollapsedCleanupGroups == null) settings.CollapsedCleanupGroups = new List<string>();
                    return settings;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Configurações ilegíveis; usando o padrão: " + ex.Message);
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temp = _filePath + ".tmp";
                using (var stream = File.Create(temp))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, settings);
                if (File.Exists(_filePath)) File.Replace(temp, _filePath, null);
                else File.Move(temp, _filePath);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao salvar configurações", ex);
            }
        }
    }
}
