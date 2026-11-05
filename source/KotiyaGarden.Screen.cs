using GardenLayout;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.Info.Game.Data;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Info.Lumina;
using System.Runtime.InteropServices;

namespace OmniToolbox.TreeHouseLocal;

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

