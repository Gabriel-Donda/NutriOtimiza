using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OtimizaPC.Repair.Console
{
    /// <summary>
    /// Decodifica linhas de saída de ferramentas do Windows, que usam codificações diferentes quando redirecionadas:
    /// sfc = UTF-16, netsh = UTF-8, dism/ipconfig/fsutil = OEM (cp850), chkdsk = ANSI (cp1252).
    /// A escolha é feita por linha, pela codificação que produz o texto mais plausível.
    /// </summary>
    internal static class OutputDecoding
    {
        private const string PortugueseLetters = "áéíóúâêôãõçàüÁÉÍÓÚÂÊÔÃÕÇÀÜ";

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        private static readonly Lazy<Encoding> Oem = new Lazy<Encoding>(() => SafeEncoding(GetOEMCP(), 850));
        private static readonly Lazy<Encoding> Ansi = new Lazy<Encoding>(() => SafeEncoding(GetACP(), 1252));

        /// <summary>Latin-1 mapeia byte↔char 1:1, permitindo separar linhas antes de decidir a codificação.</summary>
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        /// <summary>UTF-16 tem um byte zero a cada caractere ASCII.</summary>
        public static bool LooksLikeUtf16(byte[] buffer, int count)
        {
            var sample = Math.Min(count, 128);
            if (sample < 4) return false;
            var zeros = 0;
            for (var i = 0; i < sample; i++)
                if (buffer[i] == 0) zeros++;
            return zeros >= sample / 4;
        }

        public static string DecodeLine(byte[] bytes)
        {
            if (bytes.All(b => b < 0x80)) return Encoding.ASCII.GetString(bytes);

            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Não é UTF-8: decide entre OEM e ANSI
            }

            var oem = Oem.Value.GetString(bytes);
            var ansi = Ansi.Value.GetString(bytes);
            return Score(oem) >= Score(ansi) ? oem : ansi;
        }

        /// <summary>Letras acentuadas do português valem mais; símbolos e caracteres de desenho de caixa contam contra.</summary>
        private static int Score(string text)
        {
            var score = 0;
            foreach (var c in text.Where(c => c >= 0x80))
            {
                if (PortugueseLetters.IndexOf(c) >= 0) score += 2;
                else if (char.IsLetter(c)) score += 1;
                else score -= 1;
            }
            return score;
        }

        private static Encoding SafeEncoding(int codePage, int fallback)
        {
            try
            {
                return Encoding.GetEncoding(codePage);
            }
            catch
            {
                return Encoding.GetEncoding(fallback);
            }
        }

        [DllImport("kernel32.dll")]
        private static extern int GetOEMCP();

        [DllImport("kernel32.dll")]
        private static extern int GetACP();
    }
}
