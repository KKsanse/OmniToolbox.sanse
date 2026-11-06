using System;
using System.Collections.Generic;
using System.Linq;
namespace GardenLayout;
public sealed record GardenHouseGroup(ulong HouseId,string Label,List<GardenBedView> Beds);
public static class GardenGrouping
{
    public static string RegionOf(GardenBedView bed)=>string.IsNullOrWhiteSpace(bed.Region)?L("Text97EEED321F5B"):bed.Region;
    public static List<GardenHouseGroup> Houses(IEnumerable<GardenBedView> beds)
    {
        var result=beds.GroupBy(b=>b.HouseId).Select(g=>new GardenHouseGroup(g.Key,g.Select(b=>b.Address).FirstOrDefault(a=>!string.IsNullOrWhiteSpace(a))??"",g.ToList())).OrderBy(g=>g.Label,StringComparer.Ordinal).ThenBy(g=>g.HouseId).ToList();
        for(int i=0;i<result.Count;i++)if(string.IsNullOrWhiteSpace(result[i].Label))result[i]=result[i] with{Label=F("Text7BF7CB5688A4", i+1)};
        var duplicateLabels=result.GroupBy(g=>g.Label).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
        for(int i=0;i<result.Count;i++)if(duplicateLabels.Contains(result[i].Label))result[i]=result[i] with{Label=result[i].Label+F("Text3B718D89C831", i+1)};
        return result;
    }
    public static List<GardenBedView> Filter(IEnumerable<GardenBedView> beds,bool byHouse,string region,ulong house)
    {
        if(!byHouse)return beds.Where(b=>region.Length==0||RegionOf(b)==region).ToList();
        if(house!=0)return beds.Where(b=>b.HouseId==house).ToList();
        return Houses(beds).SelectMany(g=>g.Beds).ToList();
    }
}
