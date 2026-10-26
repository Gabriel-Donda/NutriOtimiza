using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Core;
using OtimizaPC.Repair.Console;

namespace OtimizaPC.Repair.Tools
{
    public sealed class RepairResult
    {
        public bool Success { get; set; }

        public string Summary { get; set; }
    }

    /// <summary>Ferramenta de reparo com saída ao vivo (SFC, DISM, reset de rede etc.).</summary>
    public abstract class RepairTool
    {
        protected RepairTool(string id, string name, string description, string group)
        {
            Id = id;
            Name = name;
            Description = description;
            Group = group;
        }

        public string Id { get; }

        public string Name { get; }

        public string Description { get; }

        public string Group { get; }

        public bool RequiresAdmin { get; set; } = true;

        /// <summary>Texto como "5 a 15 minutos".</summary>
        public string Duration { get; set; }

        public bool RequiresRestart { get; set; }

        /// <summary>Interromper no meio pode deixar o sistema num estado incompleto.</summary>
        public bool UnsafeToCancel { get; set; }

        /// <summary>Altera o sistema de forma perceptível (ex.: derruba a rede): pede confirmação antes.</summary>
        public bool NeedsConfirmation { get; set; }

        public abstract Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken cancellationToken);

        protected static string System32(string tool)
        {
            ValidateSystem32ToolName(tool);
            var systemDirectory = Path.GetFullPath(Environment.SystemDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(systemDirectory, tool));
            if (!candidate.StartsWith(systemDirectory, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A ferramenta deve permanecer dentro de System32.", nameof(tool));
            return candidate;
        }

        internal static void ValidateSystem32ToolName(string tool)
        {
            if (string.IsNullOrWhiteSpace(tool) || Path.IsPathRooted(tool)
                || tool.IndexOfAny(new[] { '\\', '/' }) >= 0
                || !string.Equals(Path.GetFileName(tool), tool, StringComparison.Ordinal)
                || tool == "." || tool == "..")
                throw new ArgumentException("O nome da ferramenta deve ser um arquivo simples de System32.", nameof(tool));
        }
    }

    public sealed class CommandStep
    {
        public CommandStep(string tool, string arguments, string title = null)
        {
            RepairTool.ValidateSystem32ToolName(tool);
            Tool = tool;
            Arguments = Environment.ExpandEnvironmentVariables(arguments);
            Title = title;
        }

        public string Tool { get; }

        public string Arguments { get; }

        public string Title { get; }

        /// <summary>Códigos de saída considerados sucesso (padrão: só 0).</summary>
        public int[] SuccessCodes { get; set; } = { 0 };
    }

    /// <summary>Ferramenta formada por uma sequência de comandos do System32.</summary>
    public sealed class CommandTool : RepairTool
    {
        private readonly IReadOnlyList<CommandStep> _steps;
        private readonly Func<IReadOnlyList<string>, bool, string> _summarize;

        public CommandTool(string id, string name, string description, string group, IReadOnlyList<CommandStep> steps,
            Func<IReadOnlyList<string>, bool, string> summarize = null)
            : base(id, name, description, group)
        {
            _steps = steps;
            _summarize = summarize;
        }

        public override async Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken cancellationToken)
        {
            var allLines = new List<string>();
            var success = true;

            for (var stepIndex = 0; stepIndex < _steps.Count; stepIndex++)
            {
                var step = _steps[stepIndex];
                cancellationToken.ThrowIfCancellationRequested();
                var stepStart = stepIndex * 100 / _steps.Count;
                output(new OutputLine($"> {step.Title ?? step.Tool} {step.Arguments}", false, false, stepStart));

                var code = await ProcessStreamer.RunAsync(System32(step.Tool), step.Arguments, line =>
                {
                    if (!line.IsProgress)
                        lock (allLines) allLines.Add(line.Text);
                    output(line.ProgressPercent.HasValue
                        ? new OutputLine(line.Text, line.IsProgress, line.IsError,
                            stepStart + line.ProgressPercent.Value * 100 / _steps.Count / 100)
                        : line);
                }, cancellationToken).ConfigureAwait(false);

                var ok = Array.IndexOf(step.SuccessCodes, code) >= 0;
                output(new OutputLine(ok ? $"Concluído (código {code})." : $"Terminou com código {code}.", false, !ok));
                output(new OutputLine(string.Empty, false));
                success &= ok;
            }

            var summary = _summarize?.Invoke(allLines, success)
                          ?? (success ? "Concluído sem erros." : "Terminou com erros. Veja a saída acima.");
            History.Record("Reparo", $"{Name}: {summary}");
            return new RepairResult { Success = success, Summary = summary };
        }
    }
}
