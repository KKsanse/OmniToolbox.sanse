using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GardenLayout;

public sealed record GardenCropDefinition(uint SeedId,string SeedName,uint CropItemId,string CropName);
public sealed record GardenCropMatch(uint SeedId,uint CropItemId,string CropName);
public sealed record GardenNameObservation(ulong HouseId,uint GroupKey,int NativeSlot,ulong ObjectId,DateTimeOffset At,GardenCropMatch Crop);

public static class GardenCropIdentity
{
    // Exact positive growth message observed in the local client's SystemMessage/None stream.
    private static readonly Regex GrowthMessage=new("^([^\\r\\n]{1,80})正茁壮成长。$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(25));
    private static readonly Regex MenuTitle=new("^园圃([0-9]{1,2})：地垄([1-8])$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(25));
    public static string? ParseName(string body)
    {
        if(body.Length>128)return null;
        var match=GrowthMessage.Match(body);return match.Success?match.Groups[1].Value:null;
    }
    public static GardenCropMatch? Resolve(string name,IEnumerable<GardenCropDefinition> definitions)
    {
        var found=definitions.Where(d=>d.SeedId!=0&&d.CropItemId!=0&&string.Equals(d.CropName,name,StringComparison.Ordinal)).Distinct().ToList();
        if(found.Count==0)return null;
        var seeds=found.Select(d=>d.SeedId).Distinct().ToList();var crops=found.Select(d=>d.CropItemId).Distinct().ToList();
        return new(seeds.Count==1?seeds[0]:0,crops.Count==1?crops[0]:0,name);
    }
    public static bool Matches(GardenNameObservation pending,ulong house,GardenTarget target,string menuTitle,DateTimeOffset now)
    {
        if(now<pending.At||now-pending.At>TimeSpan.FromSeconds(2)||pending.HouseId!=house||pending.GroupKey!=target.GroupKey||pending.NativeSlot!=target.NativeSlot||pending.ObjectId!=target.ObjectId)return false;
        var match=MenuTitle.Match(menuTitle);
        return match.Success&&int.TryParse(match.Groups[1].Value,out int garden)&&garden is >=1 and <=64&&int.Parse(match.Groups[2].Value)-1==pending.NativeSlot;
    }
    public static bool Apply(GardenRecord record,GardenCropMatch crop,DateTimeOffset at)
    {
        bool changed=!record.Active||record.ObservedState!=GardenCropState.Growing||record.CropName!=crop.CropName||record.CropItemId!=crop.CropItemId||record.SeedIdentifiedAt==null||(crop.SeedId!=0&&record.SeedId!=crop.SeedId);
        GardenCropObservation.Apply(record,GardenCropState.Growing,at);
        if(!changed)return false;
        record.CropItemId=crop.CropItemId;record.CropName=crop.CropName;
        if(crop.SeedId!=0)record.SeedId=crop.SeedId;
        record.SeedIdentifiedAt=at;
        // Identification never creates/restarts operation timestamps or reminder dedup.
        return true;
    }
}
