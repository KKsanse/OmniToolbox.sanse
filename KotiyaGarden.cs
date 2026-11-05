// SPDX-License-Identifier: AGPL-3.0-only
// Omni Toolbox port and custom garden features: Sanse. Original credits: NOTICE.md.
#nullable enable annotations
#nullable disable warnings
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Drawing;
global using OmenTools;
global using OmenTools.ImGuiOm;
global using OmenTools.Extensions;
global using OmenTools.OmenService;
global using static OmenTools.Global.Globals;
global using static OmenTools.Info.Game.Data.Addons;
global using Dalamud.Bindings.ImGui;
global using Dalamud.Interface;
global using Dalamud.Interface.Utility.Raii;
global using Dalamud.Game;
global using Dalamud.Plugin;
global using Dalamud.Plugin.Services;
global using IGameObject=OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds.IGameObject;
global using IObjectTable=OmenTools.Dalamud.Services.Game.Object.Abstractions.IObjectTable;

global using static GardenLayout.GardenText;
global using GardenLayout;


namespace GardenLayout
{
using System;



// HouseId value layout follows the installed FFXIVClientStructs HouseId /
// HouseUnit contract. This decodes saved identities, not process memory.
public sealed record GardenAddress(ushort WorldId,ushort TerritoryTypeId,int Ward,int? Plot)
{
    public static GardenAddress? Decode(ulong id)
    {
        if(id==0)return null;
        var world=(ushort)(id>>48);var territory=(ushort)(id>>32);
        if(world==0||territory==0)return null;
        int ward=(int)((id>>16)&0x3f)+1;
        int unit=(int)(id&0xff);int? plot=(unit&0x80)==0?(unit&0x7f)+1:null;
        return new(world,territory,ward,plot);
    }
    public string Describe(string world,string region)=>F("Text231BD03834D1", world, region, Ward)+(Plot.HasValue?F("Text2D3C3B7FC393", Plot.Value):L("Text69A9FFABDD85"));
}

}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;



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

}


namespace GardenLayout
{

public static class GardenFertilizer
{
    // Item 7767 verified against the running client's Item sheet.
    public const uint DefaultItemId=7767;
    public static uint Resolve(uint selected)=>selected==0?DefaultItemId:selected;
}

}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.Linq;



// Register the entire step before execution. A waiting TaskHelper task is not
// preempted when an addon listener appends a higher-priority fill task.
public static class GardenPlantSequence
{
    public static void Enqueue(Action<Func<bool>,string> enqueue,Func<bool> begin,
        Func<bool> select,Func<bool> fill,Func<bool> confirm,Func<bool> finish)
    {
        enqueue(begin,L("Text912BB478315F"));
        enqueue(select,L("TextBBA5D4EAC7D3"));
        enqueue(fill,L("Text10F4FAD83CB3"));
        enqueue(confirm,L("TextB3B6B613BC5C"));
        enqueue(finish,L("Text0CA8DCDCA647"));
    }
}

public sealed class GardenSeedClipboard
{
    public uint SeedId { get; private set; }
    public bool Copy(GardenCell cell)
    {
        if(cell.SeedId==0)return false;
        SeedId=cell.SeedId;return true;
    }
    public bool Paste(GardenCell cell)
    {
        if(SeedId==0)return false;
        cell.SeedId=SeedId;return true;
    }
    public static List<uint> ConfiguredSeeds(IEnumerable<GardenPreset> presets)=>
        presets.SelectMany(p=>p.Cells).Select(c=>c.SeedId).Where(id=>id!=0).Distinct().ToList();
}

}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

}


namespace GardenLayout
{
using System;



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

}


namespace GardenLayout
{
using FFXIVClientStructs.FFXIV.Client.Game;

public enum GardenStockStatus { Available,Missing,Unavailable }
public static unsafe class GardenNativeInventory
{
    public static GardenStockStatus FindCarried(uint itemId,out InventoryItem found)
    {
        found=default;var manager=InventoryManager.Instance();if(manager==null)return GardenStockStatus.Unavailable;
        Span<nint> bags=stackalloc nint[4];
        for(int i=0;i<4;i++)bags[i]=(nint)manager->GetInventoryContainer((InventoryType)i);
        return FindInBags(bags,itemId,out found);
    }
    // Framework-owned four carried containers only. Resolve afresh for every selection;
    // return a complete value copy, never retain a native slot pointer across frames.
    public static GardenStockStatus FindInBags(ReadOnlySpan<nint> bags,uint itemId,out InventoryItem found)
    {
        found=default;if(bags.Length!=4||itemId==0)return GardenStockStatus.Unavailable;
        bool complete=true;
        for(int i=0;i<4;i++){
            if(bags[i]==0){complete=false;continue;}
            var bag=*(InventoryContainer*)bags[i];
            if(!bag.IsLoaded||bag.Items==null||bag.Size!=35||bag.Type!=(InventoryType)i){complete=false;continue;}
            for(int slot=0;slot<35;slot++){
                var item=bag.Items[slot];
                if(item.IsSymbolic||item.ItemId!=itemId||item.Quantity==0||item.Container!=bag.Type||item.Slot!=slot)continue;
                found=item;return GardenStockStatus.Available;
            }
        }
        return complete?GardenStockStatus.Missing:GardenStockStatus.Unavailable;
    }
}

}


namespace GardenLayout
{
using FFXIVClientStructs.FFXIV.Component.GUI;



public static unsafe class GardenNativeMenu
{
    // Input is a complete SDK struct copy from the current framework frame.
    public static bool TrySnapshot(AtkComponentList list,int entries,out bool[] enabled)
    {
        enabled=[];
        if(entries is <1 or >16||list.IsUpdatePending||list.ListLength!=entries||list.AllocatedItemRendererListLength<entries||list.AllocatedItemRendererListLength>256||list.ItemRendererList==null)return false;
        enabled=new bool[entries];
        for(int i=0;i<entries;i++){var item=list.ItemRendererList[i];enabled[i]=!item.IsDisabled;}
        return true;
    }
    public static bool TryReadTitle(AtkTextNode node,out string title)
    {
        title="";var text=node.NodeText;
        if(node.NodeId!=2||node.Type!=NodeType.Text||!text.StringPtr.HasValue||text.BufUsed<1||text.BufUsed>512||text.BufSize<text.BufUsed)return false;
        var bytes=new System.ReadOnlySpan<byte>(text.StringPtr,(int)text.BufUsed);
        if(bytes[^1]!=0)return false;
        title=System.Text.Encoding.UTF8.GetString(bytes[..^1]);return true;
    }
}

}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.Linq;



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

}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;



public enum GardenAction { Plant, Care, Fertilize, Harvest, Identify }
public sealed class GardenRecord
{
    public ulong HouseId { get; set; }
    public uint GroupKey { get; set; }
    public int NativeSlot { get; set; }
    public uint SeedId { get; set; }
    public uint CropItemId { get; set; }
    public string CropName { get; set; }="";
    public DateTimeOffset? SeedIdentifiedAt { get; set; }
    public bool Active { get; set; }=true;
    public GardenCropState ObservedState { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public DateTimeOffset? PlantedAt { get; set; }
    public DateTimeOffset? CaredAt { get; set; }
    public DateTimeOffset? FertilizedAt { get; set; }
    public DateTimeOffset? HarvestedAt { get; set; }
    public DateTimeOffset? CareReminderBase { get; set; }
    public DateTimeOffset? FertilizerReminderBase { get; set; }
    public DateTimeOffset? CareReminderSentAt { get; set; }
    public DateTimeOffset? FertilizerReminderSentAt { get; set; }
}
public sealed class GardenReminderRule
{
    public bool CareEnabled { get; set; }=true;
    public bool FertilizerEnabled { get; set; }=true;
    public int CareMinutes { get; set; }=720;
    public int FertilizerMinutes { get; set; }=60;
    public int RepeatMinutes { get; set; }
}
public sealed class GardenPreferences
{
    public bool AdvanceCareTalk { get; set; }=true;
    public bool RemindersEnabled { get; set; }=true;
    public bool ChatReminder { get; set; }=true;
    public bool SoundReminder { get; set; }
    public bool QuietInCombat { get; set; }=true;
    public bool CompactHome { get; set; }=true;
}
public sealed class GardenOperation
{
    public DateTimeOffset At { get; set; }
    public ulong HouseId { get; set; }
    public uint GroupKey { get; set; }
    public int NativeSlot { get; set; }=-1;
    public GardenAction Action { get; set; }
    public string Stage { get; set; }="";
    public string RunId { get; set; }="";
    public bool Manual { get; set; }
}
public sealed record GardenSignal(GardenAction Action,int? GardenNumber=null,int? NativeSlot=null);
public sealed record GardenDue(GardenRecord Record,GardenAction Action,DateTimeOffset BaseAt,DateTimeOffset DueAt);

public static class GardenLedger
{
    private static readonly Regex PlantMessage=new("^在园圃([0-9]+)的第([0-9]+)个地垄上播下了种子。$",RegexOptions.CultureInvariant);
    public static GardenSignal? ParseSuccess(string text)
    {
        var m=PlantMessage.Match(text);
        if(m.Success&&int.TryParse(m.Groups[1].Value,out int bed)&&bed is >=1 and <=64&&int.TryParse(m.Groups[2].Value,out int slot)&&slot is >=1 and <=8)return new(GardenAction.Plant,bed,slot-1);
        return text switch {"给作物施加了肥料。"=>new(GardenAction.Fertilize),"作物正在健康成长。"=>new(GardenAction.Care),_=>null};
    }
    public static GardenRecord GetOrCreate(List<GardenRecord> records,ulong house,uint group,int slot)
    {
        if(house==0||group==0||slot is <0 or >7)throw new ArgumentException(L("Text55F72260F09D"));
        var record=records.FirstOrDefault(r=>r.HouseId==house&&r.GroupKey==group&&r.NativeSlot==slot);
        if(record!=null)return record;
        record=new(){HouseId=house,GroupKey=group,NativeSlot=slot};records.Add(record);return record;
    }
    public static bool Record(GardenRecord r,GardenAction action,DateTimeOffset at,uint seed=0)
    {
        if(at==default)throw new ArgumentException(L("Text7F11E3F7D8D4"));
        DateTimeOffset? current=action switch{GardenAction.Plant=>r.PlantedAt,GardenAction.Care=>r.CaredAt,GardenAction.Fertilize=>r.FertilizedAt,_=>r.HarvestedAt};
        if(current.HasValue&&at<=current.Value)return false;
        // A delayed care/fertilizer signal from a previous crop must not rewrite this crop.
        if(action!=GardenAction.Plant&&r.PlantedAt.HasValue&&at<r.PlantedAt)return false;
        if(!r.Active&&action is GardenAction.Care or GardenAction.Fertilize){
            r.SeedId=0;r.CropItemId=0;r.CropName="";r.SeedIdentifiedAt=null;r.PlantedAt=r.CaredAt=r.FertilizedAt=r.HarvestedAt=null;
            r.CareReminderBase=r.FertilizerReminderBase=r.CareReminderSentAt=r.FertilizerReminderSentAt=null;
        }
        switch(action){
            case GardenAction.Plant:r.CropItemId=0;r.CropName="";r.SeedIdentifiedAt=null;r.Active=true;r.ObservedState=GardenCropState.Growing;r.ObservedAt=at;r.SeedId=seed;r.PlantedAt=at;r.CaredAt=r.FertilizedAt=r.HarvestedAt=null;r.CareReminderBase=r.FertilizerReminderBase=r.CareReminderSentAt=r.FertilizerReminderSentAt=null;break;
            case GardenAction.Care:r.Active=true;r.CaredAt=at;break;
            case GardenAction.Fertilize:r.Active=true;r.FertilizedAt=at;break;
            case GardenAction.Harvest:r.Active=false;r.ObservedState=GardenCropState.Empty;r.ObservedAt=at;r.HarvestedAt=at;break;
        }
        return true;
    }
    public static DateTimeOffset? BaseTime(GardenRecord r,GardenAction action)=>action==GardenAction.Care?r.CaredAt??r.PlantedAt:r.FertilizedAt??r.PlantedAt;
    public static DateTimeOffset? DueAt(GardenRecord r,GardenReminderRule rule,GardenAction action)
    {
        if(!NeedsMaintenance(r)||(action==GardenAction.Care?!rule.CareEnabled:!rule.FertilizerEnabled))return null;
        var basis=BaseTime(r,action);if(!basis.HasValue)return null;
        int minutes=Math.Clamp(action==GardenAction.Care?rule.CareMinutes:rule.FertilizerMinutes,1,43_200);
        return basis.Value.AddMinutes(minutes);
    }
    public static List<GardenDue> Pending(IEnumerable<GardenRecord> records,IEnumerable<GardenBinding> bindings,DateTimeOffset now)
    {
        var result=new List<GardenDue>();
        foreach(var r in records){
            var b=bindings.FirstOrDefault(b=>b.HouseId==r.HouseId&&b.GroupKey==r.GroupKey);if(b==null)continue;
            var rule=b.Reminders??new();
            foreach(var action in new[]{GardenAction.Care,GardenAction.Fertilize}){
                var due=DueAt(r,rule,action);if(!due.HasValue||due>now)continue;
                var basis=BaseTime(r,action)!.Value;
                var sentBase=action==GardenAction.Care?r.CareReminderBase:r.FertilizerReminderBase;
                var sentAt=action==GardenAction.Care?r.CareReminderSentAt:r.FertilizerReminderSentAt;
                if(sentBase==basis&&sentAt.HasValue&&(rule.RepeatMinutes<=0||now<sentAt.Value.AddMinutes(Math.Clamp(rule.RepeatMinutes,1,1440))))continue;
                result.Add(new(r,action,basis,due.Value));
            }
        }
        // Fertilizer notifications use the last eligible crop in the entire bed.
        // A missing time blocks the batch rather than being counted as already due.
        foreach(var bed in result.Where(d=>d.Action==GardenAction.Fertilize).GroupBy(d=>(d.Record.HouseId,d.Record.GroupKey)).ToList()){
            var active=records.Where(r=>r.HouseId==bed.Key.HouseId&&r.GroupKey==bed.Key.GroupKey&&NeedsMaintenance(r)).ToList();
            var rule=bindings.First(b=>b.HouseId==bed.Key.HouseId&&b.GroupKey==bed.Key.GroupKey).Reminders??new();
            result.RemoveAll(d=>d.Action==GardenAction.Fertilize&&d.Record.HouseId==bed.Key.HouseId&&d.Record.GroupKey==bed.Key.GroupKey);
            if(active.Count==0||active.Any(r=>DueAt(r,rule,GardenAction.Fertilize) is not {} at||at>now))continue;
            result.AddRange(active.Select(r=>new GardenDue(r,GardenAction.Fertilize,BaseTime(r,GardenAction.Fertilize)!.Value,DueAt(r,rule,GardenAction.Fertilize)!.Value)));
        }
        return result;
    }
    public static bool NeedsMaintenance(GardenRecord r)=>r.Active&&r.ObservedState is not (GardenCropState.Empty or GardenCropState.Ready);
    public static string ReminderText(string name,string address,GardenAction action,int count)=>action==GardenAction.Fertilize
        ? F("TextAE81C17C249F", name, address, count)
        : F("Text8F829FD728DD", name, address, count);
    public static void MarkSent(GardenDue due,DateTimeOffset at)
    {
        if(due.Action==GardenAction.Care){due.Record.CareReminderBase=due.BaseAt;due.Record.CareReminderSentAt=at;}
        else{due.Record.FertilizerReminderBase=due.BaseAt;due.Record.FertilizerReminderSentAt=at;}
    }
    public static string ActionName(GardenAction action)=>action switch{GardenAction.Plant=>L("TextCFA6207EC025"),GardenAction.Care=>L("Text69CA8500A419"),GardenAction.Fertilize=>L("TextDDAE47CD1786"),GardenAction.Identify=>L("TextA869D2D4C515"),_=>L("Text3B66E843457C")};
}

public sealed class GardenRunGuard
{
    private readonly HashSet<(ulong,uint,int,GardenAction)> started=[];
    private readonly HashSet<(ulong,uint,int,GardenAction)> completed=[];
    public void Clear(){started.Clear();completed.Clear();}
    public bool Begin(ulong house,GardenTarget target,GardenAction action)=>started.Add((house,target.GroupKey,target.NativeSlot,action));
    public bool Complete(ulong house,GardenTarget target,GardenAction action)=>completed.Add((house,target.GroupKey,target.NativeSlot,action));
}

}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;



