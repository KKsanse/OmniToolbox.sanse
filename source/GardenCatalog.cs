using System;
using System.Collections.Generic;
using System.Linq;
namespace GardenLayout;
public sealed record GardenSavedEntry(ulong HouseId,uint GroupKey,string Name);
public static class GardenCatalog
{
    public static void Remember(Dictionary<string,string> names,Dictionary<string,List<int>> slots,ulong house,uint group,IEnumerable<int> nativeSlots,string fallbackName)
    {
        var values=nativeSlots.ToList();
        if(house==0||group==0||values.Count is not (4 or 6 or 8)||values.Any(i=>i<0||i>7)||values.Distinct().Count()!=values.Count)throw new ArgumentException(L("TextC49F5B3BD941"));
        string key=GardenNames.Key(house,group);slots[key]=values;
        if(!names.ContainsKey(key))names[key]=fallbackName;
    }
    public static void RememberBindings(Dictionary<string,string> names,Dictionary<string,List<int>> slots,IEnumerable<GardenBinding> bindings,IEnumerable<GardenPreset> layouts)
    {
        bool Valid(IReadOnlyCollection<int>? values)=>values!=null&&values.Count is 4 or 6 or 8&&values.All(i=>i>=0&&i<8)&&values.Distinct().Count()==values.Count;
        foreach(var binding in bindings){
            if(binding==null||binding.HouseId==0||binding.GroupKey==0)continue;
            string key=GardenNames.Key(binding.HouseId,binding.GroupKey);
            var values=slots.TryGetValue(key,out var scanned)&&Valid(scanned)?scanned:binding.NativeSlots;
            if(!Valid(values))values=layouts.FirstOrDefault(p=>p!=null&&p.Id==binding.LayoutId)?.Cells?.Where(c=>c!=null).Select(c=>c.NativeSlot).ToList();
            if(!Valid(values))continue;
            Remember(names,slots,binding.HouseId,binding.GroupKey,values!,string.IsNullOrWhiteSpace(binding.Name)?L("TextC91C71FF8708"):binding.Name);
        }
    }
    public static List<GardenSavedEntry> Saved(Dictionary<string,string> names,Dictionary<string,List<int>> slots)
    {
        var result=new List<GardenSavedEntry>();
        foreach(string key in names.Keys.Concat(slots.Keys).Distinct()){
            if(!GardenNames.TryParseKey(key,out var house,out var group))continue;
            result.Add(new(house,group,names.GetValueOrDefault(key)??""));
        }
        return result;
    }
    public static void Forget(Dictionary<string,string> names,Dictionary<string,List<int>> slots,List<GardenBinding> bindings,ulong house,uint group)
    {
        if(house==0||group==0)throw new ArgumentException(L("TextD8A9CC937220"));
        string key=GardenNames.Key(house,group);names.Remove(key);slots.Remove(key);
        bindings.RemoveAll(b=>b.HouseId==house&&b.GroupKey==group);
    }
}
