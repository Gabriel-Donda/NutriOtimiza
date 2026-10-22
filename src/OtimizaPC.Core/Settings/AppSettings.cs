using System;
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
        [DataMember] public ThemePreference Theme { get; set; } = ThemePreference.System;

        [DataMember] public bool CheckUpdatesOnStartup { get; set; } = true;

        /// <summary>Repositório do GitHub com as versões publicadas ("dono/nome"). Vazio = atualização desativada.</summary>
        [DataMember] public string UpdateRepository { get; set; } = string.Empty;

        [DataMember] public DateTime? LastUpdateCheck { get; set; }

        // O DataContractJsonSerializer não executa inicializadores de propriedade: valores padrão para campos ausentes
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Theme = ThemePreference.System;
            CheckUpdatesOnStartup = true;
            UpdateRepository = string.Empty;
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
                    return (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream) ?? new AppSettings();
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
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
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
