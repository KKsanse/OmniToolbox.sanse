#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GardenLayout;

public sealed class GardenCell
{
    public int NativeSlot { get; set; }
    public uint SeedId { get; set; }
    public uint SoilId { get; set; }
}
public sealed class GardenPreset
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Name { get; set; }=L("Text19E4AD46A88A");
    public uint DefaultSoilId { get; set; }
    public List<GardenCell> Cells { get; set; }=[];
    public List<int> PlantOrder { get; set; }=[];
}
public sealed record GardenTarget(ulong ObjectId,uint GroupKey,int NativeSlot);
public sealed record PlantStep(ulong ObjectId,uint GroupKey,int NativeSlot,uint SeedId,uint SoilId);
public sealed class GardenBinding
{
    public ulong HouseId { get; set; }
    public uint GroupKey { get; set; }
    public string LayoutId { get; set; }="";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public string Name { get; set; }="";
    public GardenReminderRule Reminders { get; set; }=new();
    public List<int> NativeSlots { get; set; }=[];
}

public static class GardenPlanner
{
    // Verified from the running client Item rows 7128, 7129 and 7130.
    public static int FurnitureSlots(uint baseId)=>baseId switch {131126=>4,131127=>6,131128=>8,_=>0};
    public static GardenPreset Create(int count,IEnumerable<int>? nativeSlots=null)
    {
        if(count is not (4 or 6 or 8))throw new ArgumentException(L("Text548B0EE38E78"));
        var slots=nativeSlots?.Order().ToList()??Enumerable.Range(0,count).ToList();
        if(slots.Count!=count)throw new ArgumentException(L("TextEAD7F803385E"));
        var preset=new GardenPreset{Cells=slots.Select(slot=>new GardenCell{NativeSlot=slot}).ToList(),PlantOrder=Enumerable.Range(0,count).ToList()};
        Validate(preset);return preset;
    }
    public static int[] Grid(int count)=>count switch {4=>[0,1,3,2],6=>[0,1,2,5,4,3],8=>[0,1,2,7,-1,3,6,5,4],_=>throw new ArgumentException(L("TextDDCD4CC797BD"))};
    public static string UniqueName(string requested,IEnumerable<GardenPreset> presets)
    {
        string stem=new(requested.Where(c=>!char.IsControl(c)).ToArray());
        stem=stem.Trim();if(stem.Length==0)stem=L("Text19E4AD46A88A");
        var names=presets.Select(p=>p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for(int n=1;;n++){
            string suffix=n==1?"":$" ({n})";
            string candidate=stem[..Math.Min(stem.Length,64-suffix.Length)]+suffix;
            if(!names.Contains(candidate))return candidate;
        }
    }
    public static bool AnchorMatches(GardenBinding b,float x,float y,float z)=>float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(z)&&float.IsFinite(b.X)&&float.IsFinite(b.Y)&&float.IsFinite(b.Z)
        &&Math.Pow(b.X-x,2)+Math.Pow(b.Y-y,2)+Math.Pow(b.Z-z,2)<=.25;
    public static GardenTarget Decode(ulong objectId,uint dataId,uint argument)
    {
        if(objectId==0||dataId!=2003757)throw new ArgumentException(L("Text3903305F572E"));
        int slot=(int)(argument>>24);uint group=argument&0xFFFFFF;
        if(slot>7||group==0)throw new ArgumentException(L("Text0BC5301B390C"));
        return new(objectId,group,slot);
    }
    public static void Validate(GardenPreset preset)
    {
        if(preset==null||string.IsNullOrWhiteSpace(preset.Id)||preset.Id.Length>64||string.IsNullOrWhiteSpace(preset.Name)||preset.Name.Length>64||preset.Name.Any(char.IsControl))
            throw new ArgumentException(L("TextD9C43DB98560"));
        if(preset.Cells==null||preset.Cells.Count is not (4 or 6 or 8)||preset.Cells.Any(c=>c==null))throw new ArgumentException(L("Text6870C06D192B"));
        if(preset.Cells.Any(c=>c.NativeSlot<0||c.NativeSlot>7)||preset.Cells.Select(c=>c.NativeSlot).Distinct().Count()!=preset.Cells.Count)
            throw new ArgumentException(L("TextA64FF68562D8"));
        if(preset.PlantOrder==null||!preset.PlantOrder.Order().SequenceEqual(Enumerable.Range(0,preset.Cells.Count)))
            throw new ArgumentException(L("TextA2CBC7FBD33E"));
    }
    public static List<PlantStep> Plan(GardenPreset preset,IReadOnlyCollection<GardenTarget> targets,uint selectedGroup,bool allNearby)
    {
        Validate(preset);
        var chosen=targets.Where(t=>allNearby||t.GroupKey==selectedGroup).GroupBy(t=>t.GroupKey).OrderBy(g=>g.Key).ToList();
        if(chosen.Count==0)throw new ArgumentException(L("Text24DA2199DCB2"));
        var steps=new List<PlantStep>();
        foreach(var group in chosen){
            if(group.Select(t=>t.NativeSlot).Distinct().Count()!=group.Count()||group.Select(t=>t.ObjectId).Distinct().Count()!=group.Count())
                throw new ArgumentException(L("TextAB44D5DB0A2C"));
            if(group.Count()!=preset.Cells.Count)throw new ArgumentException(L("TextCE6754F5F41A"));
            var slots=group.ToDictionary(t=>t.NativeSlot);
            foreach(int index in preset.PlantOrder){
                var cell=preset.Cells[index];
                if(!slots.TryGetValue(cell.NativeSlot,out var slot))throw new ArgumentException(L("Text00C08F0688D3"));
                if(cell.SeedId==0)continue;
                uint soil=cell.SoilId==0?preset.DefaultSoilId:cell.SoilId;
                if(soil==0)throw new ArgumentException(F("Text90578C2B1288", index+1));
                steps.Add(new(slot.ObjectId,slot.GroupKey,slot.NativeSlot,cell.SeedId,soil));
            }
        }
        if(steps.Count==0)throw new ArgumentException(L("Text94845097A10F"));
        return steps;
    }
    public static Dictionary<uint,int> Materials(IEnumerable<PlantStep> plan)
    {
        var result=new Dictionary<uint,int>();
        foreach(var p in plan){result[p.SeedId]=result.GetValueOrDefault(p.SeedId)+1;result[p.SoilId]=result.GetValueOrDefault(p.SoilId)+1;}
        return result;
    }
}
