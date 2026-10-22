using System;
using OtimizaPC.Core;

namespace OtimizaPC.Tweaks
{
    /// <summary>Aplica e reverte otimizações garantindo que o estado original seja salvo antes de qualquer alteração.</summary>
    public sealed class TweakService
    {
        private readonly UndoStore _undo;

        public TweakService(UndoStore undo = null)
        {
            _undo = undo ?? new UndoStore();
        }

        public TweakState GetState(Tweak tweak)
        {
            try
            {
                return tweak.GetState();
            }
            catch (Exception ex)
            {
                Log.Warn($"Falha ao ler o estado de '{tweak.Id}': {ex.Message}");
                return TweakState.Unknown;
            }
        }

        /// <summary>Data em que o app aplicou a otimização (null se foi aplicada fora do app).</summary>
        public DateTime? AppliedByAppAt(Tweak tweak) => _undo.Get(tweak.Id)?.AppliedAt;

        public void Apply(Tweak tweak)
        {
            var record = _undo.GetOrCreate(tweak.Id);
            tweak.Prepare(record);
            _undo.Save(record); // original salvo em disco ANTES de alterar o sistema

            tweak.Apply(record);
            record.AppliedAt = record.AppliedAt ?? DateTime.Now;
            _undo.Save(record);
            History.Record("Otimização", $"Aplicada: {tweak.Name} ({tweak.Id})");
        }

        public void Revert(Tweak tweak)
        {
            var record = _undo.Get(tweak.Id);
            tweak.Revert(record);
            _undo.Remove(tweak.Id);
            History.Record("Otimização", $"Revertida: {tweak.Name} ({tweak.Id})" + (record == null ? " — padrão do Windows" : string.Empty));
        }
    }
}
