using System;
using System.Collections.Generic;
using System.Linq;

namespace GardenLayout;

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
        presets.Where(p=>p!=null).SelectMany(p=>p.Cells??[]).Where(c=>c!=null).Select(c=>c.SeedId).Where(id=>id!=0).Distinct().ToList();
    public static uint[] CandidateSeeds(GardenPreset selected,IEnumerable<GardenPreset> presets)=>
        ConfiguredSeeds(new[]{selected}.Concat(presets.Where(p=>p!=selected))).Take(4).Concat(Enumerable.Repeat(0u,4)).Take(4).ToArray();
}