public static class GardenShare
{
    public const string Prefix="GLW1:";
    public const int MaxCodeLength=32_768;
    private sealed class Envelope
    {
        public int Version { get; set; }=1;
        public string Name { get; set; }="";
        public uint DefaultSoilId { get; set; }
        public List<GardenCell> Cells { get; set; }=[];
        public List<int> PlantOrder { get; set; }=[];
    }
    public static string Export(GardenPreset preset)
    {
        GardenPlanner.Validate(preset);
        var dto=new Envelope{Name=preset.Name,DefaultSoilId=preset.DefaultSoilId,Cells=preset.Cells,PlantOrder=preset.PlantOrder};
        return Prefix+Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(dto));
    }
    public static GardenPreset Import(string code,IEnumerable<GardenPreset> existing,IReadOnlySet<uint> seeds,IReadOnlySet<uint> soils)
    {
        if(string.IsNullOrWhiteSpace(code)||code.Length>MaxCodeLength)throw new ArgumentException(L("Text3011E4D0474B"));
        code=code.Trim();if(!code.StartsWith(Prefix,StringComparison.Ordinal))throw new ArgumentException(L("Text2F714C7C3248"));
        Envelope? dto;
        try{
            byte[] bytes=Convert.FromBase64String(code[Prefix.Length..]);
            dto=JsonSerializer.Deserialize<Envelope>(bytes,new JsonSerializerOptions{MaxDepth=12});
        }catch(Exception e)when(e is FormatException or JsonException){throw new ArgumentException(L("TextE7AA88F2B160"));}
        if(dto==null||dto.Version!=1)throw new ArgumentException(L("TextD38B22005F79"));
        var result=new GardenPreset{Name=dto.Name,DefaultSoilId=dto.DefaultSoilId,Cells=dto.Cells,PlantOrder=dto.PlantOrder};
        if(GardenLayoutRecovery.TryRecover(result,out var recovered)){result.Cells=recovered.Cells;result.PlantOrder=recovered.PlantOrder;}
        GardenPlanner.Validate(result);
        if(result.Cells.Any(c=>c.SeedId!=0&&!seeds.Contains(c.SeedId))||result.Cells.Any(c=>c.SoilId!=0&&!soils.Contains(c.SoilId))||(result.DefaultSoilId!=0&&!soils.Contains(result.DefaultSoilId)))throw new ArgumentException(L("TextB524D7242D04"));
        result.Name=GardenPlanner.UniqueName(result.Name,existing);
        return result; // New identity; importing never rebinds a garden or overwrites a preset.
    }
}

}


namespace GardenLayout
{


public static class GardenTalkPolicy
{
    public static bool CanAdvance(bool enabled,bool busy,GardenAction action,bool chosen,bool contextValid,bool visible,bool alreadyAdvanced)=>
        enabled&&busy&&action==GardenAction.Care&&chosen&&contextValid&&visible&&!alreadyAdvanced;
}

}


namespace GardenLayout
{

public static partial class GardenText
{
    public static System.Collections.Generic.IReadOnlyDictionary<string,string> Source {get;}=new System.Collections.Generic.Dictionary<string,string>
    {
        ["Description"]="按格选择种子与土壤，保存杂交布局；支持多园圃管理、护理施肥、作物识别与到期提醒。需要开启自动跳过对话。",
        ["OpenCommand"]="打开园圃工作台",
        ["Text000CF763ECE4"]="编辑 / 新建布局",
        ["Text0027CDE9B260"]="已绑定园圃",
        ["Text00C08F0688D3"]="布局格位在当前园圃不存在。",
        ["Text02F1A371FB49"]="统一土壤",
        ["Text0343BE98DB6E"]="房区预览",
        ["Text0558A73DC22D"]="{0} 格",
        ["Text05B9C9100B75"]="园圃配置为空。",
        ["Text0677F30054B1"]="进入工作台",
        ["Text0698F17D833B"]="{0} · 槽{1}：等待成功反馈。",
        ["Text071FBCC539B1"]="护理：",
        ["Text0836BB6E2FBE"]="已保存 {0} 个园圃绑定 · 点击进入工作台编辑布局与记录",
        ["Text08F2737B1E4B"]="当前格位没有此操作",
        ["Text0A44756FD758"]="确认本格操作",
        ["Text0BC5301B390C"]="园圃槽位数据无效。",
        ["Text0C5948A18120"]="收获当前园圃",
        ["Text0CA8DCDCA647"]="等待种植结果与材料数量更新",
        ["Text0D92C8EF2C4E"]="编辑 {0} 号格 · 游戏槽位 {1}",
        ["Text0DBF5C72C69C"]="时间记录",
        ["Text0E7E37EE6749"]="收获附近园圃",
        ["Text0E9B556DA1B4"]="已导入：",
        ["Text0EF8DBB8602F"]="{0}超时，已停止施肥；请关闭游戏园艺或选料界面后重试。",
        ["Text103FC1ECAA72"]="返回紧凑总览",
        ["Text10F4FAD83CB3"]="等待播种窗口并填入土壤和种子",
        ["Text10FE95A596BA"]="分享种子、土壤、格位与播种顺序；不含房屋、绑定关系、时间记录。导入为新布局。",
        ["Text1182C5454F11"]="未选择",
        ["Text11D174B11D9E"]="槽位 {0}：{1}，跳过。",
        ["Text12AB8A67005E"]="当前格位没有可识别的种植或退出选项，已停止。",
        ["Text12F4907D4A1B"]="预设分享 / 导入",
        ["Text1327527E9B22"]="{0} 格 · 附近",
        ["Text14FE1ED84EE6"]="填入现在",
        ["Text157989A96492"]="取消绑定",
        ["Text17337902D692"]="执行记录",
        ["Text188ACF28DC43"]="{0} 株已到期",
        ["Text19022E29F9C2"]="园圃名称需为 1–40 个字符，不能包含换行或 ##。",
        ["Text19E4AD46A88A"]="新布局",
        ["Text1A74696FCD7C"]="种植附近已绑定园圃",
        ["Text1AB1FB3E7BB5"]="游戏未找到可用肥料",
        ["Text1AF52ACC0106"]="种植时间",
        ["Text1FE2D5804F7A"]="附近没有已绑定且配置了种子的园圃，请先刷新并绑定布局。",
        ["Text20221D19F6CD"]="槽位 {0}：播种窗口已打开，等待填入材料。",
        ["Text2040C3417A5C"]="先站到有权限的园圃附近，点击刷新。已有布局可通过下方按钮编辑。",
        ["Text20CA79FC956A"]="新布局格数",
        ["Text20CACA619CDC"]="当前园圃格位与计划不一致，已停止。",
        ["Text2203B787CB3D"]="护理间隔（分钟）",
        ["Text231BD03834D1"]="{0} · {1} {2}区",
        ["Text23E1665F9447"]="留空 / 跳过",
        ["Text2406FFD195E1"]="请输入不重复的有效布局名称。",
        ["Text245D92BBF960"]="护理时间",
        ["Text24DA2199DCB2"]="请先选择当前要使用的园圃格位。",
        ["Text2925C65A2BD3"]="延后一步",
        ["Text29C0D3363F93"]="已从游戏消息识别作物：{0}",
        ["Text2A71C6CF699B"]="{0}：最多需 {1}，持有 {2}",
        ["Text2B8FD881E295"]="住宅区 {0}",
        ["Text2C6660E7E9CC"]="移除当前目标",
        ["Text2C7E22630DF7"]="此格跟随统一土壤",
        ["Text2D14BCEAA413"]="请站在有权限的房屋庭院园圃附近。",
        ["Text2D3C3B7FC393"]="{0}号",
        ["Text2E824D04771A"]="请先取消当前游戏里的播种或对话窗口，再开始布局种植。",
        ["Text2F5F1D6FBFB0"]="未设置",
        ["Text2F714C7C3248"]="分享码格式不正确，需以 GLW1: 开头。",
        ["Text2F9DAA828907"]="删除",
        ["Text3011E4D0474B"]="分享码为空或过长。",
        ["Text33300F84CAC2"]="种植当前园圃",
        ["Text335F69AF03CF"]="施肥时间",
        ["Text389C6CAA7B70"]="预览当前园圃材料",
        ["Text3903305F572E"]="目标不是可识别的园圃格位。",
        ["Text39F752BE477C"]="准备{0}格位",
        ["Text3ACAA83343CF"]="未检测",
        ["Text3B2335DDF624"]="游戏识别：{0}\n识别时间：{1}",
        ["Text3B66E843457C"]="收获 / 归档",
        ["Text3BF9B4578364"]="启用模块后可打开园圃工作台。",
        ["Text3DCD3E62C0D8"]="园圃配置过大，请保留备份后检查。",
        ["Text3EBEB20A1809"]="补记时间不能在未来。",
        ["Text3F3A45E19297"]="服务器 {0}",
        ["Text4017C5B03913"]="已停止后续操作。",
        ["Text421E5EBBB3CE"]="请先取消当前游戏园艺菜单，再开始维护。",
        ["Text42651CE97A5D"]="粘贴种子",
        ["Text434FF978E57F"]="默认打开紧凑总览",
        ["Text467FA68CF154"]="{0}发生错误，已停止。",
        ["Text47566C7CA554"]="最近 200 条 · 分开记录种植、施肥、护理 · 等待轮询不重复写入",
        ["Text47BDE3679332"]="园圃名称已保存；布局和记录保持原值。",
        ["Text4855BCF19A16"]="开始任务",
        ["Text487D90DB5B77"]="。检查格位映射后，再为园圃绑定。",
        ["Text4891F08CD3FF"]="复制布局",
        ["Text4BD2AB225AE6"]="物品 {0}",
        ["Text50924E4237E2"]="种植 {0}    护理 {1}",
        ["Text528F47200A92"]="布局包含当前游戏未识别的种子或土壤，请重新选择。",
        ["Text53E3474DEC12"]="等待园圃操作确认超时，已停止。",
        ["Text548B0EE38E78"]="请选择四格、六格或八格园圃。",
        ["Text5533D5C44E87"]="先新建布局，或从分享码导入。",
        ["Text5565F8254F5D"]="槽{0}",
        ["Text55887CF0F413"]="施肥提醒",
        ["Text55E63C9D2A06"]="已保存的信息 · 非远程实时状态",
        ["Text55F72260F09D"]="园圃记录标识无效。",
        ["Text56CBD453708D"]="施肥：",
        ["Text576C57532394"]="布局绑定已保存。",
        ["Text597DBF065A28"]="    施肥：",
        ["Text59A427553380"]="保存园圃记录失败：",
        ["Text5B01BEEF504E"]="只推进本模块当前护理格位的普通对话，不点击选项或是非确认。使用 Omni 跳过对话时可关闭此项进行对照。",
        ["Text5B140B922B0D"]=" · 手动补记",
        ["Text5E20FC223736"]="种子",
        ["Text5EF7FBC468E6"]="校准目标必须属于当前选择的园圃。",
        ["Text602C8BC3F14E"]="护理附近园圃",
        ["Text60AACEDC39AC"]="手动补记",
        ["Text60D00550E47C"]="房屋已变化，停止维护任务。",
        ["Text64A1C6EBE659"]="已刷新附近 {0} 个园圃。",
        ["Text6502FF5521F0"]="空地，未种植作物",
        ["Text652411AF1B67"]="为当前园圃绑定布局后，可以设置它自己的提醒间隔。",
        ["Text6687AC8617E4"]="停止任务",
        ["Text6724A2574B1D"]="播种顺序：",
        ["Text6744B4C6A9AA"]="已关闭",
        ["Text67811821AC3B"]="\n游戏槽位 {0}。编号图为示意，可用游戏目标校准。",
        ["Text6870C06D192B"]="布局必须包含4、6或8个格位。",
        ["Text68A26702EF7F"]="生成当前布局分享码",
        ["Text69A9FFABDD85"]=" · 公寓",
        ["Text69CA8500A419"]="护理",
        ["Text6CBBFCB945BB"]=" 另有 {0} 个布局无法确认恢复，请保留原配置并检查。",
        ["Text6D5C8CF2837A"]="种植、施肥、护理是独立任务；每次只执行你点击的操作。",
        ["Text6D7517E3B158"]="等待记录",
        ["Text6DED4E175CD7"]=" 副本",
        ["Text6EA0B5449AD0"]="游戏当前禁止此操作（选项灰色）",
        ["Text7173CF39808B"]="槽位 {0}：材料已填入，等待确认。",
        ["Text71FD28D9D3C8"]="未找到原配置，暂不自动修复布局。",
        ["Text73201667202E"]="提醒设置",
        ["Text7450CC5B16E2"]="未记录",
        ["Text752199EC7CC8"]="{0}完成：成功 {1} 格，跳过 {2} 格。",
        ["Text7730996A394D"]="没有匹配的物品",
        ["Text7770FFAE7505"]="补记时间没有晚于现有记录，或早于本轮种植。现有记录已保留。",
        ["Text781921D82BF5"]="无法确认菜单选项是否可用",
        ["Text784265790450"]="园圃格位未完整加载或家具身份不匹配，请稍后靠近刷新。",
        ["Text784E422B5D57"]="房屋位置未记录",
        ["Text7889BE2CF966"]="布局：",
        ["Text792488D8275B"]="未选择肥料或背包肥料不足，已停止施肥。",
        ["Text79B0A54090DC"]="用当前目标校准此格",
        ["Text79FA1230B3F7"]="整轮",
        ["Text7BC54B7C9FE2"]="补记槽位不属于当前园圃。",
        ["Text7CE5BBCB659E"]="状态来自最近一次操作时的菜单检测；时间与种子来自本地记录。",
        ["Text7F11E3F7D8D4"]="操作时间无效。",
        ["Text7F4660153423"]=" 号##record",
        ["Text829C10C8BFCF"]="园圃护理时自动推进对话",
        ["Text83486CCBDA09"]="园圃位置已变化，请刷新并重新绑定，避免使用错误布局。",
        ["Text83C37E8A4A5F"]="改名",
        ["Text8617B0875973"]="园圃工作台  1.0",
        ["Text883FD78F653E"]="施肥当前园圃",
        ["Text884B9DA5A598"]="{0} · 游戏槽位 {1}",
        ["Text8853B77260FF"]="手动补记 · 游戏槽位 {0}",
        ["Text8973705F0576"]="园圃布局作业",
        ["Text89A6F0471B72"]="跟随统一土壤",
        ["Text89C3159F3EDA"]="校验并导入为新布局",
        ["Text8AD9C84CF13D"]="跳过：",
        ["Text8B02972BF84B"]="作物暂时不需要更多肥料",
        ["Text8C7E4E44CC47"]="{0} 格布局 · 示意排列，请按游戏实际格位校准",
        ["Text8F38FD93B5A0"]="正在执行",
        ["Text8F829FD728DD"]="{0}（{1}）：{2} 株作物已到护理提醒时间，请及时护理。",
        ["Text90578C2B1288"]="格位{0}没有选择土壤。",
        ["Text90E5970B3801"]="本园圃 {0} 株均已到期",
        ["Text912BB478315F"]="打开园圃格位",
        ["Text92065FFB5EE8"]="搜索名称 / ID##",
        ["Text920A1670F732"]="当前目标或园圃发生变化，停止维护。",
        ["Text94845097A10F"]="请至少为一个格位选择种子。",
        ["Text961CB99AFAE8"]="新布局（{0}格）",
        ["Text971478D8FE35"]="正在处理槽位 {0}：{1}",
        ["Text972977B48445"]="保存的布局",
        ["Text97B87A080F53"]="等待施肥选料界面",
        ["Text97EEED321F5B"]="未记录房区",
        ["Text982D422E91C2"]="园圃上下文已变化，已停止确认种植。",
        ["Text99BD299E6406"]="此格土壤（可选）",
        ["Text9AEA47BEF8B9"]="槽位 {0}：等待种子与土壤数量更新。",
        ["Text9C59580BDE9A"]="已备份并恢复 {0} 个布局，原种子、土壤、槽位与绑定保持不变。",
        ["Text9CD88EEFAEC2"]="系统提示音",
        ["Text9E2A52E6F634"]="未识别园艺操作或取消选项，已停止。",
        ["Text9E614859CC0B"]="园圃或房区发生变化，已停止。",
        ["Text9F0952336F72"]="作物已成熟",
        ["Text9F457338B705"]="本地时间",
        ["Text9F858079CF5D"]="打开所选肥料菜单",
        ["Text9F8B0DA2F890"]="园圃",
        ["TextA0E556C2B31D"]="请手动刷新附近园圃。时间记录从本版观察到的成功操作开始，也可手动补记。",
        ["TextA1C82BEE001D"]="刷新附近园圃",
        ["TextA2CBC7FBD33E"]="播种顺序必须包含每个布局格且不重复。",
        ["TextA31DA82E8C3E"]="护理当前园圃",
        ["TextA3EA3C17B401"]="确认删除",
        ["TextA4696F56343F"]="{0}超时，已停止后续格位。",
        ["TextA64FF68562D8"]="布局中有重复或无效的游戏槽位，请重新校准。",
        ["TextA869D2D4C515"]="作物识别",
        ["TextAA7D07798009"]="游戏聊天显示具体作物名后自动补录；保留已有操作时间，未知的种植时间不作推算。",
        ["TextAA9FE323F0E1"]="请选择与园圃格数一致的布局。",
        ["TextAB44D5DB0A2C"]="园圃格位不唯一，停止操作。",
        ["TextABE087F4B3C5"]="格位编辑",
        ["TextAD29A08E1A06"]="施肥按本园圃全部已记录的生长中作物合并提醒，以最后一株到期时间为准；空地及已检测成熟的作物不计入。时间未知的作物需先补记。间隔是你设定的提醒周期，不代表所有作物的成熟或枯萎时间。未记录护理/施肥时，从本轮种植记录开始计时。游戏关闭期间不发送提醒，重新登录后检查到期项目。",
        ["TextADF564B6196C"]="整圃剩余 {0}时{1}分",
        ["TextAE71EE588850"]="复制分享码",
        ["TextAE81C17C249F"]="{0}（{1}）：本园圃已记录的 {2} 株生长中作物均已到施肥提醒时间，可统一施肥。",
        ["TextB0493E50F288"]="重命名园圃",
        ["TextB073DB9C0BD6"]="就绪",
        ["TextB2ED7CB58887"]="游戏槽位",
        ["TextB3B6B613BC5C"]="确认本格播种",
        ["TextB42D66998A4D"]="已复制：",
        ["TextB4AF1F2CABF1"]="默认使用鱼粉；可在上方更换其他肥料。",
        ["TextB524D7242D04"]="布局含有当前游戏未识别的种子或土壤。",
        ["TextB81F66B01C61"]="背包中没有{0}，未开始施肥。请补充肥料，或在提醒设置中更换肥料。",
        ["TextB8894B8E1003"]="打开格位",
        ["TextBA3B39BB00D7"]="补记参数无效。",
        ["TextBA615C1A763C"]="离开房屋时显示已保存的布局和记录；需要靠近并刷新后才能操作。",
        ["TextBABB1AD8BDA5"]="提交施肥",
        ["TextBBA5D4EAC7D3"]="选择种植或跳过已有作物",
        ["TextBD1FE9882AFE"]="没有可处理的附近园圃，请刷新。",
        ["TextBDD2CB287011"]="种子未知",
        ["TextC18E33F7A6AA"]="可收获",
        ["TextC21B197E84D3"]="施肥附近园圃",
        ["TextC2C6BB3B7664"]="新建或导入布局",
        ["TextC3453C639ACA"]="材料不足：需要 {0} 和 {1}。没有替换为其他物品。",
        ["TextC6A489896851"]="当前布局数据不完整。请选择有效的四格、六格或八格布局。",
        ["TextC6AE39AB184A"]="复制种子",
        ["TextC85A760307E9"]="时间格式：yyyy-MM-dd HH:mm",
        ["TextC8C545CA59AE"]="已选种子 · 点击复用",
        ["TextC9E0F3954B5C"]="种植布局",
        ["TextCB4FBA11A986"]="将此布局绑定当前园圃",
        ["TextCB557C76D00D"]="空地",
        ["TextCB6DA8F5FDF2"]="已保存园圃 {0}",
        ["TextCE6754F5F41A"]="园圃格数与布局不一致。",
        ["TextCE9E55A6D3D7"]="种植完成：已确认 {0} 格，跳过已有作物 {1} 格。",
        ["TextCFA6207EC025"]="种植",
        ["TextCFA7DA4CF992"]="启用提醒",
        ["TextD05E22AE8BE8"]="剩余 {0}时{1}分",
        ["TextD19C08D4C752"]="布局备份失败，未修改布局：",
        ["TextD273BCA035D5"]="补记与归档只修改本地记录，不操作游戏作物。",
        ["TextD28629B1F701"]="护理提醒",
        ["TextD2E7B4FBF6D4"]="背包中的{0}已不可用，已停止施肥。请关闭游戏选料界面后补充肥料。",
        ["TextD38B22005F79"]="分享码版本暂不支持。",
        ["TextD3DF2CB34811"]="游戏内文字提醒",
        ["TextD534FA0E2807"]="剪贴板内容过长。",
        ["TextD55786AFD1EA"]="当前园圃已变化，停止维护任务。",
        ["TextD6A95703B17E"]="{0} 号格\n{1}##gardenCell{2}",
        ["TextD77B9713AB0B"]="槽位 {0}：等待播种确认。",
        ["TextD976DAF6059C"]="保存补记",
        ["TextD9C43DB98560"]="布局名称或标识无效。",
        ["TextDC71485F8EE9"]="点击",
        ["TextDCBE4EF55A55"]="新建布局",
        ["TextDD1384AE67EA"]="重复提醒（分钟，0 为只提醒一次）",
        ["TextDDAE47CD1786"]="施肥",
        ["TextDDCD4CC797BD"]="园圃格数无效。",
        ["TextDEE8C7D2C9FE"]="园圃上下文变化，已停止种植。",
        ["TextDEF24911B45D"]="状态未确认",
        ["TextE026C6693DC5"]="未绑定",
        ["TextE2B9B73BD656"]="无法读取园圃配置，原文件已保留：",
        ["TextE376E8ADBD45"]="预览附近种植",
        ["TextE446DD387E50"]="已复制分享码。",
        ["TextE4DB283E93BE"]="{0}发生异常，已停止施肥；请关闭游戏园艺或选料界面后重试。",
        ["TextE6318953050B"]="请先在游戏里选中要校准的实际格位。",
        ["TextE6D7EEB88188"]="生长中",
        ["TextE7AA88F2B160"]="分享码内容损坏或格式无效。",
        ["TextE7FA65456AC4"]="战斗中暂缓提醒",
        ["TextE91C9E16DD25"]="收到成功反馈，时间已记录",
        ["TextEA079BC32197"]="已收获 / 归档",
        ["TextEAAD260F5E90"]="园圃上下文已变化，已停止核对结果。",
        ["TextEAD7F803385E"]="园圃格位数量不完整，请重新刷新。",
        ["TextEB42882E94A1"]="已确认成功",
        ["TextEBFB049FBD78"]="当前房屋与选中的园圃不一致，请刷新后重新选择。",
        ["TextECB963A1EC1B"]="园圃不在附近，请靠近后刷新。",
        ["TextED31FBB483EE"]="操作",
        ["TextEF5C77416E28"]="提前一步",
        ["TextEF8629C8A5B7"]="粘贴分享码",
        ["TextF0681C3609D9"]="园圃上下文变化，停止施肥。",
        ["TextF151A1ED9A55"]="本地操作时间已补记。",
        ["TextF1BCDC0D31BD"]="{0}（种子待确认）",
        ["TextF2B57DBFDD7C"]="种子 / 状态",
        ["TextF4CE9FB84387"]="   {0}格园圃",
        ["TextF4D8C22939D4"]="预设分享 / 导入##GardenShare",
        ["TextF5B941B63FF5"]="施肥间隔（分钟）",
        ["TextF65434A68EC2"]="某个园圃绑定的布局不存在，请重新绑定。",
        ["TextF6B971CCEE5D"]="园圃 {0}",
        ["TextF6C02CD10EDC"]="全部房区",
        ["TextF6F58196C9DF"]="进入园圃工作台",
        ["TextF708841160AB"]="分享码已生成，请复制后发给朋友。",
        ["TextF77E9DDF6221"]="背包数据尚未就绪，请稍后重试施肥。",
        ["TextF919AE930CEF"]="格位映射已校准，没有执行种植。",
        ["TextFAA2F2ED6735"]="布局",
        ["TextFAA3DAB035C7"]="当前绑定：",
        ["TextFAC8BA101C60"]="当前园圃 {0} 格，所选布局 {1} 格。请按对应格数新建或选择布局。",
        ["TextFAEF49B8571D"]="最多种植 {0} 格，已有作物的格位会跳过。\n",
        ["TextFB451A39AF12"]="自动施肥使用的肥料",
        ["TextFC69966E9183"]="{0} 株时间未知，待补记",
        ["TextFDEF305597E9"]="种植：",
        ["Title"]="园圃工作台",
    };
}

}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.Globalization;



