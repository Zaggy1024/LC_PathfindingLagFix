using BepInEx.Configuration;
using System;

namespace PathfindingLagFix;

internal struct ConfigOptions
{
    private static readonly ConfigOptions OnlyFixesPreset = new()
    {
    };
    private static readonly ConfigOptions VanillaPreset = new()
    {
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

    internal static void BindAllOptions(ConfigFile file)
    {
        presetOption = BindOption(file, "General", "Preset", ConfigPreset.OnlyFixes, presetDescription);

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

        Plugin.Instance.Logger.LogInfo($"{Plugin.MOD_UNIQUE_NAME} {Plugin.MOD_VERSION} is using preset {presetOption.Value} with options:");
    }
}
