namespace OtimizaPC.Hardware.Sensors
{
    public static class SensorAvailability
    {
        public static string Message(SensorServiceState state, string error, bool isAdministrator,
            bool isPawnIoInstalled, bool hasCpuTemperature)
        {
            if (state == SensorServiceState.Failed) return "Não foi possível iniciar a leitura de sensores: " + error;
            if (state == SensorServiceState.Starting) return "Carregando sensores…";
            if (hasCpuTemperature) return null;
            if (!isAdministrator) return "Execute o aplicativo como administrador para acessar a temperatura da CPU.";
            if (!isPawnIoInstalled) return "Instale o driver PawnIO para habilitar a leitura da temperatura da CPU.";
            return "O PawnIO está instalado, mas a CPU não forneceu temperatura. Verifique os sensores ou reinicie o Windows; o hardware pode não ser compatível.";
        }
    }
}
