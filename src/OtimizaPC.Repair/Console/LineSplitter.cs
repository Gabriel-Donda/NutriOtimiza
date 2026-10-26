using System;
using System.Text;

namespace OtimizaPC.Repair.Console
{
    public sealed class OutputLine
    {
        public OutputLine(string text, bool isProgress, bool isError = false, int? progressPercent = null)
        {
            Text = text;
            IsProgress = isProgress;
            IsError = isError;
            ProgressPercent = progressPercent;
        }

        public string Text { get; }

        /// <summary>Linha terminada só com \r (barra de progresso): substitui a anterior em vez de acumular.</summary>
        public bool IsProgress { get; }

        public bool IsError { get; }

        /// <summary>Percentual extraído da saída da ferramenta, quando ela fornece um.</summary>
        public int? ProgressPercent { get; }
    }

    /// <summary>
    /// Separa o fluxo de caracteres em linhas. "\r" sozinho (sem "\n") marca uma linha de progresso que
    /// será sobrescrita; "\r\n" e "\r\r\n" (sfc) são quebras normais.
    /// </summary>
    internal sealed class LineSplitter
    {
        private readonly StringBuilder _current = new StringBuilder();
        private readonly Action<string, bool> _emit;
        private bool _pendingCr;

        public LineSplitter(Action<string, bool> emit)
        {
            _emit = emit;
        }

        public void Feed(char[] buffer, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var c = buffer[i];
                if (c == '\0') continue;

                if (_pendingCr)
                {
                    if (c == '\r') continue; // "\r\r\n": aguarda o \n
                    _pendingCr = false;
                    if (c == '\n')
                    {
                        Emit(false);
                        continue;
                    }
                    Emit(true);
                }

                if (c == '\r') _pendingCr = true;
                else if (c == '\n') Emit(false);
                else _current.Append(c);
            }
        }

        public void Flush()
        {
            if (_pendingCr || _current.Length > 0) Emit(false);
            _pendingCr = false;
        }

        private void Emit(bool progress)
        {
            var text = _current.ToString();
            _current.Clear();
            // Progresso vazio só limpa a linha no console; aqui não tem significado
            if (progress && text.Trim().Length == 0) return;
            _emit(text, progress);
        }
    }
}
