using System;
using System.ServiceProcess;
using Microsoft.Win32;
using OtimizaPC.Core;

namespace OtimizaPC.Tweaks.Registry
{
    /// <summary>
    /// Altera o tipo de inicialização de um serviço pelo registro (reversível pelo mesmo mecanismo)
    /// e para/inicia o serviço na hora.
    /// </summary>
    public sealed class ServiceTweak : RegistryTweak
    {
        public const int Automatic = 2;
        public const int Manual = 3;
        public const int Disabled = 4;

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
        private readonly string _serviceName;

        public ServiceTweak(string id, string name, string description, string category, string serviceName, int startType = Disabled, int defaultStartType = Manual)
            : base(id, name, description, category)
        {
            _serviceName = serviceName;
            Changes.Add(RegistryChange.Dword(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{serviceName}", "Start", startType, defaultStartType));
        }

        public override TweakState GetState()
        {
            // Serviço inexistente nesta edição do Windows conta como "nada a fazer"
            using (var key = RegistryAccess.Open(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{_serviceName}", false))
                return key == null ? TweakState.Unknown : base.GetState();
        }

        protected override void AfterApply()
        {
            if (((int)Changes[0].Value) != Disabled) return;
            Control(service =>
            {
                if (service.Status == ServiceControllerStatus.Stopped) return;
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
            });
        }

        protected override void AfterRevert()
        {
            using (var key = RegistryAccess.Open(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{_serviceName}", false))
            {
                if (!(key?.GetValue("Start") is int start) || start != Automatic) return;
            }
            Control(service =>
            {
                if (service.Status != ServiceControllerStatus.Stopped) return;
                service.Start();
            });
        }

        private void Control(Action<ServiceController> action)
        {
            try
            {
                using (var service = new ServiceController(_serviceName))
                    action(service);
            }
            catch (Exception ex)
            {
                // A alteração do tipo de inicialização já vale a partir do próximo boot
                Log.Warn($"Não foi possível controlar o serviço {_serviceName} agora: {ex.Message}");
            }
        }
    }
}
