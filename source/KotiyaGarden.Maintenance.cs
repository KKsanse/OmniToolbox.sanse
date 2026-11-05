using GardenLayout;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.Interop.Game.AddonEvent;

namespace OmniToolbox.TreeHouseLocal;

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
