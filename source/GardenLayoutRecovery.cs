using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace GardenLayout;
public sealed record GardenRecoveryReport(int Repaired,int Unresolved);
public static class GardenLayoutRecovery
{
    public static bool TryRecover(GardenPreset preset,out GardenPreset recovered)
    {
        recovered=null!;
        try{GardenPlanner.Validate(preset);return false;}catch(ArgumentException){}
        if(preset==null||preset.Cells==null||preset.PlantOrder==null||preset.Cells.Count>4096||preset.PlantOrder.Count>4096)return false;
        var cells=preset.Cells.ToList();var order=preset.PlantOrder.ToList();
        while(cells.Count>8){
            if(!cells.Take(8).Select((c,i)=>c!=null&&c.NativeSlot==i&&c.SeedId==0&&c.SoilId==0).All(x=>x))return false;
            cells=cells.Skip(8).ToList();
        }
        if(cells.Count is not (4 or 6 or 8))return false;
        while(order.Count>cells.Count){
            if(order.Count<8||!order.Take(8).SequenceEqual(Enumerable.Range(0,8)))return false;
            order=order.Skip(8).ToList();
        }
        var candidate=new GardenPreset{Id=preset.Id,Name=preset.Name,DefaultSoilId=preset.DefaultSoilId,Cells=cells,PlantOrder=order};
        try{GardenPlanner.Validate(candidate);}catch(ArgumentException){return false;}
        if(cells.Count==preset.Cells.Count&&order.Count==preset.PlantOrder.Count)return false;
        recovered=candidate;return true;
    }
    public static GardenRecoveryReport Apply(IEnumerable<GardenPreset> layouts,Action beforeChange)
    {
        var changes=new List<(GardenPreset Original,GardenPreset Fixed)>();int unresolved=0;
        foreach(var layout in layouts){
            if(TryRecover(layout,out var candidate))changes.Add((layout,candidate));
            else try{GardenPlanner.Validate(layout);}catch(ArgumentException){unresolved++;}
        }
        if(changes.Count>0){
            beforeChange(); // No mutation if the original cannot be backed up.
            foreach(var pair in changes){pair.Original.Cells=pair.Fixed.Cells;pair.Original.PlantOrder=pair.Fixed.PlantOrder;}
        }
        return new(changes.Count,unresolved);
    }
    public static string Backup(string source)
    {
        if(!File.Exists(source))throw new IOException(L("Text71FD28D9D3C8"));
        string backup=source+".before-layout-repair-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+"-"+Guid.NewGuid().ToString("N")[..8]+".bak";
        File.Copy(source,backup,false);return backup;
    }
}
