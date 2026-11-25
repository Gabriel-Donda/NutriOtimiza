using System;
using System.Collections.Generic;

namespace OtimizaPC.Repair.Console
{
    public sealed class ConsoleOutputBuffer
    {
        private readonly object _gate = new object();
        private List<OutputLine> _pending = new List<OutputLine>();

        public void Add(OutputLine line)
        {
            lock (_gate) _pending.Add(line);
        }

        public IReadOnlyList<OutputLine> Drain()
        {
            lock (_gate)
            {
                if (_pending.Count == 0) return Array.Empty<OutputLine>();

                var batch = _pending;
                _pending = new List<OutputLine>();
                return batch;
            }
        }
    }
}
