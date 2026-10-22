using System;
using System.Collections.Generic;

namespace OtimizaPC.Core.Models
{
    /// <summary>Par rótulo/valor exibido nas telas e no relatório.</summary>
    public sealed class InfoEntry
    {
        public InfoEntry(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }

        public string Value { get; }
    }

    /// <summary>Grupo de informações (ex.: um pente de memória, um disco).</summary>
    public sealed class InfoGroup
    {
        public InfoGroup(string header = null)
        {
            Header = header;
        }

        public string Header { get; }

        public List<InfoEntry> Entries { get; } = new List<InfoEntry>();

        /// <summary>Adiciona a entrada somente se o valor não for vazio.</summary>
        public InfoGroup Add(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                Entries.Add(new InfoEntry(label, value.Trim()));
            return this;
        }
    }

    /// <summary>Seção do relatório de hardware (ex.: Processador, Memória).</summary>
    public sealed class InfoSection
    {
        public InfoSection(string title, string icon)
        {
            Title = title;
            Icon = icon;
        }

        public string Title { get; }

        /// <summary>Nome do ícone (SymbolRegular do WPF-UI).</summary>
        public string Icon { get; }

        public List<InfoGroup> Groups { get; } = new List<InfoGroup>();

        public InfoGroup NewGroup(string header = null)
        {
            var group = new InfoGroup(header);
            Groups.Add(group);
            return group;
        }
    }

    public sealed class HardwareReport
    {
        public DateTime GeneratedAt { get; set; }

        public string MachineName { get; set; }

        public List<InfoSection> Sections { get; } = new List<InfoSection>();
    }
}
