using AttachmentBackport.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;
using Microsoft.Extensions.DependencyInjection;

namespace AttachmentBackport;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.rukira.attachmentbackport";
    public override string Name { get; init; } = "AttachmentBackport";
    public override string Author { get; init; } = "RuKira";
    public override List<string>? Contributors { get; init; }
    public override Version Version { get; init; } = new("1.0.0");
    public override Range SptVersion { get; init; } = new("~4.0.1");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, Range>? ModDependencies { get; init; } = new();
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 3)]
public class AttachmentBackportMod(
    ISptLogger<AttachmentBackportMod> logger,
    AttachmentPatchService attachmentPatchService
) : IOnLoad
{
    public Task OnLoad()
    {
        logger.Info("AttachmentBackport loaded successfully!");
        attachmentPatchService.ApplyPatches();
        return Task.CompletedTask;
    }
}