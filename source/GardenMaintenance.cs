using System;

namespace GardenLayout;

public enum GardenCropState { Unknown, Empty, Growing, Ready }
public sealed record GardenMenuOption(bool Present, bool? Enabled);
public sealed record GardenMenuState(GardenMenuOption Plant, GardenMenuOption Care, GardenMenuOption Fertilize, GardenMenuOption Harvest)
{
    // Menu presence identifies a state; an option must also be enabled before execution.
    public GardenCropState Crop => Plant.Enabled == true ? GardenCropState.Empty : Harvest.Enabled == true ? GardenCropState.Ready : Care.Present || Fertilize.Present ? GardenCropState.Growing : GardenCropState.Unknown;
    public GardenMenuOption For(GardenAction action) => action switch { GardenAction.Care => Care, GardenAction.Fertilize => Fertilize, GardenAction.Harvest => Harvest, _ => Plant };
    public string? SkipReason(GardenAction action)
    {
        if (Crop == GardenCropState.Empty && action != GardenAction.Plant) return L("Text6502FF5521F0");
        if (Crop == GardenCropState.Ready && action is GardenAction.Care or GardenAction.Fertilize) return L("Text9F0952336F72");
        var option = For(action);
        if (!option.Present) return L("Text08F2737B1E4B");
        if (option.Enabled == null) return L("Text781921D82BF5");
        return option.Enabled == true ? null : L("Text6EA0B5449AD0");
    }
}

public static class GardenMaintenanceFailure
{
    // Exact strings verified from the running client's LogMessage rows 4010/4012.
    public static string? Parse(string text, GardenAction action) => action == GardenAction.Fertilize ? text switch {
        "已经施加了足够的肥料了。" => L("Text8B02972BF84B"),
        "没有肥料。" => L("Text1AB1FB3E7BB5"),
        _ => null
    } : null;
}

public static class GardenCropObservation
{
    public static void Apply(GardenRecord record, GardenCropState state, DateTimeOffset at)
    {
        record.ObservedState = state;
        record.ObservedAt = at;
        if (state == GardenCropState.Empty) record.Active = false;
        else if (state is GardenCropState.Growing or GardenCropState.Ready) {
            if (!record.Active) {
                // A previously empty/harvested slot now has a crop whose planting was not observed.
                record.SeedId = 0;record.CropItemId=0;record.CropName="";record.SeedIdentifiedAt=null;
                record.PlantedAt = record.CaredAt = record.FertilizedAt = record.HarvestedAt = null;
                record.CareReminderBase = record.FertilizerReminderBase = record.CareReminderSentAt = record.FertilizerReminderSentAt = null;
            }
            record.Active = true;
        }
    }
    public static string Name(GardenRecord? record) => record == null ? L("Text3ACAA83343CF") : record.ObservedState switch {
        GardenCropState.Empty => L("TextCB557C76D00D"), GardenCropState.Growing => L("TextE6D7EEB88188"), GardenCropState.Ready => L("TextC18E33F7A6AA"), _ => L("TextDEF24911B45D")
    };
}
