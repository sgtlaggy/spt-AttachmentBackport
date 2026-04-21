using System.Reflection;
using System.Text.Json;
using AttachmentBackport.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using Path = System.IO.Path;

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace AttachmentBackport.Services;

[Injectable]
public class AttachmentPatchService(
    ISptLogger<AttachmentPatchService> logger,
    DatabaseService databaseService
)
{
    private ModConfig _config = new();
    
    private void LoadConfig(string modFolder)
    {
        try
        {
            var configPath = Path.Combine(modFolder, "Data", "config.json");

            if (!File.Exists(configPath))
            {
                logger.Warning("[AttachmentBackport] config.json not found, using defaults.");
                return;
            }

            var json = File.ReadAllText(configPath);
            _config = JsonSerializer.Deserialize<ModConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ModConfig();

            DebugLog($"[AttachmentBackport] Config loaded. Debug = {_config.Debug}");
        }
        catch (Exception ex)
        {
            logger.Warning($"[AttachmentBackport] Failed to load config.json: {ex.Message}");
            _config = new ModConfig();
        }
    }
    
    private void DebugLog(string message)
    {
        if (_config.Debug)
        {
            logger.Info(message);
        }
    }
    
    public void ApplyPatches()
    {
        try
        {
            var modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            var jsonPath = Path.Combine(modFolder, "Data", "attachmentChanges.json");
            
            LoadConfig(modFolder);

            if (!File.Exists(jsonPath))
            {
                logger.Warning($"[AttachmentBackport] Patch file not found: {jsonPath}");
                return;
            }

            var json = File.ReadAllText(jsonPath);
            var patchFile = JsonSerializer.Deserialize<AttachmentPatchFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (patchFile?.Items == null || patchFile.Items.Count == 0)
            {
                DebugLog("[AttachmentBackport] No patch entries found.");
                return;
            }

            DebugLog($"[AttachmentBackport] Loaded {patchFile.Items.Count} patch entries.");

            var applied = 0;
            var missing = 0;
            var duplicateNames = 0;
            var nameFallbackUsed = 0;

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in patchFile.Items)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    DebugLog("[AttachmentBackport] Encountered patch entry with empty name.");
                    missing++;
                    continue;
                }

                if (!seenNames.Add(entry.Name))
                {
                    DebugLog($"[AttachmentBackport] Duplicate patch entry by name: {entry.Name}");
                    duplicateNames++;
                    continue;
                }

                var item = ResolveItem(entry, out var resolvedBy, out var resolvedTpl);

                if (item == null)
                {
                    DebugLog($"[AttachmentBackport] Item does not exist in database: {entry.Name} ({entry.Tpl})");
                    missing++;
                    continue;
                }

                if (resolvedBy == "name")
                {
                    nameFallbackUsed++;
                    DebugLog($"[AttachmentBackport] Resolved by locale name fallback: {entry.Name} -> {resolvedTpl}");
                }

                var propsProp = item.GetType().GetProperty("Properties", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var props = propsProp?.GetValue(item);

                if (props == null)
                {
                    DebugLog($"[AttachmentBackport] Item has null Properties: {entry.Name} ({resolvedTpl ?? entry.Tpl})");
                    missing++;
                    continue;
                }

                foreach (var change in from change in entry.Changes let success = SetPropertyValue(props, change.Key, change.Value) where !success select change)
                {
                    DebugLog($"[AttachmentBackport] Could not set '{change.Key}' on {entry.Name} ({resolvedTpl ?? entry.Tpl})");
                }

                DebugLog($"[AttachmentBackport] Patched: {entry.Name} [{resolvedBy}] ({resolvedTpl ?? entry.Tpl})");
                applied++;
            }

            logger.Info($"[AttachmentBackport] Finished. Applied: {applied}, Missing: {missing}, DuplicateNames: {duplicateNames}, NameFallbackUsed: {nameFallbackUsed}");
        }
        catch (Exception ex)
        {
            logger.Error($"[AttachmentBackport] Failed to apply patches: {ex}");
        }
    }

    private TemplateItem? ResolveItem(AttachmentPatchEntry entry, out string resolvedBy, out string? resolvedTpl)
    {
        resolvedBy = "none";
        resolvedTpl = null;

        var tables = databaseService.GetTables();
        var itemsDb = tables.Templates.Items;

        if (!string.IsNullOrWhiteSpace(entry.Tpl) && itemsDb.TryGetValue(entry.Tpl, out var itemByTpl))
        {
            resolvedBy = "tpl";
            resolvedTpl = entry.Tpl;
            return itemByTpl;
        }

        var localeTpl = FindTplByLocaleName(entry.Name);
        if (string.IsNullOrWhiteSpace(localeTpl) || !itemsDb.TryGetValue(localeTpl, out var itemByName)) return null;
        resolvedBy = "name";
        resolvedTpl = localeTpl;
        return itemByName;

    }

    private string? FindTplByLocaleName(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        var tables = databaseService.GetTables();
        var globalLocales = tables.Locales.Global;

        if (!globalLocales.TryGetValue("en", out var enLocale) || enLocale.Value == null)
            return (from locale in globalLocales.Values
                    where locale.Value != null
                    select FindTplInLocaleDictionary(locale.Value, targetName))
                .FirstOrDefault(tpl => !string.IsNullOrWhiteSpace(tpl));
        {
            var tpl = FindTplInLocaleDictionary(enLocale.Value, targetName);
            if (!string.IsNullOrWhiteSpace(tpl))
            {
                return tpl;
            }
        }

        return (from locale in globalLocales.Values where locale.Value != null select FindTplInLocaleDictionary(locale.Value, targetName)).FirstOrDefault(tpl => !string.IsNullOrWhiteSpace(tpl));
    }

    private static string? FindTplInLocaleDictionary(IDictionary<string, string> localeDict, string targetName)
    {
        return (from i in localeDict where i.Key.EndsWith(" Name", StringComparison.OrdinalIgnoreCase) where string.Equals(i.Value, targetName, StringComparison.OrdinalIgnoreCase) select i.Key[..^5]).FirstOrDefault();
    }

    private bool SetPropertyValue(object propsObject, string propertyName, JsonElement value)
    {
        var prop = propsObject.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
        );

        if (prop == null || !prop.CanWrite)
        {
            return false;
        }

        try
        {
            var converted = ConvertJsonElement(value, prop.PropertyType);
            prop.SetValue(propsObject, converted);
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning($"[AttachmentBackport] Failed setting {propertyName}: {ex.Message}");
            return false;
        }
    }

    private static object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(string))
            return element.GetString();

        if (underlyingType == typeof(int))
            return element.GetInt32();

        if (underlyingType == typeof(float))
            return element.GetSingle();

        if (underlyingType == typeof(double))
            return element.GetDouble();

        if (underlyingType == typeof(decimal))
            return element.GetDecimal();

        if (underlyingType == typeof(bool))
            return element.GetBoolean();

        if (underlyingType.IsEnum)
        {
            return element.ValueKind == JsonValueKind.String ? Enum.Parse(underlyingType, element.GetString()!, true) : Enum.ToObject(underlyingType, element.GetInt32());
        }

        var raw = element.GetRawText();
        return JsonSerializer.Deserialize(raw, underlyingType, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}

public class AttachmentPatchFile
{
    public List<AttachmentPatchEntry> Items { get; set; } = [];
}

public class AttachmentPatchEntry
{
    public string Tpl { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, JsonElement> Changes { get; set; } = new();
}