public static partial class GardenText
{
    public static Func<string,string>? Resolve;
    public static string L(string key)=>Resolve?.Invoke(key)??Source[key];
    public static string F(string key,params object?[] values)=>string.Format(CultureInfo.CurrentCulture,L(key),values);
}


}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;



// This renderer has no game/service dependency and is exercised in an offline
// native Dear ImGui host. All game actions go through explicit commands.
public sealed class GardenUi
{
    public bool Overview { get; set; }=true;
    public string RequestedTab { get; set; }="";
    public uint SelectedGroup { get; set; }
    public ulong SelectedHouse { get; set; }
    public int SelectedCell { get; set; }
    private int selectedRecordSlot;
    private string activeTab=L("TextC9E0F3954B5C");
    private string nameEdit="",editedLayout="",seedSearch="",soilSearch="",cellSoilSearch="",fertilizerSearch="",shareCode="",shareNotice="",manualTime="",localNotice="";
    private bool showShare,deleteConfirm;
    private int manualAction;
    private int newLayoutSlots=8;
    private IReadOnlyList<int> RecordSlots=>Bed?.NativeSlots is {Count:>0} slots?slots:Binding?.NativeSlots is {Count:>0} saved?saved:Enumerable.Range(0,Bed?.Slots??8).ToArray();
    private int shareDrawFrame=-1;
    private string regionFilter="";
    private string bedNameEdit="",editingBedKey="";
    private readonly GardenSeedClipboard clipboard=new();
    private GardenUiData data=null!;
    private Action<GardenUiCommand> command=null!;
    private float S=>Math.Max(.75f,data.Scale);
    private GardenBedView? Bed=>data.Beds.FirstOrDefault(b=>b.GroupKey==SelectedGroup&&b.HouseId==SelectedHouse);
    private IEnumerable<GardenBedView> VisibleBeds=>data.Beds.Where(b=>regionFilter.Length==0||RegionOf(b)==regionFilter);
    private static string RegionOf(GardenBedView bed)=>string.IsNullOrWhiteSpace(bed.Region)?L("Text97EEED321F5B"):bed.Region;
    private static string BedLabel(GardenBedView bed)=>string.IsNullOrWhiteSpace(bed.Address)?bed.Name:bed.Name+" · "+bed.Address;
    private GardenBinding? Binding=>data.Bindings.FirstOrDefault(b=>b.GroupKey==SelectedGroup&&b.HouseId==SelectedHouse);
    private GardenPreset? Preset=>data.Layouts.FirstOrDefault(p=>p.Id==data.SelectedLayoutId);
    private IEnumerable<GardenRecord> BedRecords=>data.Records.Where(r=>r.HouseId==SelectedHouse&&r.GroupKey==SelectedGroup);
    private void Send(GardenUiCommandKind kind)=>command(new(kind,SelectedHouse,SelectedGroup==0?null:SelectedGroup,data.SelectedLayoutId,SelectedCell));
    private void Save()=>command(new(GardenUiCommandKind.Save));
    private static string Time(DateTimeOffset? value)=>value?.ToLocalTime().ToString("MM/dd HH:mm",CultureInfo.InvariantCulture)??L("Text7450CC5B16E2");
    private string Item(uint id)=>id==0?L("Text23E1665F9447"):data.Seeds.Concat(data.Soils).FirstOrDefault(i=>i.Id==id)?.Name??F("Text4BD2AB225AE6", id);
    public void Draw(GardenUiData model,Action<GardenUiCommand> execute)
    {
        data=model;command=execute;
        if(Bed==null&&data.Beds.Count>0)SelectBed(data.Beds[0]);
        ImGui.PushID("GardenLayoutWorkV2");
        try{
            ImGui.TextUnformatted(L("Text8617B0875973"));ImGui.SameLine();ImGui.TextDisabled(data.Busy?L("Text8F38FD93B5A0"):L("TextB073DB9C0BD6"));
            if(ImGui.Button(Overview?L("TextF6F58196C9DF"):L("Text103FC1ECAA72")))Overview=!Overview;
            ImGui.SameLine();if(ImGui.Button(L("TextA1C82BEE001D")))Send(GardenUiCommandKind.Refresh);
            ImGui.SameLine();using(new Disabled(!data.Busy))if(ImGui.Button(L("Text6687AC8617E4")))Send(GardenUiCommandKind.Stop);
            ImGui.Separator();
            DrawRegionPicker();
            if(Overview)DrawOverview();else DrawWorkbench();
            ImGui.Separator();
            if(!string.IsNullOrEmpty(localNotice))ImGui.TextWrapped(localNotice);
            if(!string.IsNullOrEmpty(data.Notice))ImGui.TextWrapped(data.Notice);
            DrawShare();
        }finally{ImGui.PopID();}
    }
    private void DrawRegionPicker()
    {
        var regions=data.Beds.Select(RegionOf).Distinct().Order().ToList();
        if(regionFilter.Length>0&&!regions.Contains(regionFilter))regionFilter="";
        if(BeginBoundedCombo(L("Text0343BE98DB6E"),regionFilter.Length==0?L("TextF6C02CD10EDC"):regionFilter,250*S)){
            if(ImGui.Selectable(L("TextF6C02CD10EDC"),regionFilter.Length==0))regionFilter="";
            foreach(string region in regions)if(ImGui.Selectable(region,regionFilter==region))regionFilter=region;
            ImGui.EndCombo();
        }
        var visible=VisibleBeds.ToList();
        if(Bed==null||!visible.Contains(Bed)){
            if(visible.Count>0)SelectBed(visible[0]);else{SelectedGroup=0;SelectedHouse=0;}
        }
        ImGui.TextDisabled(L("TextBA615C1A763C"));
    }
    private void SelectBed(GardenBedView b)
    {
        SelectedGroup=b.GroupKey;SelectedHouse=b.HouseId;
        var bound=data.Bindings.FirstOrDefault(x=>x.GroupKey==b.GroupKey&&x.HouseId==b.HouseId);
        if(bound!=null&&data.Layouts.Any(p=>p.Id==bound.LayoutId))data.SelectedLayoutId=bound.LayoutId;
        SelectedCell=0;newLayoutSlots=b.Slots is 4 or 6 or 8?b.Slots:8;selectedRecordSlot=RecordSlots.FirstOrDefault();Save();
    }
    private string DueText(IEnumerable<GardenRecord> records,GardenReminderRule? rule,GardenAction action)
    {
        if(rule==null)return L("Text2F5F1D6FBFB0");
        if(action==GardenAction.Care?!rule.CareEnabled:!rule.FertilizerEnabled)return L("Text6744B4C6A9AA");
        var active=records.Where(GardenLedger.NeedsMaintenance).ToList();
        var due=active.Select(r=>GardenLedger.DueAt(r,rule,action)).Where(t=>t.HasValue).Select(t=>t!.Value).Order().ToList();
        if(due.Count==0)return L("Text6D7517E3B158");
        if(action==GardenAction.Fertilize&&due.Count!=active.Count)return F("TextFC69966E9183", active.Count-due.Count);
        var delta=(action==GardenAction.Fertilize?due[^1]:due[0])-data.Now;
        if(delta<=TimeSpan.Zero)return action==GardenAction.Fertilize?F("Text90E5970B3801", due.Count):F("Text188ACF28DC43", due.Count(t=>t<=data.Now));
        return action==GardenAction.Fertilize?F("TextADF564B6196C", (int)delta.TotalHours, delta.Minutes):F("TextD05E22AE8BE8", (int)delta.TotalHours, delta.Minutes);
    }
    private void DrawOverview()
    {
        ImGui.TextDisabled(F("Text0836BB6E2FBE", data.Bindings.Count));
        if(data.Beds.Count==0){ImGui.Spacing();ImGui.TextWrapped(L("Text2040C3417A5C"));}
        float desiredHeight=Math.Clamp(VisibleBeds.Count()*150*S+16*S,180*S,430*S);
        float bodyHeight=Math.Min(Math.Max(180*S,ImGui.GetContentRegionAvail().Y-110*S),desiredHeight);
        ImGui.BeginChild("GardenOverview",new(0,bodyHeight),true);
        foreach(var b in VisibleBeds){
            ImGui.PushID($"{b.HouseId}-{b.GroupKey}");
            var binding=data.Bindings.FirstOrDefault(x=>x.HouseId==b.HouseId&&x.GroupKey==b.GroupKey);
            var preset=data.Layouts.FirstOrDefault(p=>p.Id==binding?.LayoutId);
            var rows=data.Records.Where(r=>r.HouseId==b.HouseId&&r.GroupKey==b.GroupKey).ToList();
            ImGui.TextWrapped(BedLabel(b));ImGui.TextDisabled(b.Nearby?F("Text1327527E9B22", b.Slots):L("Text55E63C9D2A06"));
            ImGui.TextUnformatted(L("Text7889BE2CF966")+(preset?.Name??L("TextE026C6693DC5")));
            ImGui.TextDisabled(F("Text50924E4237E2", Time(rows.Where(r=>r.Active).Select(r=>r.PlantedAt).Min()), Time(rows.Where(r=>r.Active).Select(r=>r.CaredAt).Min())));
            ImGui.TextUnformatted(L("Text071FBCC539B1")+DueText(rows,binding?.Reminders,GardenAction.Care)+L("Text597DBF065A28")+DueText(rows,binding?.Reminders,GardenAction.Fertilize));
            if(ImGui.Button(L("Text0677F30054B1"))){SelectBed(b);Overview=false;RequestedTab=L("TextC9E0F3954B5C");}
            ImGui.SameLine();if(ImGui.Button(L("Text0DBF5C72C69C"))){SelectBed(b);Overview=false;RequestedTab=L("Text0DBF5C72C69C");}
            using(new Disabled(data.Busy||!b.Nearby)){
                ImGui.SameLine();if(ImGui.Button(L("Text69CA8500A419")))command(new(GardenUiCommandKind.Care,b.HouseId,b.GroupKey));
                ImGui.SameLine();if(ImGui.Button(L("TextDDAE47CD1786")))command(new(GardenUiCommandKind.Fertilize,b.HouseId,b.GroupKey));
            }
            ImGui.Separator();ImGui.PopID();
        }
        ImGui.EndChild();
        if(ImGui.Button(L("Text000CF763ECE4"))){Overview=false;RequestedTab=L("TextC9E0F3954B5C");}
        using(new Disabled(data.Busy)){
            ImGui.SameLine();if(ImGui.Button(L("TextE376E8ADBD45")))command(new(GardenUiCommandKind.Preview));
            ImGui.SameLine();if(ImGui.Button(L("Text1A74696FCD7C")))command(new(GardenUiCommandKind.Plant));
            if(ImGui.Button(L("Text602C8BC3F14E")))command(new(GardenUiCommandKind.Care));
            ImGui.SameLine();if(ImGui.Button(L("TextC21B197E84D3")))command(new(GardenUiCommandKind.Fertilize));
            ImGui.SameLine();if(ImGui.Button(L("Text0E7E37EE6749")))command(new(GardenUiCommandKind.Harvest));
        }
        ImGui.TextDisabled(L("Text6D5C8CF2837A"));
    }
    private void DrawWorkbench()
    {
        if(BeginBoundedCombo(L("Text9F8B0DA2F890"),Bed==null?L("Text1182C5454F11"):BedLabel(Bed),250*S)){
            foreach(var b in VisibleBeds){ImGui.PushID($"{b.HouseId}-{b.GroupKey}");if(ImGui.Selectable(BedLabel(b),b==Bed))SelectBed(b);if(ImGui.IsItemHovered())ImGui.SetTooltip(BedLabel(b));ImGui.PopID();}
            ImGui.EndCombo();
        }
        if(Bed is {} selectedBed)ImGui.TextWrapped(BedLabel(selectedBed));
        if(Bed is {} namedBed){
            string key=GardenNames.Key(namedBed.HouseId,namedBed.GroupKey);
            if(editingBedKey!=key){editingBedKey=key;bedNameEdit=namedBed.Name;}
            bool inlineRename=ImGui.GetContentRegionAvail().X>=360*S;
            ImGui.SetNextItemWidth(inlineRename?220*S:Math.Max(1,ImGui.GetContentRegionAvail().X));ImGui.InputText("##GardenBedName",ref bedNameEdit,81);
            if(inlineRename)ImGui.SameLine();if(ImGui.Button(L("TextB0493E50F288"))){
                try{
                    string name=GardenNames.Set(data.BedNames,namedBed.HouseId,namedBed.GroupKey,bedNameEdit);
                    var binding=Binding;if(binding!=null)binding.Name=name;
                    int at=data.Beds.IndexOf(namedBed);if(at>=0)data.Beds[at]=namedBed with{Name=name};
                    Save();localNotice=L("Text47BDE3679332");
                }catch(ArgumentException e){localNotice=e.Message;}
            }
        }
        if(ImGui.BeginTabBar("GardenWorkbenchTabs")){
            Tab(L("TextC9E0F3954B5C"),DrawLayout);Tab(L("Text0DBF5C72C69C"),DrawRecords);Tab(L("Text73201667202E"),DrawReminders);Tab(L("Text17337902D692"),DrawOperations);
            ImGui.EndTabBar();
        }
        RequestedTab="";
        if(activeTab==L("TextC9E0F3954B5C")){
            using(new Disabled(data.Busy||Bed is not {Nearby:true})){
                if(ImGui.Button(L("Text389C6CAA7B70")))Send(GardenUiCommandKind.Preview);
                ImGui.SameLine();if(ImGui.Button(L("Text33300F84CAC2")))Send(GardenUiCommandKind.Plant);
            }
        }
        using(new Disabled(data.Busy||Bed is not {Nearby:true})){
            if(ImGui.Button(L("TextA31DA82E8C3E")))Send(GardenUiCommandKind.Care);
            ImGui.SameLine();if(ImGui.Button(L("Text883FD78F653E")))Send(GardenUiCommandKind.Fertilize);
            ImGui.SameLine();if(ImGui.Button(L("Text0C5948A18120")))Send(GardenUiCommandKind.Harvest);
        }
    }
    private void Tab(string name,Action draw)
    {
        if(ImGui.BeginTabItem(name,RequestedTab==name?ImGuiTabItemFlags.SetSelected:ImGuiTabItemFlags.None)){
            activeTab=name;
            float height=Math.Clamp(ImGui.GetContentRegionAvail().Y-85*S,250*S,470*S);
            ImGui.BeginChild("tab-"+name,new(0,height),false);draw();ImGui.EndChild();ImGui.EndTabItem();
        }
    }
    private void DrawLayout()
    {
        using(new Disabled(data.Busy)){
            if(BeginBoundedCombo(L("Text972977B48445"),Preset?.Name??L("TextC2C6BB3B7664"),230*S)){
                foreach(var p in data.Layouts){ImGui.PushID(p.Id);if(ImGui.Selectable(p.Name,p==Preset)){data.SelectedLayoutId=p.Id;Save();}ImGui.PopID();}
                ImGui.EndCombo();
            }
            if(BeginBoundedCombo(L("Text20CA79FC956A"),F("Text0558A73DC22D", newLayoutSlots),100*S)){
                foreach(int count in new[]{4,6,8})if(ImGui.Selectable(F("Text0558A73DC22D", count),newLayoutSlots==count))newLayoutSlots=count;
                ImGui.EndCombo();
            }
            if(ImGui.Button(L("TextDCBE4EF55A55"))){
                var p=GardenPlanner.Create(newLayoutSlots,Bed?.Slots==newLayoutSlots?Bed.NativeSlots:null);p.Name=GardenPlanner.UniqueName(F("Text961CB99AFAE8", newLayoutSlots),data.Layouts);data.Layouts.Add(p);data.SelectedLayoutId=p.Id;SelectedCell=0;deleteConfirm=false;Save();
            }
            ImGui.SameLine();if(ImGui.Button(L("Text12F4907D4A1B"))){shareCode="";shareNotice="";showShare=true;}
            var preset=Preset;if(preset==null){ImGui.TextDisabled(L("Text5533D5C44E87"));return;}
            if(editedLayout!=preset.Id){editedLayout=preset.Id;nameEdit=preset.Name;deleteConfirm=false;}
            ImGui.SetNextItemWidth(210*S);ImGui.InputText("##LayoutName",ref nameEdit,65);
            ImGui.SameLine();if(ImGui.Button(L("Text83C37E8A4A5F"))){
                string name=nameEdit.Trim();
                if(name.Length is <1 or >64||name.Any(char.IsControl)||data.Layouts.Any(p=>p.Id!=preset.Id&&string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)))localNotice=L("Text2406FFD195E1");
                else{preset.Name=name;Save();}
            }
            ImGui.SameLine();if(ImGui.Button(L("Text4891F08CD3FF"))){
                var p=new GardenPreset{Name=GardenPlanner.UniqueName(preset.Name+L("Text6DED4E175CD7"),data.Layouts),DefaultSoilId=preset.DefaultSoilId,Cells=preset.Cells.Select(c=>new GardenCell{NativeSlot=c.NativeSlot,SeedId=c.SeedId,SoilId=c.SoilId}).ToList(),PlantOrder=new(preset.PlantOrder)};
                data.Layouts.Add(p);data.SelectedLayoutId=p.Id;Save();
            }
            ImGui.SameLine();if(ImGui.Button(deleteConfirm?L("TextA3EA3C17B401"):L("Text2F9DAA828907"))){
                if(deleteConfirm){data.Layouts.Remove(preset);data.Bindings.RemoveAll(b=>b.LayoutId==preset.Id);data.SelectedLayoutId=data.Layouts.FirstOrDefault()?.Id??"";deleteConfirm=false;Save();return;}deleteConfirm=true;
            }
            using(new Disabled(Bed==null||!Bed.Nearby||preset.Cells.Count!=Bed.Slots))if(ImGui.Button(L("TextCB4FBA11A986")))Send(GardenUiCommandKind.Bind);
            if(Bed!=null&&preset.Cells.Count!=Bed.Slots)ImGui.TextWrapped(F("TextFAC8BA101C60", Bed.Slots, preset.Cells.Count));
            if(Binding!=null){ImGui.SameLine();if(ImGui.Button(L("Text157989A96492")))Send(GardenUiCommandKind.Unbind);}
            ImGui.SameLine();ImGui.TextDisabled(L("TextFAA3DAB035C7")+(data.Layouts.FirstOrDefault(p=>p.Id==Binding?.LayoutId)?.Name??L("TextE026C6693DC5")));
            ImGui.Separator();
            if(preset.Cells==null||preset.Cells.Count is not (4 or 6 or 8)||preset.Cells.Any(c=>c==null)||preset.PlantOrder==null){ImGui.TextWrapped(L("TextC6A489896851"));return;}
            bool columns=ImGui.GetContentRegionAvail().X>=520*S;
            if(columns&&ImGui.BeginTable("GridAndEditor",2,ImGuiTableFlags.SizingStretchProp)){
                ImGui.TableSetupColumn(L("TextFAA2F2ED6735"),ImGuiTableColumnFlags.WidthStretch,1.35f);ImGui.TableSetupColumn(L("TextABE087F4B3C5"),ImGuiTableColumnFlags.WidthStretch,1);
                ImGui.TableNextRow();ImGui.TableNextColumn();DrawGrid(preset);ImGui.TableNextColumn();DrawEditor(preset);ImGui.EndTable();
            }else{DrawGrid(preset);ImGui.Separator();DrawEditor(preset);}
        }
    }
    private void DrawGrid(GardenPreset preset)
    {
        SelectedCell=Math.Clamp(SelectedCell,0,preset.Cells.Count-1);int[] ring=GardenPlanner.Grid(preset.Cells.Count);int columns=preset.Cells.Count==4?2:3;int rows=preset.Cells.Count==8?3:2;
        float width=Math.Max(65*S,(ImGui.GetContentRegionAvail().X-(columns-1)*8*S)/columns);
        for(int r=0;r<rows;r++)for(int c=0;c<columns;c++){
            if(c>0)ImGui.SameLine(0,6*S);int i=ring[r*columns+c];
            if(i<0){ImGui.BeginGroup();ImGui.Dummy(new(width,17*S));ImGui.TextDisabled(F("TextF4CE9FB84387", preset.Cells.Count));ImGui.Dummy(new(width,20*S));ImGui.EndGroup();continue;}
            var cell=preset.Cells[i];bool selected=SelectedCell==i;
            if(selected)ImGui.PushStyleColor(ImGuiCol.Button,ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            string name=Item(cell.SeedId);string shortName=name.Length>9?name[..8]+"…":name;
            if(ImGui.Button(F("TextD6A95703B17E", i+1, shortName, i),new(width,64*S))){SelectedCell=i;seedSearch=cellSoilSearch="";}
            if(selected)ImGui.PopStyleColor();
            if(ImGui.IsItemHovered())ImGui.SetTooltip(name+F("Text67811821AC3B", cell.NativeSlot+1));
        }
        ImGui.TextDisabled(F("Text8C7E4E44CC47", preset.Cells.Count));
        ImGui.TextWrapped(L("Text6724A2574B1D")+string.Join(" > ",preset.PlantOrder.Select(i=>i+1)));
        var record=BedRecords.FirstOrDefault(r=>r.NativeSlot==preset.Cells[SelectedCell].NativeSlot);
        ImGui.TextDisabled(L("TextFDEF305597E9")+Time(record?.PlantedAt));ImGui.TextDisabled(L("Text071FBCC539B1")+Time(record?.CaredAt));ImGui.TextDisabled(L("Text56CBD453708D")+Time(record?.FertilizedAt));
    }
    private bool ItemPicker(string id,string label,List<GardenItemOption> items,ref uint value,ref string search,bool allowEmpty=false)
    {
        ImGui.TextDisabled(label);
        bool changed=false;
        uint selected=value;
        float fieldWidth=Math.Max(1,Math.Min(360*S,ImGui.GetContentRegionAvail().X-4*S));
        if(BeginBoundedCombo(id,value==0?(allowEmpty?L("Text23E1665F9447"):id.Contains("override")?L("Text89A6F0471B72"):L("Text1182C5454F11")):items.FirstOrDefault(i=>i.Id==selected)?.Name??F("Text4BD2AB225AE6", value),fieldWidth)){
            // Never use negative fill-width input inside an auto-sizing popup.
            ImGui.SetNextItemWidth(Math.Max(1,ImGui.GetContentRegionAvail().X));ImGui.InputText(L("Text92065FFB5EE8")+id,ref search,96);
            if(allowEmpty&&ImGui.Selectable(L("Text23E1665F9447"),value==0)){value=0;changed=true;}
            int rendered=0;string query=search;
            foreach(var item in items.Where(i=>string.IsNullOrWhiteSpace(query)||i.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||i.Id.ToString().Contains(query)).Take(250)){
                if(ImGui.Selectable($"{item.Name}##item{item.Id}",item.Id==value)){value=item.Id;changed=true;}
                if(ImGui.IsItemHovered())ImGui.SetTooltip(item.Name);
                rendered++;
            }
            if(rendered==0)ImGui.TextDisabled(L("Text7730996A394D"));ImGui.EndCombo();
        }
        return changed;
    }
    private bool BeginBoundedCombo(string id,string preview,float requestedWidth)
    {
        int marker=id.IndexOf("##",StringComparison.Ordinal);string label=marker<0?id:id[..marker];
        float labelWidth=label.Length==0?0:ImGui.CalcTextSize(label).X+ImGui.GetStyle().ItemInnerSpacing.X;
        float fieldWidth=Math.Max(1,Math.Min(requestedWidth,Math.Max(1,ImGui.GetContentRegionAvail().X-labelWidth)));
        var viewport=ImGui.GetMainViewport().WorkSize;
        float popupWidth=Math.Min(Math.Clamp(fieldWidth,220*S,420*S),Math.Max(1,viewport.X-24*S));
        float popupHeight=Math.Min(320*S,Math.Max(1,viewport.Y-32*S));
        ImGui.SetNextItemWidth(fieldWidth);
        // The parent fixes the popup width before BeginCombo switches windows.
        ImGui.SetNextWindowSizeConstraints(new(popupWidth,0),new(popupWidth,popupHeight));
        return ImGui.BeginCombo(id,preview);
    }
    private void DrawEditor(GardenPreset p)
    {
        var cell=p.Cells[SelectedCell];ImGui.TextWrapped(F("Text0D92C8EF2C4E", SelectedCell+1, cell.NativeSlot+1));
        uint seed=cell.SeedId;if(ItemPicker("##seed",L("Text5E20FC223736"),data.Seeds,ref seed,ref seedSearch,true)){cell.SeedId=seed;Save();}
        using(new Disabled(cell.SeedId==0))if(ImGui.Button(L("TextC6AE39AB184A"))){clipboard.Copy(cell);localNotice=L("TextB42D66998A4D")+Item(clipboard.SeedId);}
        ImGui.SameLine();using(new Disabled(clipboard.SeedId==0))if(ImGui.Button(L("Text42651CE97A5D"))){clipboard.Paste(cell);Save();}
        ImGui.TextDisabled(L("TextC8C545CA59AE"));
        int n=0;foreach(uint id in GardenSeedClipboard.ConfiguredSeeds(data.Layouts)){
            if(n++%2!=0)ImGui.SameLine();if(ImGui.Button(Item(id)+"##reuse"+id)){cell.SeedId=id;Save();}
        }
        uint soil=p.DefaultSoilId;if(ItemPicker("##soil",L("Text02F1A371FB49"),data.Soils,ref soil,ref soilSearch)){p.DefaultSoilId=soil;Save();}
        uint overrideSoil=cell.SoilId;if(ItemPicker("##overrideSoil",L("Text99BD299E6406"),data.Soils,ref overrideSoil,ref cellSoilSearch)){cell.SoilId=overrideSoil;Save();}
        if(ImGui.Button(L("Text2C7E22630DF7"))){cell.SoilId=0;Save();}
        if(ImGui.Button(L("Text79B0A54090DC")))Send(GardenUiCommandKind.Calibrate);
        int order=p.PlantOrder.IndexOf(SelectedCell);
        if(ImGui.Button(L("TextEF5C77416E28"))&&order>0){(p.PlantOrder[order-1],p.PlantOrder[order])=(p.PlantOrder[order],p.PlantOrder[order-1]);Save();}
        ImGui.SameLine();if(ImGui.Button(L("Text2925C65A2BD3"))&&order>=0&&order<p.PlantOrder.Count-1){(p.PlantOrder[order+1],p.PlantOrder[order])=(p.PlantOrder[order],p.PlantOrder[order+1]);Save();}
    }
    private string RecordedSeed(GardenRecord? record)=>record==null?L("TextBDD2CB287011"):record.SeedId!=0?Item(record.SeedId):string.IsNullOrWhiteSpace(record.CropName)?L("TextBDD2CB287011"):F("TextF1BCDC0D31BD", record.CropName);
    private void DrawRecords()
    {
        ImGui.TextDisabled(L("Text7CE5BBCB659E"));
        if(ImGui.BeginTable("GardenRecords",5,ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersInnerH|ImGuiTableFlags.SizingStretchProp)){
            foreach(string label in new[]{L("TextB2ED7CB58887"),L("TextF2B57DBFDD7C"),L("Text1AF52ACC0106"),L("Text245D92BBF960"),L("Text335F69AF03CF")})ImGui.TableSetupColumn(label);
            ImGui.TableHeadersRow();
            foreach(int i in RecordSlots){
                var record=BedRecords.FirstOrDefault(r=>r.NativeSlot==i);ImGui.TableNextRow();ImGui.TableNextColumn();
                if(ImGui.Selectable((i+1)+L("Text7F4660153423")+i,selectedRecordSlot==i,ImGuiSelectableFlags.SpanAllColumns))selectedRecordSlot=i;
                ImGui.TableNextColumn();ImGui.TextWrapped(RecordedSeed(record));if(ImGui.IsItemHovered()&&record?.SeedIdentifiedAt is {} identified)ImGui.SetTooltip(F("Text3B2335DDF624", record.CropName, Time(identified)));ImGui.TextDisabled(GardenCropObservation.Name(record));
                ImGui.TableNextColumn();ImGui.TextWrapped(Time(record?.PlantedAt));ImGui.TableNextColumn();ImGui.TextWrapped(Time(record?.CaredAt));ImGui.TableNextColumn();ImGui.TextWrapped(Time(record?.FertilizedAt));
            }ImGui.EndTable();
        }
        ImGui.TextWrapped(L("TextAA7D07798009"));
        ImGui.Separator();ImGui.TextUnformatted(F("Text8853B77260FF", selectedRecordSlot+1));
        string[] actions={L("TextCFA6207EC025"),L("Text69CA8500A419"),L("TextDDAE47CD1786"),L("TextEA079BC32197")};
        if(BeginBoundedCombo(L("TextED31FBB483EE"),actions[manualAction],150*S)){
            for(int i=0;i<actions.Length;i++)if(ImGui.Selectable(actions[i],manualAction==i))manualAction=i;
            ImGui.EndCombo();
        }
        if(string.IsNullOrEmpty(manualTime))manualTime=data.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ImGui.SetNextItemWidth(210*S);ImGui.InputText(L("Text9F457338B705"),ref manualTime,40);
        using(new Disabled(data.Busy||Bed==null)){
            if(ImGui.Button(L("TextD976DAF6059C"))){
                if(DateTime.TryParseExact(manualTime,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var dt)){
                    var at=new DateTimeOffset(DateTime.SpecifyKind(dt,DateTimeKind.Local));
                    if(at>data.Now.AddMinutes(2))localNotice=L("Text3EBEB20A1809");
                    else command(new(GardenUiCommandKind.ManualRecord,SelectedHouse,SelectedGroup,data.SelectedLayoutId,selectedRecordSlot,(GardenAction)manualAction,at));
                }else localNotice=L("TextC85A760307E9");
            }
        }
        ImGui.SameLine();if(ImGui.Button(L("Text14FE1ED84EE6")))manualTime=data.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ImGui.TextDisabled(L("TextD273BCA035D5"));
    }
    private void DrawReminders()
    {
        var prefs=data.Preferences;bool enabled=prefs.RemindersEnabled;if(ImGui.Checkbox(L("TextCFA7DA4CF992"),ref enabled)){prefs.RemindersEnabled=enabled;Save();}
        bool chat=prefs.ChatReminder;if(ImGui.Checkbox(L("TextD3DF2CB34811"),ref chat)){prefs.ChatReminder=chat;Save();}
        ImGui.SameLine();bool sound=prefs.SoundReminder;if(ImGui.Checkbox(L("Text9CD88EEFAEC2"),ref sound)){prefs.SoundReminder=sound;Save();}
        bool quiet=prefs.QuietInCombat;if(ImGui.Checkbox(L("TextE7FA65456AC4"),ref quiet)){prefs.QuietInCombat=quiet;Save();}
        bool compact=prefs.CompactHome;if(ImGui.Checkbox(L("Text434FF978E57F"),ref compact)){prefs.CompactHome=compact;Save();}
        bool advance=prefs.AdvanceCareTalk;if(ImGui.Checkbox(L("Text829C10C8BFCF"),ref advance)){prefs.AdvanceCareTalk=advance;Save();}
        if(ImGui.IsItemHovered())ImGui.SetTooltip(L("Text5B01BEEF504E"));
        uint fertilizer=data.FertilizerId;if(ItemPicker("##fertilizer",L("TextFB451A39AF12"),data.Fertilizers,ref fertilizer,ref fertilizerSearch)){data.FertilizerId=fertilizer;Save();}
        ImGui.TextDisabled(L("TextB4AF1F2CABF1"));
        ImGui.Separator();var b=Binding;if(b==null){ImGui.TextWrapped(L("Text652411AF1B67"));return;}
        var rule=b.Reminders??=new();bool care=rule.CareEnabled;if(ImGui.Checkbox(L("TextD28629B1F701"),ref care)){rule.CareEnabled=care;Save();}
        int cm=rule.CareMinutes;ImGui.SetNextItemWidth(130*S);if(ImGui.InputInt(L("Text2203B787CB3D"),ref cm)){rule.CareMinutes=Math.Clamp(cm,1,43_200);Save();}
        bool fert=rule.FertilizerEnabled;if(ImGui.Checkbox(L("Text55887CF0F413"),ref fert)){rule.FertilizerEnabled=fert;Save();}
        int fm=rule.FertilizerMinutes;ImGui.SetNextItemWidth(130*S);if(ImGui.InputInt(L("TextF5B941B63FF5"),ref fm)){rule.FertilizerMinutes=Math.Clamp(fm,1,43_200);Save();}
        int repeat=rule.RepeatMinutes;ImGui.SetNextItemWidth(130*S);if(ImGui.InputInt(L("TextDD1384AE67EA"),ref repeat)){rule.RepeatMinutes=Math.Clamp(repeat,0,1440);Save();}
        ImGui.TextUnformatted(L("Text071FBCC539B1")+DueText(BedRecords,rule,GardenAction.Care));ImGui.TextUnformatted(L("Text56CBD453708D")+DueText(BedRecords,rule,GardenAction.Fertilize));
        ImGui.TextWrapped(L("TextAD29A08E1A06"));
    }
    private void DrawOperations()
    {
        ImGui.TextDisabled(L("Text47566C7CA554"));
        foreach(var row in data.Operations.Where(r=>SelectedGroup==0||(r.HouseId==SelectedHouse&&(r.GroupKey==SelectedGroup||r.GroupKey==0))).OrderByDescending(r=>r.At).Take(200))
            ImGui.TextWrapped($"{row.At.ToLocalTime():MM/dd HH:mm:ss}  {GardenLedger.ActionName(row.Action)}  {(row.NativeSlot<0?L("Text79FA1230B3F7"):F("Text5565F8254F5D", row.NativeSlot+1))}  {row.Stage}{(row.Manual?L("Text5B140B922B0D"):"")}");
    }
    private void DrawShare()
    {
        if(!showShare||shareDrawFrame==ImGui.GetFrameCount())return;
        shareDrawFrame=ImGui.GetFrameCount();ImGui.SetNextWindowSize(new(560*S,350*S),ImGuiCond.FirstUseEver);
        if(ImGui.Begin(L("TextF4D8C22939D4"),ref showShare,ImGuiWindowFlags.NoCollapse)){
            ImGui.TextWrapped(L("Text10FE95A596BA"));
            using(new Disabled(Preset==null))if(ImGui.Button(L("Text68A26702EF7F"))){
                try{shareCode=GardenShare.Export(Preset!);shareNotice=L("TextF708841160AB");}catch(Exception e){shareNotice=e.Message;}
            }
            ImGui.SameLine();if(ImGui.Button(L("TextAE71EE588850"))){ImGui.SetClipboardText(shareCode);shareNotice=L("TextE446DD387E50");}
            ImGui.SameLine();if(ImGui.Button(L("TextEF8629C8A5B7"))){string text=ImGui.GetClipboardText();if(text.Length<=GardenShare.MaxCodeLength)shareCode=text;else shareNotice=L("TextD534FA0E2807");}
            ImGui.InputTextMultiline("##GardenShareCode",ref shareCode,GardenShare.MaxCodeLength,new(-1,150*S));
            using(new Disabled(data.Busy))if(ImGui.Button(L("Text89C3159F3EDA"))){
                try{var p=GardenShare.Import(shareCode,data.Layouts,data.Seeds.Select(i=>i.Id).ToHashSet(),data.Soils.Select(i=>i.Id).ToHashSet());data.Layouts.Add(p);data.SelectedLayoutId=p.Id;Save();shareNotice=L("Text0E9B556DA1B4")+p.Name+L("Text487D90DB5B77");}
                catch(Exception e){shareNotice=e.Message;}
            }
            if(!string.IsNullOrEmpty(shareNotice))ImGui.TextWrapped(shareNotice);
        }ImGui.End();
    }
    private readonly struct Disabled:IDisposable
    {
        public Disabled(bool disabled)=>ImGui.BeginDisabled(disabled);
        public void Dispose()=>ImGui.EndDisabled();
    }
}

}


