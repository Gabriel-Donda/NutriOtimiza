using System;
using System.Runtime.InteropServices;
using System.Threading;
using OtimizaPC.Core;

namespace OtimizaPC.Cleaning.Cleaners
{
    /// <summary>Esvazia a lixeira de todas as unidades via API do Shell.</summary>
    public sealed class RecycleBinCleaner : Cleaner
    {
        public RecycleBinCleaner()
            : base("recycle-bin", "Lixeira",
                "Arquivos que você já excluiu. Depois de esvaziar, não é possível recuperá-los.",
                CleanupGroups.Windows, CleanupRisk.Moderate, requiresAdmin: false, defaultSelected: false)
        {
        }

        public override CleanupScan Scan(CancellationToken cancellationToken)
        {
            if (!Query(out var bytes, out var items))
                return new CleanupScan { Note = "Não foi possível consultar a lixeira" };
            return new CleanupScan { Bytes = bytes, ItemCount = (int)Math.Min(items, int.MaxValue) };
        }

        public override CleanResult Clean(CleanupScan scan, CancellationToken cancellationToken)
        {
            if (scan.ItemCount == 0) return new CleanResult();

            const uint flags = 0x1 | 0x2 | 0x4; // sem confirmação, sem barra de progresso, sem som
            var hr = SHEmptyRecycleBin(IntPtr.Zero, null, flags);
            if (hr != 0)
            {
                Log.Warn($"SHEmptyRecycleBin retornou 0x{hr:X8}");
                return new CleanResult { Skipped = scan.ItemCount, Note = "O Windows não permitiu esvaziar a lixeira" };
            }
            return new CleanResult { FreedBytes = scan.Bytes, Deleted = scan.ItemCount };
        }

        private static bool Query(out long bytes, out long items)
        {
            bytes = items = 0;
            // A estrutura tem empacotamento diferente em 32 e 64 bits
            if (IntPtr.Size == 8)
            {
                var info = new SHQUERYRBINFO64 { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO64)) };
                if (SHQueryRecycleBin64(null, ref info) != 0) return false;
                bytes = info.i64Size;
                items = info.i64NumItems;
            }
            else
            {
                var info = new SHQUERYRBINFO32 { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO32)) };
                if (SHQueryRecycleBin32(null, ref info) != 0) return false;
                bytes = info.i64Size;
                items = info.i64NumItems;
            }
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO64
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SHQUERYRBINFO32
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin64(string rootPath, ref SHQUERYRBINFO64 info);

        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin32(string rootPath, ref SHQUERYRBINFO32 info);

        [DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);
    }
}
