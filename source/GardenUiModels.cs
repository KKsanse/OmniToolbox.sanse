using System;
using System.Collections.Generic;
using System.Linq;

namespace GardenLayout;

public sealed record GardenItemOption(uint Id,string Name);
public sealed record GardenBedView(ulong HouseId,uint GroupKey,string Name,bool Nearby,int Slots,string Region="",string Address="",IReadOnlyList<int>? NativeSlots=null);
public enum GardenUiCommandKind { Save, Refresh, Bind, Unbind, Calibrate, Preview, Plant, Stop, Care, Fertilize, Harvest, ManualRecord, RemoveGarden }
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
