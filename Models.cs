using Microsoft.Win32;

namespace PC_Medic;

internal enum StartupKind
{
    RegistryRun,
    StartupFolder,
}

internal enum Impact
{
    Low,
    Medium,
    High,
}

internal sealed class StartupEntry
{
    public required string Name { get; init; }

    public string Command { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public required string Source { get; init; }

    public StartupKind Kind { get; init; }

    public bool MachineWide { get; init; }

    public RegistryHive Hive { get; init; } = RegistryHive.CurrentUser;

    public RegistryView View { get; init; } = RegistryView.Default;

    public string KeyPath { get; init; } = string.Empty;

    public string ApprovedPath { get; init; } = string.Empty;

    public string ValueName { get; init; } = string.Empty;

    public string FolderFile { get; init; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public Impact EstimatedImpact { get; init; }

    public string ImpactLabel => EstimatedImpact switch
    {
        Impact.High => "HIGH",
        Impact.Medium => "MED",
        _ => "LOW",
    };

    public Color ImpactTone => EstimatedImpact switch
    {
        Impact.High => Theme.Bad,
        Impact.Medium => Theme.Warn,
        _ => Theme.TextDim,
    };
}

internal sealed class ServiceEntry
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string ImagePath { get; init; } = string.Empty;

    public bool Delayed { get; set; }

    public bool ThirdParty { get; init; }
}

internal readonly record struct PowerStep(string Label, bool Ok, string Detail);
