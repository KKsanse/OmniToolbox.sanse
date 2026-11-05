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

namespace OmniToolbox.TreeHouseLocal;

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
