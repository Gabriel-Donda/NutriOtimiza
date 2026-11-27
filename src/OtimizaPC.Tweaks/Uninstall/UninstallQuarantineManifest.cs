using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace OtimizaPC.Tweaks.Uninstall
{
    [DataContract]
    internal sealed class UninstallQuarantineManifest
    {
        [DataMember] public string ProgramName { get; set; }
        [DataMember] public string UserSid { get; set; }
        [DataMember] public DateTime CreatedUtc { get; set; }
        [DataMember] public List<QuarantinedLeftover> Items { get; set; }

        internal static UninstallQuarantineManifest Read(Stream stream) =>
            (UninstallQuarantineManifest)Serializer().ReadObject(stream);

        internal void Write(Stream stream) => Serializer().WriteObject(stream, this);

        private static DataContractJsonSerializer Serializer() =>
            new DataContractJsonSerializer(typeof(UninstallQuarantineManifest), new DataContractJsonSerializerSettings
            {
                DateTimeFormat = new DateTimeFormat("o")
            });
    }

    [DataContract]
    internal sealed class QuarantinedLeftover
    {
        [DataMember] public LeftoverKind Kind { get; set; }
        [DataMember] public string OriginalPath { get; set; }
        [DataMember] public string BackupName { get; set; }
        [DataMember] public RegistryHive Hive { get; set; }
        [DataMember] public RegistryView View { get; set; }
        [DataMember] public string RegistryPath { get; set; }
    }
}
