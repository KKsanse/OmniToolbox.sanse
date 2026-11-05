using GardenLayout;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.Info.Lumina;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreeHouseLocal;

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

