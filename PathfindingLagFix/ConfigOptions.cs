using BepInEx.Configuration;
using System;

namespace PathfindingLagFix;

internal struct ConfigOptions
{
    private static readonly ConfigOptions OnlyFixesPreset = new()
    {
        DistancePathfindingFallbackNodeSelection = DistancePathfindingFallbackNodeSelectionType.BestPathable,
    };
    private static readonly ConfigOptions VanillaPreset = new()
    {
        DistancePathfindingFallbackNodeSelection = DistancePathfindingFallbackNodeSelectionType.Vanilla,
    };

    internal static ConfigOptions CurrentOptions = OnlyFixesPreset;

    private enum ConfigPreset
    {
        OnlyFixes,
        Vanilla,
    }

    private const string presetDescription =
        "Select a preset to use as defaults for all options that change gameplay.\n" +
        "\n" +
        "OnlyFixes: Options that are intended to act solely as bug fixes and should retain the intention of the original code.\n" +
        "Vanilla: Options that completely match vanilla as much as possible.";
    private static ConfigEntry<ConfigPreset> presetOption;

    private const string distancePathfindingFallbackNodeSelectionDescription =
        "How nodes should be selected if the criteria for a distance based pathfinding operation (i.e. bracken evasion) fails.\n" +
        "\n" +
        "UsePreset: Use the option selected by the current preset.\n" +
        "BestPathable: The enemy will go to the furthest/closest node that can be reached. This is the old behavior of PathfindingLagFix, and it guarantees the bracken will not get stuck when spotted.\n" +
        "Vanilla: The enemy will attempt to go to the furthest/closest node, regardless of whether it can be reached. This will cause brackens to sometimes stutter step towards the furthest position instead of moving smoothly.\n" +
        "DontMove: The enemy will not move until it has a valid path to follow. For the bracken, this will result in it standing still and looking at the player until they look away.";
    private static ConfigEntry<DistancePathfindingFallbackNodeSelectionType> distancePathfindingFallbackNodeSelectionOption;
    internal DistancePathfindingFallbackNodeSelectionType DistancePathfindingFallbackNodeSelection;

    internal static void BindAllOptions(ConfigFile file)
    {
        presetOption = BindOption(file, "General", "Preset", ConfigPreset.OnlyFixes, presetDescription);

        distancePathfindingFallbackNodeSelectionOption = BindOption(file, "Behavior", "DistancePathfindingFallbackNodeSelection", DistancePathfindingFallbackNodeSelectionType.UsePreset, distancePathfindingFallbackNodeSelectionDescription);

        UpdateCurrentOptions();
    }

    private static ConfigEntry<T> BindOption<T>(ConfigFile file, string section, string key, T defaultValue, string description)
    {
        var configEntry = file.Bind(section, key, defaultValue, description);
        configEntry.SettingChanged += (_, _) => UpdateCurrentOptions();
        return configEntry;
    }

    private static void UpdateCurrentOptions()
    {
        CurrentOptions = presetOption.Value switch
        {
            ConfigPreset.OnlyFixes => OnlyFixesPreset,
            ConfigPreset.Vanilla => VanillaPreset,
            _ => throw new InvalidOperationException($"Unknown preset {presetOption.Value}"),
        };

        if (distancePathfindingFallbackNodeSelectionOption.Value != DistancePathfindingFallbackNodeSelectionType.UsePreset)
            CurrentOptions.DistancePathfindingFallbackNodeSelection = distancePathfindingFallbackNodeSelectionOption.Value;

        Plugin.Instance.Logger.LogInfo($"{Plugin.MOD_UNIQUE_NAME} {Plugin.MOD_VERSION} is using preset {presetOption.Value} with options:");
        Plugin.Instance.Logger.LogInfo($"    {nameof(DistancePathfindingFallbackNodeSelection)} = {CurrentOptions.DistancePathfindingFallbackNodeSelection} ({distancePathfindingFallbackNodeSelectionOption.Value})");
    }
}

enum DistancePathfindingFallbackNodeSelectionType
{
    UsePreset,
    BestPathable,
    Vanilla,
    DontMove,
}