namespace GardenLayout
{
using System;
using System.Collections.Generic;
using System.Linq;



public sealed record GardenItemOption(uint Id,string Name);
public sealed record GardenBedView(ulong HouseId,uint GroupKey,string Name,bool Nearby,int Slots,string Region="",string Address="",IReadOnlyList<int>? NativeSlots=null);
public enum GardenUiCommandKind { Save, Refresh, Bind, Unbind, Calibrate, Preview, Plant, Stop, Care, Fertilize, Harvest, ManualRecord }
public sealed record GardenUiCommand(GardenUiCommandKind Kind,ulong HouseId=0,uint? Group=null,string LayoutId="",int Cell=0,GardenAction Action=GardenAction.Plant,DateTimeOffset? At=null);
public sealed class GardenUiData
{
    public Dictionary<string,string> BedNames { get; set; }=[];
    public List<GardenPreset> Layouts { get; set; }=[];
    public List<GardenBinding> Bindings { get; set; }=[];
    public List<GardenRecord> Records { get; set; }=[];
    public List<GardenOperation> Operations { get; set; }=[];
    public GardenPreferences Preferences { get; set; }=new();
    public List<GardenBedView> Beds { get; set; }=[];
    public List<GardenItemOption> Seeds { get; set; }=[];
    public List<GardenItemOption> Soils { get; set; }=[];
    public List<GardenItemOption> Fertilizers { get; set; }=[];
    public uint FertilizerId { get; set; }
    public string SelectedLayoutId { get; set; }="";
    public string Notice { get; set; }="";
    public bool Busy { get; set; }
    public float Scale { get; set; }=1;
    public DateTimeOffset Now { get; set; }=DateTimeOffset.UtcNow;
    public Func<uint,int> InventoryCount { get; set; }=_=>0;
}
public static class GardenNames
{
    public static string Key(ulong house,uint group)=>$"{house}:{group}";
    public static bool TryParseKey(string key,out ulong house,out uint group)
    {
        house=0;group=0;var parts=key.Split(':');
        return parts.Length==2&&ulong.TryParse(parts[0],out house)&&uint.TryParse(parts[1],out group)&&house!=0&&group!=0;
    }
    public static string Set(Dictionary<string,string> names,ulong house,uint group,string value)
    {
        string name=value.Trim();
        if(house==0||group==0||name.Length is <1 or >40||name.Any(char.IsControl)||name.Contains("##",StringComparison.Ordinal))throw new ArgumentException(L("Text19022E29F9C2"));
        names[Key(house,group)]=name;return name;
    }
}

}


