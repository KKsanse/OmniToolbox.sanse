using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GardenLayout;

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
