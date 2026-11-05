using FFXIVClientStructs.FFXIV.Client.Game;
namespace GardenLayout;
public enum GardenStockStatus { Available,Missing,Unavailable }
public static unsafe class GardenNativeInventory
{
    public static GardenStockStatus FindCarried(uint itemId,out InventoryItem found)
    {
        found=default;var manager=InventoryManager.Instance();if(manager==null)return GardenStockStatus.Unavailable;
        Span<nint> bags=stackalloc nint[4];
        for(int i=0;i<4;i++)bags[i]=(nint)manager->GetInventoryContainer((InventoryType)i);
        return FindInBags(bags,itemId,out found);
    }
    // Framework-owned four carried containers only. Resolve afresh for every selection;
    // return a complete value copy, never retain a native slot pointer across frames.
    public static GardenStockStatus FindInBags(ReadOnlySpan<nint> bags,uint itemId,out InventoryItem found)
    {
        found=default;if(bags.Length!=4||itemId==0)return GardenStockStatus.Unavailable;
        bool complete=true;
        for(int i=0;i<4;i++){
            if(bags[i]==0){complete=false;continue;}
            var bag=*(InventoryContainer*)bags[i];
            if(!bag.IsLoaded||bag.Items==null||bag.Size!=35||bag.Type!=(InventoryType)i){complete=false;continue;}
            for(int slot=0;slot<35;slot++){
                var item=bag.Items[slot];
                if(item.IsSymbolic||item.ItemId!=itemId||item.Quantity==0||item.Container!=bag.Type||item.Slot!=slot)continue;
                found=item;return GardenStockStatus.Available;
            }
        }
        return complete?GardenStockStatus.Missing:GardenStockStatus.Unavailable;
    }
}