namespace OmniToolbox.TreeHouseLocal
{
using System.Numerics;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools.Info.Game.Data;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Info.Lumina;
using OmenTools.Info.Lumina.ExtraSheets;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using Action = System.Action;
using ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;



[ModuleApiVersion(1)]
public sealed unsafe partial class KotiyaGarden : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface,
        Author = "Sanse",
        Commands = [new("OpenCommand", OmniToolbox.Commands.OmniCommandCatalog.FormatModule(nameof(KotiyaGarden), "open"))]
    };
    public override OmniToolbox.UI.ModuleLocalization Localization { get; } = OmniToolbox.UI.ModuleLocalization.Create(GardenText.Source);
    private Settings config = new();
    private TreeTools.Threading.TaskHelper TaskHelper;
    private bool windowOpen,settingsDirty,stateReady;
    private string stateFile="";
    public override bool HasSettings => true;
    protected override void OnStart(TreeTools.Lifecycle.FeatureLifetime activation)
    {
        GardenText.Resolve=key=>OmniToolbox.UI.OmniLoc.Get(this,key);
        stateFile=Path.Combine(OmniToolbox.Host.DalamudServices.PluginInterface.GetPluginConfigDirectory(),"TreeHouseLocalData",nameof(KotiyaGarden),"state.json");
        stateReady=false;LoadState();stateReady=true;
        TaskHelper=new(){TimeoutMS=10_000};activation.Add(TaskHelper);
        activation.OnStopping(()=>{screenLoaded=false;runningLayout=false;activePlant=null;maintenanceTarget=null;windowOpen=false;TaskHelper.Abort();});
        activation.OnStopping(()=>IAddonLifecycle.Instance().UnregisterListener(OnAddon));
        IAddonLifecycle.Instance().RegisterListener(AddonEvent.PostSetup,"HousingGardening",OnAddon);
        activation.OnStopping(()=>TargetManager.Instance().Unreg(OnSetHardTarget));
        TargetManager.Instance().RegPostSetHardTarget(OnSetHardTarget);
        activation.OnStopping(()=>IChatGui.Instance().ChatMessage-=OnGardenMessage);
        activation.OnStopping(()=>IFramework.Instance().Update-=OnGardenFramework);
        activation.OnStopping(()=>OmniToolbox.Host.DalamudServices.PluginInterface.UiBuilder.Draw-=DrawWindow);
        OmniToolbox.Host.DalamudServices.PluginInterface.UiBuilder.Draw+=DrawWindow;
        runGuard.Clear();cachedBeds=[];houseAddresses.Clear();nextReminderCheck=nextAuditSave=default;runId="";
        activation.OnStopping(()=>{IAddonLifecycle.Instance().UnregisterListener(OnIdentityMenu);pendingCropName=null;});
        IAddonLifecycle.Instance().RegisterListener(AddonEvent.PostSetup,"SelectString",OnIdentityMenu);
        InitGardenScreen();
    }
    protected override void OnStop()
    {
        if(stateReady)SaveLayouts();stateReady=false;screenLoaded=false;windowOpen=false;
        GardenText.Resolve=null;
    }
    protected override bool OnInterruptAutomation()
    {
        if(TaskHelper==null||!TaskHelper.IsBusy)return false;
        AbortLayout(L("Text4017C5B03913"));return true;
    }
    public override bool DrawSettings()
    {
        GardenText.Resolve=key=>OmniToolbox.UI.OmniLoc.Get(this,key);
        if(!screenLoaded){ImGui.TextWrapped(L("Text3BF9B4578364"));return false;}
        DrawGardenScreen();var changed=settingsDirty;settingsDirty=false;return changed;
    }
    public override bool TryHandleCommand(string arguments)
    {
        if(!string.Equals(arguments.Trim(),"open",StringComparison.OrdinalIgnoreCase))return false;
        windowOpen=true;return true;
    }
    private void ToggleOverlayConfig(bool open)=>windowOpen=open;
    private void DrawWindow()
    {
        if(!screenLoaded||!windowOpen)return;
        ImGui.SetNextWindowSize(new(900,800),ImGuiCond.FirstUseEver);
        if(ImGui.Begin(OmniToolbox.UI.OmniLoc.Get(this,"Title")+"###KotiyaGarden",ref windowOpen,ImGuiWindowFlags.NoCollapse))DrawGardenScreen();
        ImGui.End();
    }

    #region 事件

    private void OnAddon
    (
        AddonEvent type,
        AddonArgs  args
    )
    {
        OnLayoutPlantAddon();
    }

    private void OnSetHardTarget
    (
        bool         result,
        IGameObject? target,
        bool         checkMode,
        bool         a4,
        int          a5
    )
    {
        var housing=HousingManager.Instance();
        if(housing==null||housing->OutdoorTerritory==null){ToggleOverlayConfig(false);return;}

        switch (target)
        {
            case null:
                ToggleOverlayConfig(false);
                break;

            case { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj, DataID: 2003757 }:
                ToggleOverlayConfig(true);
                break;

            default:
                ToggleOverlayConfig(false);
                break;
        }
    }

    #endregion

    #region 流程

    private void StartAction
    (
        string entryKeyword,
        Action extraAction = null,
        uint? group = null
    )
    {
        if (TaskHelper.IsBusy || !IsEnvironmentValid(out var objectIDs)) return;
        var kind=entryKeyword==TextForAction(4)?GardenLayout.GardenAction.Care:entryKeyword==TextForAction(3)?GardenLayout.GardenAction.Fertilize:GardenLayout.GardenAction.Harvest;
        try{objectIDs=ReadNearbyBeds().Where(b=>!group.HasValue||b.Group==group.Value).SelectMany(b=>b.Targets).Select(t=>t.ObjectId).ToList();}
        catch(Exception e){layoutNotice=e.Message;return;}
        if(objectIDs.Count==0){layoutNotice=L("TextBD1FE9882AFE");return;}
        if(ICondition.Instance()[ConditionFlag.OccupiedInQuestEvent]){layoutNotice=L("Text421E5EBBB3CE");return;}
        if(kind==GardenAction.Fertilize){
            runFertilizerId=GardenFertilizer.Resolve(config.FertilizerSelected);
            if(!CheckFertilizerStock(out var reason)){layoutNotice=reason;return;}
        }
        StartRun(kind);

        TaskHelper.Enqueue(() => TargetManager.Target = null, L("Text2C6660E7E9CC"));

        foreach (var garden in objectIDs)
        {
            TaskHelper.Enqueue(() => BeginMaintenanceStep(garden,kind), F("Text39F752BE477C", GardenLayout.GardenLedger.ActionName(kind)));
            TaskHelper.Enqueue(() => ClickGardenEntryByText(entryKeyword),       L("TextDC71485F8EE9"));
            extraAction?.Invoke();
            TaskHelper.Enqueue(FinishMaintenanceStep,L("Text0A44756FD758"),timeoutAction:()=>AbortLayout(L("Text53E3474DEC12")));
        }
        TaskHelper.Enqueue(()=>{maintenanceTarget=null;layoutNotice=F("Text752199EC7CC8", GardenLayout.GardenLedger.ActionName(kind), maintenanceDone, maintenanceSkipped);Audit(kind,null,layoutNotice);});
    }

    private void StartGather(uint? group=null) =>
        StartAction(LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(6).Text.ToString(),group:group);

    private void StartTend(uint? group=null) =>
        StartAction(LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(4).Text.ToString(),group:group);

    private void StartFertilize(uint? group=null) =>
        StartAction
        (
            LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(3).Text.ToString(),
            () =>
            {
                QueueFertilizerStage(CheckFertilizerState,L("Text97B87A080F53"));
                QueueFertilizerStage(ClickFertilizer,L("Text9F858079CF5D"));
                QueueFertilizerStage(SelectFertilizerContext,L("TextBABB1AD8BDA5"));
            },
            group
        );

    #endregion

    #region 工具

    private static bool IsEnvironmentValid
    (
        out List<ulong> objectIDs
    )
    {
        objectIDs = [];

        if (IObjectTable.Instance().LocalPlayer == null) return false;

        var manager = HousingManager.Instance();
        if (manager == null) return false;

        // 不在房区里
        var outdoorZone = manager->OutdoorTerritory;
        if (outdoorZone == null) return false;

        var houseID = (ulong)manager->GetCurrentHouseId();
        if (houseID == 0) return false;

        // 在自己有权限的房子院子里
        // 具体怎么个有权限法不想测了
        foreach (var type in Enum.GetValues<EstateType>())
        {
            if (type == EstateType.SharedEstate)
            {
                for (var i = 0; i < 2; i++)
                {
                    var typeHouseID = HousingManager.GetOwnedHouseId(type, i);
                    if (typeHouseID == houseID)
                        goto Out;
                }
            }
            else
            {
                var typeHouseID = HousingManager.GetOwnedHouseId(type);
                if (typeHouseID == houseID)
                    goto Out;
            }
        }

        return false;

        Out: ;

        // 找一下有没有园圃
        return TryObtainGardensAround(out objectIDs);
    }

    private static bool TryObtainGardensAround
    (
        out List<ulong> objectIDs
    )
    {
        objectIDs = [];

        var outdoorZone = HousingManager.Instance()->OutdoorTerritory;
        if (outdoorZone == null) return false;

        // 找一下有没有园圃
        List<(ulong GameObjectID, Vector3 Position)> gardenCenters = [];

        foreach (var housingObj in outdoorZone->FurnitureManager.ObjectManager.ObjectArray.Objects)
        {
            if (housingObj == null || housingObj.Value == null) continue;
            if (housingObj.Value->ObjectKind                              != ObjectKind.HousingEventObject) continue;
            if (GardenLayout.GardenPlanner.FurnitureSlots(housingObj.Value->BaseId) == 0) continue;
            if (LocalPlayerState.DistanceTo3D(housingObj.Value->Position) > 10) continue;

            gardenCenters.Add(new(housingObj.Value->GetGameObjectId(), housingObj.Value->Position));
        }

        if (gardenCenters.Count == 0) return false;

        // 园圃家具周围绕一圈的那个实际可交互的坑位
        objectIDs = IObjectTable.Instance()
                            .Where
                            (x => x is { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj, DataID: 2003757 } &&
                                  gardenCenters.Any(g => Vector3.DistanceSquared(x.Position, g.Position) <= 25)
                            )
                            .Select(x => x.GameObjectID)
                            .ToList();
        return objectIDs.Count > 0;
    }

    private uint runFertilizerId;
    private bool CheckFertilizerStock(out string reason)
    {
        var status=GardenNativeInventory.FindCarried(runFertilizerId,out _);
        reason=status==GardenStockStatus.Unavailable?L("TextF77E9DDF6221"):F("TextB81F66B01C61", ItemName(runFertilizerId));
        return status==GardenStockStatus.Available;
    }
    private void QueueFertilizerStage(Func<bool> poll,string stage)
    {
        bool announced=false;
        TaskHelper.Enqueue(()=>{
            if(maintenanceTarget==null)return true;
            if(!announced){announced=true;Audit(GardenAction.Fertilize,maintenanceTarget,stage,maintenanceHouse);}
            return poll();
        },stage,timeoutAction:()=>AbortLayout(F("Text0EF8DBB8602F", stage)),exceptionAction:()=>AbortLayout(F("TextE4DB283E93BE", stage)));
    }
    private bool CheckFertilizerState()
    {
        if(maintenanceTarget==null||!maintenanceChosen||maintenanceRejected||maintenanceSuccess)return true;
        if (!MaintenanceContextValid()) { AbortLayout(L("TextF0681C3609D9")); return true; }
        if (SelectString != null && SelectString->IsAddonAndNodesReady()) return false;

        return (Inventory!=null&&Inventory->IsVisible) ||
               (InventoryLarge!=null&&InventoryLarge->IsVisible) ||
               (InventoryExpansion!=null&&InventoryExpansion->IsVisible);
    }

    private bool ClickFertilizer()
    {
        if(maintenanceTarget==null||!maintenanceChosen||maintenanceRejected||maintenanceSuccess||fertilizerOpened)return true;
        if(!MaintenanceContextValid()){AbortLayout(L("TextF0681C3609D9"));return true;}
        if (SelectString != null && SelectString->IsAddonAndNodesReady()) return false;
        if (!ICondition.Instance()[ConditionFlag.OccupiedInQuestEvent]) return false;

        if (GardenNativeInventory.FindCarried(runFertilizerId,out var fertilizerItem)!=GardenStockStatus.Available)
        {
            AbortLayout(F("TextD2E7B4FBF6D4", ItemName(runFertilizerId)));
            return true;
        }

        AgentInventoryContext.Instance()->
            OpenForItemSlot
        (
            fertilizerItem.Container,
            fertilizerItem.Slot,
            0,
            AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory)->AddonId
        );

        fertilizerOpened=true;
        return true;
    }
    private bool SelectFertilizerContext()
    {
        if(maintenanceTarget==null||!maintenanceChosen||maintenanceRejected||maintenanceSuccess||!fertilizerOpened||fertilizerSubmitted)return true;
        if(!MaintenanceContextValid()){AbortLayout(L("TextF0681C3609D9"));return true;}
        fertilizerSubmitted=AddonContextMenuEvent.Select(TextForAction(3));return fertilizerSubmitted;
    }

    private bool ClickGardenEntryByText
    (
        string text
    )
    {
        if(maintenanceTarget==null)return true;
        if (!SelectString->IsAddonAndNodesReady())
            return false;

        FlushCropIdentity();
        if(maintenanceMenuSent)return true;
        if(!MaintenanceContextValid()){AbortLayout(L("Text920A1670F732"));return true;}
        if(!TryReadGardenMenu(out var menu))return false;
        ObserveMaintenanceCrop(menu.Crop);
        string reason=menu.SkipReason(currentAction);
        bool chosen=reason==null;
        if(!AddonSelectStringEvent.TryScanSelectStringText(chosen?text:TextForAction(1),out var index)){AbortLayout(L("Text9E2A52E6F634"));return true;}
        if(!chosen){maintenanceSkipReason=reason;}
        maintenanceMenuSent=true;maintenanceChosen=chosen;
        if(!AddonSelectStringEvent.Select(index)){maintenanceMenuSent=maintenanceChosen=false;return false;}
        return true;
    }

    #endregion

    private sealed class Settings
    {
        public uint FertilizerSelected = GardenFertilizer.DefaultItemId;
        public uint SeedSelected = 0;
        public uint SoilSelected = 0;
        public List<GardenLayout.GardenPreset> Layouts = [];
        public string SelectedLayoutID = string.Empty;
        public List<GardenLayout.GardenBinding> GardenBindings = [];
        public List<GardenLayout.GardenRecord> Records = [];
        public List<GardenLayout.GardenOperation> Operations = [];
        public GardenLayout.GardenPreferences Preferences = new();
        public Dictionary<string,string> GardenNames = [];
        public Dictionary<string,List<int>> GardenSlots = [];
    }
}



}


