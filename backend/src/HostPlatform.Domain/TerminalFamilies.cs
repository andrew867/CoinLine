namespace HostPlatform.Domain;

public sealed record TerminalFamily(string Id, string Release, string Variant, IReadOnlyList<int> MaximizerTypeNumbers)
{
    public string Label => $"{Release} {Variant}";
}

/// <summary>Inventory families identified by the desktop terminal type catalogue.</summary>
public static class TerminalFamilies
{
    public static IReadOnlyList<TerminalFamily> Catalog { get; } =
    [
        new("msr16-card", "MSR 1.6", "Card", [8]),
        new("msr16-multipay", "MSR 1.6", "Multi-pay", [9]),
        new("msr16-desk", "MSR 1.6", "Desk", [11]),
        new("msr17-card", "MSR 1.7", "Card", [12]),
        new("msr17-card-generic", "MSR 1.7", "Generic Card", [23]),
        new("msr17-multipay", "MSR 1.7", "Multi-pay", [13]),
        new("msr17-multipay-data", "MSR 1.7", "Multi-pay with data jack", [14]),
        new("msr17-multipay-generic", "MSR 1.7", "Generic 2 Multi-pay", [24]),
        new("msr17-multipay-generic-data", "MSR 1.7", "Generic 2 Multi-pay with data jack", [26]),
        new("msr17-desk", "MSR 1.7", "Desk", [15]),
        new("msr17-desk-generic", "MSR 1.7", "Generic 2 Desk", [25]),
        new("msr17-inmate", "MSR 1.7i", "Inmate", [16]),
        new("msr17-coin-basic", "MSR 1.7", "Coin Basic", [19]),
        new("msr18-audiogram", "MSR 1.8", "Multi-pay Audio-Gram", [18]),
        new("mtr19-card", "MTR 1.9", "Card", [27]),
        new("mtr19-multipay", "MTR 1.9", "Multi-pay", [28]),
        new("mtr19-desk", "MTR 1.9", "Desk", [29]),
        new("mtr19-coin-basic", "MTR 1.9", "Coin Basic", [31]),
        new("r1-international-multipay", "R1 International", "Multi-pay", [32]),
        new("r1-international-desk", "R1 International", "Desk", [33]),
        new("mtr111-card", "MTR 1.11", "Card", [43]),
        new("mtr111-multipay", "MTR 1.11", "Multi-pay", [44]),
        new("mtr111-desk", "MTR 1.11", "Desk", [45]),
        new("mtr111-coin-basic", "MTR 1.11", "Coin Basic", [45, 49]),
        new("mtr20-card", "MTR 2.0", "Card", [35]),
        new("mtr20-multipay", "MTR 2.0", "Multi-pay", [36]),
        new("mtr20-desk", "MTR 2.0", "Desk", [37]),
        new("mtr20-coin-basic", "MTR 2.0", "Coin Basic", [38]),
        new("mtr20-card-graphical", "MTR 2.0", "Graphical Card", [39]),
        new("mtr20-multipay-graphical", "MTR 2.0", "Graphical Multi-pay", [40]),
        new("mtr21-visa", "MTR 2.1", "Visa", [21]),
        new("mtr22-mondex", "MTR 2.2", "Mondex", [22]),
        new("mtr23-smartcity", "MTR 2.3", "SmartCity", [34]),
        new("mtr24-proton", "MTR 2.4", "Proton", [41])
    ];

    public static TerminalFamily? Find(string? id) => Catalog.FirstOrDefault(family => family.Id == id);
}
