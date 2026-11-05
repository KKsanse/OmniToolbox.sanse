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

namespace OmniToolbox.TreeHouseLocal;

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