namespace OmniToolbox.TreeHouseLocal
{
using GardenLayout;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.Info.Lumina;
using OmenTools.Interop.Game.Lumina;



public unsafe partial class KotiyaGarden
{
    private List<GardenCropDefinition> cropDefinitions=[];
    private GardenNameObservation pendingCropName;
    private void InitCropIdentity()
    {
        pendingCropName=null;
        cropDefinitions=Sheets.Seeds.Values.Select(seed=>{
            var crop=LuminaGetter.GetRowOrDefault<Lumina.Excel.Sheets.GardeningSeed>(seed.AdditionalData.RowId).Item;
            return new GardenCropDefinition(seed.RowId,seed.Name.ToString(),crop.RowId,crop.Value.Name.ToString());
        }).Where(d=>d.CropItemId!=0&&!string.IsNullOrWhiteSpace(d.CropName)).ToList();
    }
    private void CaptureCropName(string body)
    {
        if(!IFramework.Instance().IsInFrameworkUpdateThread)return;
        var name=GardenCropIdentity.ParseName(body);if(name==null)return;
        var crop=GardenCropIdentity.Resolve(name,cropDefinitions);if(crop==null)return;
        var manager=HousingManager.Instance();
        if(manager==null||manager->OutdoorTerritory==null||!TryReadGarden(TargetManager.Target,out var target))return;
        ulong house=(ulong)manager->GetCurrentHouseId();if(house==0)return;
        pendingCropName=new(house,target.GroupKey,target.NativeSlot,target.ObjectId,DateTimeOffset.UtcNow,crop);
        FlushCropIdentity();
    }
    private void OnIdentityMenu(AddonEvent kind,AddonArgs args)=>FlushCropIdentity();
    private void FlushCropIdentity()
    {
        var pending=pendingCropName;if(!screenLoaded||pending==null)return;
        var now=DateTimeOffset.UtcNow;
        if(now-pending.At>TimeSpan.FromSeconds(2)){pendingCropName=null;return;}
        var manager=HousingManager.Instance();
        if(manager==null||manager->OutdoorTerritory==null||!TryReadGarden(TargetManager.Target,out var target)){pendingCropName=null;return;}
        ulong house=(ulong)manager->GetCurrentHouseId();
        if(house!=pending.HouseId||target.ObjectId!=pending.ObjectId){pendingCropName=null;return;}
        var menu=SelectString;if(!menu->IsAddonAndNodesReady())return;
        // Title node 2 was verified from the native menu. Copy the full SDK text node/string.
        var pointer=menu->GetTextNodeById(2);if(pointer==null)return;
        var node=*pointer;
        if(!GardenNativeMenu.TryReadTitle(node,out var title))return;
        if(!GardenCropIdentity.Matches(pending,house,target,title,now))return;
        pendingCropName=null;
        var record=GardenLedger.GetOrCreate(config.Records,house,target.GroupKey,target.NativeSlot);
        if(!GardenCropIdentity.Apply(record,pending.Crop,now))return;
        Audit(GardenAction.Identify,target,F("Text29C0D3363F93", pending.Crop.CropName),house);
        SaveLayouts();
    }
}


}


namespace OmniToolbox.TreeHouseLocal
{
using GardenLayout;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Game.ClientState.Conditions;
using OmenTools.Info.Game.Data;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Info.Lumina;
using OmenTools.Info.Lumina.ExtraSheets;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.Lumina;



public unsafe partial class KotiyaGarden
{
    private string layoutNotice="";
    private bool runningLayout,seedSubmitted;
    private PlantStep activePlant;
    private ulong planHouse;
    private int seedBefore,soilBefore,plantedCount,skippedCount;
    private sealed record Bed(uint Group,ulong House,List<GardenTarget> Targets,System.Numerics.Vector3 Position);
    private List<Bed> cachedBeds=[];

    private GardenPreset SelectedLayout=>config.Layouts.FirstOrDefault(p=>p.Id==config.SelectedLayoutID);
    private void SaveLayouts(){PersistState();settingsDirty=true;auditDirty=false;}
    private void EnsureLayouts()
    {
        config.Layouts??=[];
        config.GardenBindings??=[];
        if(SelectedLayout==null&&config.Layouts.Count>0){config.SelectedLayoutID=config.Layouts[0].Id;}
    }
    private string ItemName(uint itemId)
    {
        if(itemId==0)return L("Text23E1665F9447");
        if(Sheets.Seeds.TryGetValue(itemId,out var seed))return seed.Name.ToString();
        if(Sheets.Soils.TryGetValue(itemId,out var soil))return soil.Name.ToString();
        return F("Text4BD2AB225AE6", itemId);
    }

