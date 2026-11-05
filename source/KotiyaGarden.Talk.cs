using OmenTools.Interop.Game.AddonEvent;

namespace OmniToolbox.TreeHouseLocal;

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
