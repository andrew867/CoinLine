namespace HostPlatform.Firmware;

/// <summary>Blocks live flashing unless explicitly enabled via configuration after HARDWARE_VALIDATION_REQUIRED.</summary>
public static class FirmwareSafetyGate
{
    /// <summary>Legacy tests-only toggle — prefer <see cref="EnsureSimulationOrThrow(bool, bool)"/> with config-derived flag.</summary>
    public static bool AllowLiveFlashing { get; set; }

    public static void EnsureSimulationOrThrow(bool simulationMode, bool allowLiveFlashingFromConfiguration)
    {
        if (!simulationMode && !allowLiveFlashingFromConfiguration)
            throw new InvalidOperationException(
                "Live firmware flashing is disabled (SimulationMode required). HARDWARE_VALIDATION_REQUIRED — set Firmware:AllowLiveFlashing only after certified modem/XMODEM path.");
    }

    public static void EnsureSimulationOrThrow(bool simulationMode)
    {
        EnsureSimulationOrThrow(simulationMode, AllowLiveFlashing);
    }
}