    private List<Bed> ReadNearbyBeds()
    {
        if(!IsEnvironmentValid(out var nearby))throw new InvalidOperationException(L("Text2D14BCEAA413"));
        ulong house=(ulong)HousingManager.Instance()->GetCurrentHouseId();
        var list=new List<(GardenTarget Target,System.Numerics.Vector3 Position)>();
        foreach(var id in nearby){var obj=IObjectTable.Instance().FirstOrDefault(x=>x.GameObjectID==id);if(TryReadGarden(obj,out var target))list.Add((target,obj.Position));}
        var beds=list.GroupBy(x=>x.Target.GroupKey).OrderBy(g=>g.Key).Select(g=>new Bed(g.Key,house,g.Select(x=>x.Target).OrderBy(t=>t.NativeSlot).ToList(),g.First().Position)).ToList();
        foreach(var bed in beds){
            int expected=GardenSizeAt(bed.Position);
            if(expected==0||bed.Targets.Count!=expected||bed.Targets.Select(t=>t.NativeSlot).Distinct().Count()!=expected)throw new InvalidOperationException(L("Text784265790450"));
            config.GardenSlots[GardenLayout.GardenNames.Key(house,bed.Group)]=bed.Targets.Select(t=>t.NativeSlot).ToList();
        }
        return beds;
    }
    private static int GardenSizeAt(System.Numerics.Vector3 position)
    {
        var manager=HousingManager.Instance();if(manager==null||manager->OutdoorTerritory==null)return 0;
        int match=0;
        foreach(var obj in manager->OutdoorTerritory->FurnitureManager.ObjectManager.ObjectArray.Objects){
            if(obj==null||obj.Value==null)continue;
            var copy=*obj.Value;
            if(copy.ObjectKind!=FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.HousingEventObject||System.Numerics.Vector3.DistanceSquared(copy.Position,position)>.25f)continue;
            int count=GardenPlanner.FurnitureSlots(copy.BaseId);if(count==0)continue;
            if(match!=0)return 0;match=count;
        }
        return match;
    }
    private static bool TryReadGarden(IGameObject obj,out GardenTarget target)
    {
        target=null;
        if(obj is not {ObjectKind:Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj,DataID:2003757}||!obj.IsValid())return false;
        var pointer=(EventObject*)obj.ToStruct();if(pointer==null)return false;
        var copy=*pointer;
        if(copy.BaseId!=2003757||copy.Arg!=copy.GimmickId)return false;
        try{target=GardenPlanner.Decode(obj.GameObjectID,copy.BaseId,copy.Arg);return true;}catch(ArgumentException){return false;}
    }
    private List<PlantStep> BuildPlantPlan(uint? scope=null)
    {
        var beds=ReadNearbyBeds();var plan=new List<PlantStep>();
        foreach(var bed in beds.Where(b=>!scope.HasValue||b.Group==scope.Value)){
            var binding=config.GardenBindings.FirstOrDefault(b=>b.HouseId==bed.House&&b.GroupKey==bed.Group);
            if(binding==null)continue;
            var preset=config.Layouts.FirstOrDefault(p=>p.Id==binding.LayoutId)??throw new InvalidOperationException(L("TextF65434A68EC2"));
            if(!GardenPlanner.AnchorMatches(binding,bed.Position.X,bed.Position.Y,bed.Position.Z))throw new InvalidOperationException(L("Text83486CCBDA09"));
            plan.AddRange(GardenPlanner.Plan(preset,bed.Targets,bed.Group,false));
        }
        if(plan.Count==0)throw new InvalidOperationException(L("Text1FE2D5804F7A"));
        if(plan.Any(p=>!Sheets.Seeds.ContainsKey(p.SeedId)||!Sheets.Soils.ContainsKey(p.SoilId)))throw new InvalidOperationException(L("Text528F47200A92"));
        return plan;
    }
    private void PreviewLayout(uint? scope=null)
    {
        try{
            var plan=BuildPlantPlan(scope);var materials=GardenPlanner.Materials(plan);
            layoutNotice=F("TextFAEF49B8571D", plan.Count)+string.Join("\n",materials.Select(m=>F("Text2A71C6CF699B", ItemName(m.Key), m.Value, LocalPlayerState.GetItemCount(m.Key))));
        }catch(Exception e){layoutNotice=e.Message;}
    }
    private void StartLayoutPlant(uint? scope=null)
    {
        if(TaskHelper.IsBusy)return;
        if(ICondition.Instance()[ConditionFlag.OccupiedInQuestEvent]){layoutNotice=L("Text2E824D04771A");return;}
        try{
            var plan=BuildPlantPlan(scope);planHouse=(ulong)HousingManager.Instance()->GetCurrentHouseId();
            runningLayout=true;activePlant=null;plantedCount=skippedCount=0;StartRun(GardenLayout.GardenAction.Plant);
            foreach(var step in plan){
                GardenPlantSequence.Enqueue(
                    (poll,name)=>TaskHelper.Enqueue(WrapPlantStage(poll,name,step),name,timeoutAction:()=>AbortLayout(F("TextA4696F56343F", name)),exceptionAction:()=>AbortLayout(F("Text467FA68CF154", name))),
                    ()=>BeginPlantStep(step),SelectPlantAction,()=>FillSeedAndSoil(step),()=>ConfirmPlannedSeed(step),FinishPlantStep);
                TaskHelper.DelayNext(250);
            }
            TaskHelper.Enqueue(()=>{runningLayout=false;activePlant=null;layoutNotice=F("TextCE9E55A6D3D7", plantedCount, skippedCount);Audit(GardenLayout.GardenAction.Plant,null,layoutNotice);});
        }catch(Exception e){AbortLayout(e.Message);}
    }
    private bool ContextValid(PlantStep step)
    {
        var housing=HousingManager.Instance();
        if(housing==null||housing->OutdoorTerritory==null||(ulong)housing->GetCurrentHouseId()!=planHouse)return false;
        var obj=IObjectTable.Instance().FirstOrDefault(x=>x.GameObjectID==step.ObjectId);
        if(!TryReadGarden(obj,out var actual)||actual.GroupKey!=step.GroupKey||actual.NativeSlot!=step.NativeSlot)return false;
        var binding=config.GardenBindings.FirstOrDefault(b=>b.HouseId==planHouse&&b.GroupKey==step.GroupKey);
        return binding!=null&&GardenPlanner.AnchorMatches(binding,obj.Position.X,obj.Position.Y,obj.Position.Z);
    }
    private bool BeginPlantStep(PlantStep step)
    {
        if(!ContextValid(step)){AbortLayout(L("Text9E614859CC0B"));return true;}
        if(!runGuard.Begin(planHouse,new(step.ObjectId,step.GroupKey,step.NativeSlot),GardenLayout.GardenAction.Plant)){activePlant=null;return true;}
        activePlant=step;seedSubmitted=false;plantMenuSent=plantConfirmed=false;
        TargetManager.Target=IObjectTable.Instance().First(x=>x.GameObjectID==step.ObjectId);
        layoutNotice=F("Text971478D8FE35", step.NativeSlot+1, ItemName(step.SeedId));
        new EventStartPackt(step.ObjectId,721047).Send();return true;
    }
    private bool SelectPlantAction()
    {
        if(activePlant==null||plantMenuSent)return true;
        if(!SelectString->IsAddonAndNodesReady())return false;
        FlushCropIdentity();
        if(!ContextValid(activePlant)){AbortLayout(L("TextDEE8C7D2C9FE"));return true;}
        if(!TryReadGardenMenu(out var menu))return false;
        var row=GardenLedger.GetOrCreate(config.Records,planHouse,activePlant.GroupKey,activePlant.NativeSlot);GardenCropObservation.Apply(row,menu.Crop,DateTimeOffset.UtcNow);SaveLayouts();
        string plant=TextForAction(2);
        if(menu.Plant.Enabled==true&&AddonSelectStringEvent.TryScanSelectStringText(plant,out var index)){plantMenuSent=AddonSelectStringEvent.Select(index);return plantMenuSent;}
        if(!AddonSelectStringEvent.TryScanSelectStringText(TextForAction(1),out var cancel)){AbortLayout(L("Text12AB8A67005E"));return true;}
        AddonSelectStringEvent.Select(cancel);activePlant=null;skippedCount++;return true;
    }
    private static string TextForAction(uint row)=>LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(row).Text.ToString();
    private void OnLayoutPlantAddon()
    {
        // Observation only: never append work behind the task waiting for its result.
        if(runningLayout&&activePlant!=null)layoutNotice=F("Text20221D19F6CD", activePlant.NativeSlot+1);
    }
    private bool ConfirmPlannedSeed(PlantStep step)
    {
        if(!runningLayout||!seedSubmitted||activePlant!=step||plantConfirmed)return true;
        if(!ContextValid(step)){AbortLayout(L("Text982D422E91C2"));return true;}
        if(MaterialsConsumed(step))return true; // Another enabled helper may already have confirmed.
        layoutNotice=F("TextD77B9713AB0B", step.NativeSlot+1);
        plantConfirmed=AddonSelectYesnoEvent.ClickYes();return plantConfirmed;
    }
    private bool FillSeedAndSoil(PlantStep step)
    {
        if(!runningLayout||activePlant!=step||seedSubmitted)return true;
        if(!ContextValid(step)||TargetManager.Target?.GameObjectID!=step.ObjectId){AbortLayout(L("Text20CACA619CDC"));return true;}
        var agent=AgentHousingPlant.Instance();
        if(agent==null||!agent->IsAddonReady()||!agent->IsAddonShown())return false;
        if(!Inventories.Player.TryGetFirstItem(x=>x.ItemId==step.SeedId,out var seed)||!Inventories.Player.TryGetFirstItem(x=>x.ItemId==step.SoilId,out var soil)){
            AbortLayout(F("TextC3453C639ACA", ItemName(step.SeedId), ItemName(step.SoilId)));return true;
        }
        seedBefore=(int)LocalPlayerState.GetItemCount(step.SeedId);soilBefore=(int)LocalPlayerState.GetItemCount(step.SoilId);
        agent->SelectedItems[0]=new(){ItemId=soil->ItemId,InventoryType=soil->Container,InventorySlot=(ushort)soil->Slot};
        agent->SelectedItems[1]=new(){ItemId=seed->ItemId,InventoryType=seed->Container,InventorySlot=(ushort)seed->Slot};
        agent->ConfirmSeedAndSoilSelection();seedSubmitted=true;layoutNotice=F("Text7173CF39808B", step.NativeSlot+1);return true;
    }
    private bool MaterialsConsumed(PlantStep step)=>LocalPlayerState.GetItemCount(step.SeedId)==seedBefore-1&&LocalPlayerState.GetItemCount(step.SoilId)==soilBefore-1;
    private bool FinishPlantStep()
    {
        if(ICondition.Instance()[ConditionFlag.OccupiedInQuestEvent])return false;
        if(activePlant!=null){
            if(!seedSubmitted)return false;
            if(!runningLayout)return true;
            if(!ContextValid(activePlant)){AbortLayout(L("TextEAAD260F5E90"));return true;}
            if(!MaterialsConsumed(activePlant)){layoutNotice=F("Text9AEA47BEF8B9", activePlant.NativeSlot+1);return false;}
            RecordPlantConfirmed(activePlant);plantedCount++;activePlant=null;
        }
        return true;
    }
    private void AbortLayout(string text){TaskHelper?.Abort();runningLayout=false;activePlant=null;maintenanceTarget=null;layoutNotice=text;Audit(currentAction,null,text);}
}

}


namespace OmniToolbox.TreeHouseLocal
{
using GardenLayout;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.Interop.Game.AddonEvent;



public unsafe partial class KotiyaGarden
{
    private bool MaintenanceContextValid()
    {
        var target=maintenanceTarget;
        var manager=HousingManager.Instance();
        if(target==null||manager==null||manager->OutdoorTerritory==null||(ulong)manager->GetCurrentHouseId()!=maintenanceHouse||TargetManager.Target?.GameObjectID!=target.ObjectId)return false;
        return TryReadGarden(IObjectTable.Instance().FirstOrDefault(x=>x.GameObjectID==target.ObjectId),out var actual)&&actual.GroupKey==target.GroupKey&&actual.NativeSlot==target.NativeSlot;
    }
    private void ObserveMaintenanceCrop(GardenCropState state)
    {
        if(maintenanceTarget==null||state==GardenCropState.Unknown)return;
        var row=GardenLedger.GetOrCreate(config.Records,maintenanceHouse,maintenanceTarget.GroupKey,maintenanceTarget.NativeSlot);
        GardenCropObservation.Apply(row,state,DateTimeOffset.UtcNow);SaveLayouts();
    }
    private static bool TryReadGardenMenu(out GardenMenuState menu)
    {
        menu=null;
        var basePointer=SelectString;
        if(!basePointer->IsAddonAndNodesReady())return false;
        var pointer=(FFXIVClientStructs.FFXIV.Client.UI.AddonSelectString*)basePointer;
        // Resolve each frame, copy complete SDK structures, and bound all list indices.
        var addon=*pointer;
        var popup=addon.PopupMenu;
        if(popup.EntryCount is <1 or >16||popup.List==null||addon.AtkValues==null||addon.AtkValuesCount<7+popup.EntryCount)return false;
        var list=*popup.List;
        if(!GardenNativeMenu.TrySnapshot(list,popup.EntryCount,out var enabled))return false;
        GardenMenuOption Option(uint row)
        {
            string label=TextForAction(row);
            if(string.IsNullOrWhiteSpace(label)||!AddonSelectStringEvent.TryScanSelectStringText(label,out int index))return new(false,null);
            if(index<0||index>=popup.EntryCount||index>=list.ListLength)return new(true,null);
            return new(true,enabled[index]);
        }
        menu=new(Option(2),Option(4),Option(3),Option(6));
        return true;
    }
}

}


namespace OmniToolbox.TreeHouseLocal
{
using GardenLayout;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.Info.Game.Data;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Info.Lumina;
using System.Runtime.InteropServices;



public unsafe partial class KotiyaGarden
{
    private GardenUi screen=new();
    private GardenUiData screenData=new();
    private readonly GardenRunGuard runGuard=new();
    private string runId="";
    private GardenAction currentAction;
    private GardenTarget maintenanceTarget;
    private ulong maintenanceHouse;
    private bool maintenanceMenuSent,maintenanceChosen,maintenanceSuccess,plantMenuSent,plantConfirmed;
    private bool maintenanceRejected,fertilizerOpened,fertilizerSubmitted,careTalkAdvanced;
    private string maintenanceSkipReason="";
    private int maintenanceDone,maintenanceSkipped;
    private DateTimeOffset nextReminderCheck,nextAuditSave;
    private bool auditDirty;
    private bool screenLoaded;
    private readonly Dictionary<ulong,(string Region,string Address)> houseAddresses=new();
    private (string Region,string Address) DescribeHouse(ulong house)
    {
        if(houseAddresses.TryGetValue(house,out var saved))return saved;
        var value=GardenAddress.Decode(house);
        if(value==null)return (L("Text97EEED321F5B"),L("Text784E422B5D57"));
        string world=OmenTools.Interop.Game.Lumina.LuminaGetter.GetRowOrDefault<Lumina.Excel.Sheets.World>(value.WorldId).Name.ToString();
        string region=OmenTools.Interop.Game.Lumina.LuminaGetter.GetRowOrDefault<Lumina.Excel.Sheets.TerritoryType>(value.TerritoryTypeId).PlaceName.Value.Name.ToString();
        if(string.IsNullOrWhiteSpace(world))world=F("Text3F3A45E19297", value.WorldId);
        if(string.IsNullOrWhiteSpace(region))region=F("Text2B8FD881E295", value.TerritoryTypeId);
        return houseAddresses[house]=(region,value.Describe(world,region));
    }
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool MessageBeep(uint type);

