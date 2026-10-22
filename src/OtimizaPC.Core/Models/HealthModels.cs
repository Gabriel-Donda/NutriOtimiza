using System.Collections.Generic;

namespace OtimizaPC.Core.Models
{
    public enum HealthStatus
    {
        Ok,
        Info,
        Warning,
        Critical,
    }

    /// <summary>Resultado de uma verificação de saúde do sistema.</summary>
    public sealed class HealthCheck
    {
        public HealthCheck(string title, string detail, HealthStatus status)
        {
            Title = title;
            Detail = detail;
            Status = status;
        }

        public string Title { get; }

        public string Detail { get; }

        public HealthStatus Status { get; }
    }

    public sealed class HealthResult
    {
        public List<HealthCheck> Checks { get; } = new List<HealthCheck>();

        /// <summary>Nota de 0 a 100.</summary>
        public int Score { get; set; }
    }
}