    private string layoutRecoveryNotice="";
    private void InitGardenScreen()
    {
        config.FertilizerSelected=GardenFertilizer.Resolve(config.FertilizerSelected);
        EnsureLayouts();config.Records??=[];config.Operations??=[];config.Preferences??=new();config.GardenNames??=[];config.GardenSlots??=[];
        layoutRecoveryNotice="";bool recoveredLayouts=false;
        try{
            var recovery=GardenLayoutRecovery.Apply(config.Layouts,()=>GardenLayoutRecovery.Backup(File.Exists(stateFile)?stateFile:OmniToolbox.Host.DalamudServices.PluginInterface.ConfigFile.FullName));
            recoveredLayouts=recovery.Repaired>0;
            if(recovery.Repaired>0)layoutRecoveryNotice=F("Text9C59580BDE9A", recovery.Repaired);
            if(recovery.Unresolved>0)layoutRecoveryNotice+=F("Text6CBBFCB945BB", recovery.Unresolved);
        }catch(Exception e){layoutRecoveryNotice=L("TextD19C08D4C752")+e.Message;}
        foreach(var b in config.GardenBindings)b.Reminders??=new();
        screenData=new(){BedNames=config.GardenNames,Layouts=config.Layouts,Bindings=config.GardenBindings,Records=config.Records,Operations=config.Operations,Preferences=config.Preferences,SelectedLayoutId=config.SelectedLayoutID,
            Seeds=Sheets.Seeds.Values.Select(x=>new GardenItemOption(x.RowId,x.Name.ToString())).OrderBy(x=>x.Name).ToList(),
            Soils=Sheets.Soils.Values.Select(x=>new GardenItemOption(x.RowId,x.Name.ToString())).OrderBy(x=>x.Name).ToList(),
            Fertilizers=Sheets.Fertilizers.Values.Select(x=>new GardenItemOption(x.RowId,x.Name.ToString())).OrderBy(x=>x.Name).ToList(),FertilizerId=config.FertilizerSelected,
            InventoryCount=id=>(int)LocalPlayerState.GetItemCount(id)};
        screen=new(){Overview=config.Preferences.CompactHome};
        InitCropIdentity();
        IChatGui.Instance().ChatMessage+=OnGardenMessage;
        IFramework.Instance().Update+=OnGardenFramework;
        screenLoaded=true;
        layoutNotice=L("TextA0E556C2B31D");
        if(layoutRecoveryNotice.Length>0){layoutNotice=layoutRecoveryNotice;if(recoveredLayouts)SaveLayouts();}
    }
    private void UninitGardenScreen()
    {
        screenLoaded=false;
        IChatGui.Instance().ChatMessage-=OnGardenMessage;
        IFramework.Instance().Update-=OnGardenFramework;
        maintenanceTarget=null;
        if(config!=null)SaveLayouts();
    }
    private void DrawGardenScreen()
    {
        if(!screenLoaded)return;
        screenData.Busy=TaskHelper.IsBusy;screenData.Notice=layoutNotice;screenData.Scale=GlobalUIScale;screenData.Now=DateTimeOffset.UtcNow;
        var rows=new List<GardenBedView>();
        for(int i=0;i<cachedBeds.Count;i++){
            var b=cachedBeds[i];var binding=config.GardenBindings.FirstOrDefault(x=>x.HouseId==b.House&&x.GroupKey==b.Group);
            string name=config.GardenNames.TryGetValue(GardenLayout.GardenNames.Key(b.House,b.Group),out var custom)?custom:string.IsNullOrWhiteSpace(binding?.Name)?F("TextF6B971CCEE5D", i+1):binding.Name;
            var location=DescribeHouse(b.House);rows.Add(new(b.House,b.Group,name,true,b.Targets.Count,location.Region,location.Address,b.Targets.Select(t=>t.NativeSlot).ToList()));
        }
        int savedIndex=0;
        foreach(var binding in config.GardenBindings){
            savedIndex++;
            if(rows.Any(r=>r.HouseId==binding.HouseId&&r.GroupKey==binding.GroupKey))continue;
            string name=config.GardenNames.TryGetValue(GardenLayout.GardenNames.Key(binding.HouseId,binding.GroupKey),out var custom)?custom:string.IsNullOrWhiteSpace(binding.Name)?F("TextCB6DA8F5FDF2", savedIndex):binding.Name;
            var slots=SavedSlots(binding.HouseId,binding.GroupKey);var location=DescribeHouse(binding.HouseId);rows.Add(new(binding.HouseId,binding.GroupKey,name,false,slots.Count,location.Region,location.Address,slots));
        }
        foreach(var entry in config.GardenNames){
            if(!GardenLayout.GardenNames.TryParseKey(entry.Key,out var house,out var group)||rows.Any(r=>r.HouseId==house&&r.GroupKey==group))continue;
            var slots=SavedSlots(house,group);var location=DescribeHouse(house);rows.Add(new(house,group,entry.Value,false,slots.Count,location.Region,location.Address,slots));
        }
        screenData.Beds=rows;
        screen.Draw(screenData,ExecuteGardenCommand);
    }
    private List<int> SavedSlots(ulong house,uint group)
    {
        if(config.GardenSlots.TryGetValue(GardenLayout.GardenNames.Key(house,group),out var saved)&&saved.Count is 4 or 6 or 8)return saved;
        var binding=config.GardenBindings.FirstOrDefault(b=>b.HouseId==house&&b.GroupKey==group);
        if(binding?.NativeSlots is {Count:>0})return binding.NativeSlots;
        return config.Layouts.FirstOrDefault(p=>p.Id==binding?.LayoutId)?.Cells.Select(c=>c.NativeSlot).Order().ToList()??Enumerable.Range(0,8).ToList();
    }
    private void ExecuteGardenCommand(GardenUiCommand cmd)
    {
        try{
            if(cmd.Kind==GardenUiCommandKind.Save){config.SelectedLayoutID=screenData.SelectedLayoutId;config.FertilizerSelected=GardenFertilizer.Resolve(screenData.FertilizerId);screenData.FertilizerId=config.FertilizerSelected;SaveLayouts();return;}
            if(cmd.Kind==GardenUiCommandKind.Refresh){cachedBeds=ReadNearbyBeds();SaveLayouts();layoutNotice=F("Text64A1C6EBE659", cachedBeds.Count);return;}
            if(cmd.Kind==GardenUiCommandKind.Stop){AbortLayout(L("Text4017C5B03913"));return;}
            if(TaskHelper.IsBusy)return;
            if(cmd.Group.HasValue&&cmd.Kind is GardenUiCommandKind.Plant or GardenUiCommandKind.Preview or GardenUiCommandKind.Care or GardenUiCommandKind.Fertilize or GardenUiCommandKind.Harvest){
                var currentHouse=HousingManager.Instance();
                if(currentHouse==null||currentHouse->OutdoorTerritory==null||(ulong)currentHouse->GetCurrentHouseId()!=cmd.HouseId)throw new InvalidOperationException(L("TextEBFB049FBD78"));
            }
            config.SelectedLayoutID=screenData.SelectedLayoutId;
            var preset=config.Layouts.FirstOrDefault(p=>p.Id==cmd.LayoutId);
            switch(cmd.Kind){
                case GardenUiCommandKind.Bind:{
                    var bed=ReadNearbyBeds().FirstOrDefault(b=>b.House==cmd.HouseId&&b.Group==cmd.Group)??throw new InvalidOperationException(L("TextECB963A1EC1B"));
                    if(preset==null||preset.Cells.Count!=bed.Targets.Count)throw new InvalidOperationException(L("TextAA9FE323F0E1"));
                    var binding=config.GardenBindings.FirstOrDefault(b=>b.HouseId==bed.House&&b.GroupKey==bed.Group);
                    if(binding==null){binding=new(){HouseId=bed.House,GroupKey=bed.Group,Name=screenData.Beds.FirstOrDefault(b=>b.HouseId==bed.House&&b.GroupKey==bed.Group)?.Name??L("Text9F8B0DA2F890")};config.GardenBindings.Add(binding);}
                    binding.LayoutId=preset.Id;binding.NativeSlots=bed.Targets.Select(t=>t.NativeSlot).ToList();binding.X=bed.Position.X;binding.Y=bed.Position.Y;binding.Z=bed.Position.Z;SaveLayouts();layoutNotice=L("Text576C57532394");break;
                }
                case GardenUiCommandKind.Unbind:config.GardenBindings.RemoveAll(b=>b.HouseId==cmd.HouseId&&b.GroupKey==cmd.Group);SaveLayouts();break;
                case GardenUiCommandKind.Calibrate:{
                    if(preset==null||cmd.Cell<0||cmd.Cell>=preset.Cells.Count||!TryReadGarden(TargetManager.Target,out var target))throw new InvalidOperationException(L("TextE6318953050B"));
                    var manager=HousingManager.Instance();
                    if(manager==null||(ulong)manager->GetCurrentHouseId()!=cmd.HouseId||target.GroupKey!=cmd.Group||!SavedSlots(cmd.HouseId,target.GroupKey).Contains(target.NativeSlot))throw new InvalidOperationException(L("Text5EF7FBC468E6"));
                    var cell=preset.Cells[cmd.Cell];int before=cell.NativeSlot;var other=preset.Cells.FirstOrDefault(c=>c.NativeSlot==target.NativeSlot);
                    if(other!=null)other.NativeSlot=before;cell.NativeSlot=target.NativeSlot;SaveLayouts();layoutNotice=L("TextF919AE930CEF");break;
                }
                case GardenUiCommandKind.Preview:PreviewLayout(cmd.Group);break;
                case GardenUiCommandKind.Plant:StartLayoutPlant(cmd.Group);break;
                case GardenUiCommandKind.Care:StartTend(cmd.Group);break;
                case GardenUiCommandKind.Fertilize:StartFertilize(cmd.Group);break;
                case GardenUiCommandKind.Harvest:StartGather(cmd.Group);break;
                case GardenUiCommandKind.ManualRecord:{
                    if(!cmd.Group.HasValue||!cmd.At.HasValue||cmd.At> DateTimeOffset.UtcNow.AddMinutes(2))throw new InvalidOperationException(L("TextBA3B39BB00D7"));
                    if(!SavedSlots(cmd.HouseId,cmd.Group.Value).Contains(cmd.Cell))throw new InvalidOperationException(L("Text7BC54B7C9FE2"));
                    var record=GardenLedger.GetOrCreate(config.Records,cmd.HouseId,cmd.Group.Value,cmd.Cell);
                    uint seed=preset?.Cells.FirstOrDefault(c=>c.NativeSlot==cmd.Cell)?.SeedId??record.SeedId;
                    if(!GardenLedger.Record(record,cmd.Action,cmd.At.Value,seed))throw new InvalidOperationException(L("Text7770FFAE7505"));
                    Audit(cmd.Action,new(0,cmd.Group.Value,cmd.Cell),L("Text60AACEDC39AC"),cmd.HouseId,true);SaveLayouts();layoutNotice=L("TextF151A1ED9A55");break;
                }
            }
        }catch(Exception e){layoutNotice=e.Message;}
    }
    private void StartRun(GardenAction action)
    {
        var manager=HousingManager.Instance();if(manager!=null)planHouse=(ulong)manager->GetCurrentHouseId();
        maintenanceDone=maintenanceSkipped=0;currentAction=action;runId=Guid.NewGuid().ToString("N");runGuard.Clear();maintenanceTarget=null;
        Audit(action,null,L("Text4855BCF19A16"));
    }
    private Func<bool> WrapPlantStage(Func<bool> poll,string name,PlantStep step)
    {
        bool announced=false;
        return ()=>{
            if(!runningLayout)return true;
            if(!announced){announced=true;Audit(GardenAction.Plant,new(step.ObjectId,step.GroupKey,step.NativeSlot),name,planHouse);}
            return poll();
        };
    }
    private void RecordPlantConfirmed(PlantStep step)
    {
        var target=new GardenTarget(step.ObjectId,step.GroupKey,step.NativeSlot);
        if(!runGuard.Complete(planHouse,target,GardenAction.Plant))return;
        var row=GardenLedger.GetOrCreate(config.Records,planHouse,step.GroupKey,step.NativeSlot);
        GardenLedger.Record(row,GardenAction.Plant,DateTimeOffset.UtcNow,step.SeedId);
        Audit(GardenAction.Plant,target,L("TextEB42882E94A1"),planHouse);SaveLayouts();
    }
    private bool BeginMaintenanceStep(ulong objectId,GardenAction kind)
    {
        maintenanceTarget=null;
        if(kind==GardenAction.Fertilize&&!CheckFertilizerStock(out var stockReason)){AbortLayout(stockReason);return true;}
        if(!IsEnvironmentValid(out var ids)||!ids.Contains(objectId)||!TryReadGarden(IObjectTable.Instance().FirstOrDefault(x=>x.GameObjectID==objectId),out var target)){AbortLayout(L("TextD55786AFD1EA"));return true;}
        maintenanceHouse=(ulong)HousingManager.Instance()->GetCurrentHouseId();
        if(maintenanceHouse!=planHouse){AbortLayout(L("Text60D00550E47C"));return true;}
        if(!runGuard.Begin(maintenanceHouse,target,kind))return true;
        maintenanceTarget=target;maintenanceMenuSent=maintenanceChosen=maintenanceSuccess=false;
        maintenanceRejected=fertilizerOpened=fertilizerSubmitted=careTalkAdvanced=false;maintenanceSkipReason="";
        TargetManager.Target=IObjectTable.Instance().First(x=>x.GameObjectID==objectId);
        Audit(kind,target,L("TextB8894B8E1003"),maintenanceHouse);
        layoutNotice=F("Text884B9DA5A598", GardenLedger.ActionName(kind), target.NativeSlot+1);
        new EventStartPackt(objectId,721047).Send();return true;
    }
    private bool FinishMaintenanceStep()
    {
        if(maintenanceTarget==null)return true;
        if(ICondition.Instance()[ConditionFlag.OccupiedInQuestEvent])return false;
        if(maintenanceChosen&&!maintenanceSuccess&&!maintenanceRejected){layoutNotice=F("Text0698F17D833B", GardenLedger.ActionName(currentAction), maintenanceTarget.NativeSlot+1);return false;}
        if(!maintenanceChosen||maintenanceRejected){maintenanceSkipped++;Audit(currentAction,maintenanceTarget,L("Text8AD9C84CF13D")+maintenanceSkipReason,maintenanceHouse);}
        else maintenanceDone++;
        maintenanceTarget=null;return true;
    }
    private void OnGardenMessage(IHandleableChatMessage message)
    {
        if(!screenLoaded)return;
        // Growth/name announcements are SystemMessage/None, distinct from care confirmations.
        string body=message.OriginalMessage.ExtractText();
        if(message.LogKind==XivChatType.SystemMessage&&message.SourceKind is XivChatRelationKind.None or XivChatRelationKind.LocalPlayer)CaptureCropName(body);
        bool local=message.SourceKind==XivChatRelationKind.LocalPlayer;
        var signal=local&&message.LogKind==XivChatType.SystemMessage?GardenLedger.ParseSuccess(body):null;
        string rejection=(local||message.SourceKind==XivChatRelationKind.None)&&message.LogKind is XivChatType.ErrorMessage or XivChatType.SystemError?GardenMaintenanceFailure.Parse(body,currentAction):null;
        bool harvestLoot=local&&message.LogKind==XivChatType.LootNotice&&currentAction==GardenAction.Harvest&&maintenanceChosen&&maintenanceTarget!=null;
        if(signal==null&&!harvestLoot&&rejection==null)return;
        var plant=activePlant;var target=maintenanceTarget;var thisRun=runId;
        void Handle(){
            if(!screenLoaded||runId!=thisRun)return;
            if(signal?.Action==GardenAction.Plant&&runningLayout&&plant!=null&&activePlant==plant&&signal.NativeSlot==plant.NativeSlot&&ContextValid(plant)){
                RecordPlantConfirmed(plant);return;
            }
            if(rejection!=null){
                if(target!=null&&maintenanceTarget==target&&maintenanceChosen&&TaskHelper.IsBusy&&MaintenanceContextValid()){
                    maintenanceRejected=true;maintenanceSkipReason=rejection;layoutNotice=F("Text11D174B11D9E", target.NativeSlot+1, rejection);
                }
                return;
            }
            var action=harvestLoot?GardenAction.Harvest:signal!.Action;
            if(target==null||maintenanceTarget!=target||!maintenanceChosen||action!=currentAction||!TaskHelper.IsBusy)return;
            var manager=HousingManager.Instance();
            if(manager==null||manager->OutdoorTerritory==null||(ulong)manager->GetCurrentHouseId()!=maintenanceHouse||!TryReadGarden(IObjectTable.Instance().FirstOrDefault(x=>x.GameObjectID==target.ObjectId),out var actual)||actual.GroupKey!=target.GroupKey||actual.NativeSlot!=target.NativeSlot)return;
            if(!runGuard.Complete(maintenanceHouse,target,action))return;
            var row=GardenLedger.GetOrCreate(config.Records,maintenanceHouse,target.GroupKey,target.NativeSlot);
            GardenLedger.Record(row,action,DateTimeOffset.UtcNow);maintenanceSuccess=true;
            Audit(action,target,L("TextE91C9E16DD25"),maintenanceHouse);SaveLayouts();
        }
        if(IFramework.Instance().IsInFrameworkUpdateThread)Handle();else _=IFramework.Instance().RunOnFrameworkThread(Handle);
    }
    private void Audit(GardenAction action,GardenTarget target,string stage,ulong house=0,bool manual=false)
    {
        if(config?.Operations==null)return;
        config.Operations.Add(new(){At=DateTimeOffset.UtcNow,HouseId=house==0?planHouse:house,GroupKey=target?.GroupKey??0,NativeSlot=target?.NativeSlot??-1,Action=action,Stage=stage,RunId=runId,Manual=manual});
        if(config.Operations.Count>200)config.Operations.RemoveRange(0,config.Operations.Count-200);
        auditDirty=true;
    }
    private void OnGardenFramework(IFramework framework)
    {
        if(!screenLoaded)return;
        FlushCropIdentity();
        AdvanceOwnCareTalk();
        if(auditDirty&&DateTimeOffset.UtcNow>=nextAuditSave){SaveLayouts();nextAuditSave=DateTimeOffset.UtcNow.AddSeconds(2);}
        if(DateTimeOffset.UtcNow<nextReminderCheck)return;
        nextReminderCheck=DateTimeOffset.UtcNow.AddSeconds(10);
        if(TaskHelper.IsBusy||!config.Preferences.RemindersEnabled||IObjectTable.Instance().LocalPlayer==null||(config.Preferences.QuietInCombat&&ICondition.Instance()[ConditionFlag.InCombat]))return;
        var now=DateTimeOffset.UtcNow;var due=GardenLedger.Pending(config.Records,config.GardenBindings,now);if(due.Count==0)return;
        foreach(var group in due.GroupBy(d=>(d.Record.HouseId,d.Record.GroupKey,d.Action))){
            var binding=config.GardenBindings.First(b=>b.HouseId==group.Key.HouseId&&b.GroupKey==group.Key.GroupKey);
            string name=string.IsNullOrWhiteSpace(binding.Name)?L("Text0027CDE9B260"):binding.Name;
            string text=GardenLedger.ReminderText(name,DescribeHouse(binding.HouseId).Address,group.Key.Action,group.Count());
            if(config.Preferences.ChatReminder)IChatGui.Instance().Print(text,L("Text8973705F0576"));
            layoutNotice=text;foreach(var item in group)GardenLedger.MarkSent(item,now);
        }
        if(config.Preferences.SoundReminder)MessageBeep(0x40);
        SaveLayouts();
    }
}


}


namespace OmniToolbox.TreeHouseLocal
{
using System.Text.Json;



public sealed unsafe partial class KotiyaGarden
{
    private static readonly JsonSerializerOptions stateJson=new(){IncludeFields=true,WriteIndented=true,MaxDepth=48};
    private void LoadState()
    {
        if(!File.Exists(stateFile))return;
        try{
            if(new FileInfo(stateFile).Length>8_388_608)throw new InvalidDataException(L("Text3DCD3E62C0D8"));
            var loaded=JsonSerializer.Deserialize<Settings>(File.ReadAllText(stateFile),stateJson)??throw new InvalidDataException(L("Text05B9C9100B75"));
            config.Layouts=loaded.Layouts??[];config.SelectedLayoutID=loaded.SelectedLayoutID;
            config.GardenBindings=loaded.GardenBindings??[];config.Records=loaded.Records??[];config.Operations=loaded.Operations??[];
            config.Preferences=loaded.Preferences??new();config.GardenNames=loaded.GardenNames??[];config.GardenSlots=loaded.GardenSlots??[];
            config.FertilizerSelected=GardenFertilizer.Resolve(loaded.FertilizerSelected);
        }catch(Exception e){throw new InvalidDataException(L("TextE2B9B73BD656")+e.Message,e);}
    }
    private void PersistState()
    {
        if(!stateReady||string.IsNullOrWhiteSpace(stateFile))return;
        string temporary=stateFile+".tmp";
        try{
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            File.WriteAllText(temporary,JsonSerializer.Serialize(config,stateJson));
            if(File.Exists(stateFile))File.Copy(stateFile,stateFile+".bak",true);
            File.Move(temporary,stateFile,true);
        }catch(Exception e){layoutNotice=L("Text59A427553380")+e.Message;OmniToolbox.Host.DalamudServices.PluginLog?.Error(e,"KotiyaGarden state save failed");}
    }
}

}


namespace OmniToolbox.TreeHouseLocal
{
using OmenTools.Interop.Game.AddonEvent;



public sealed unsafe partial class KotiyaGarden
{
    private void AdvanceOwnCareTalk()
    {
        if(!config.Preferences.AdvanceCareTalk||TaskHelper==null||!TaskHelper.IsBusy||currentAction!=GardenAction.Care||!maintenanceChosen||careTalkAdvanced)return;
        var talk=Talk;
        bool ready=talk!=null&&talk->IsAddonAndNodesReady()&&talk->IsVisible;
        if(!GardenTalkPolicy.CanAdvance(config.Preferences.AdvanceCareTalk,TaskHelper.IsBusy,currentAction,maintenanceChosen,MaintenanceContextValid(),ready,careTalkAdvanced))return;
        // Only advance this run's care Talk; never select menus or accept Yes/No here.
        careTalkAdvanced=AddonTalkEvent.ClickNext();
    }
}

}
