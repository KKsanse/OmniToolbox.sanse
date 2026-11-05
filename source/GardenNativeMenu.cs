using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GardenLayout;

public static unsafe class GardenNativeMenu
{
    // Input is a complete SDK struct copy from the current framework frame.
    public static bool TrySnapshot(AtkComponentList list,int entries,out bool[] enabled)
    {
        enabled=[];
        if(entries is <1 or >16||list.IsUpdatePending||list.ListLength!=entries||list.AllocatedItemRendererListLength<entries||list.AllocatedItemRendererListLength>256||list.ItemRendererList==null)return false;
        enabled=new bool[entries];
        for(int i=0;i<entries;i++){var item=list.ItemRendererList[i];enabled[i]=!item.IsDisabled;}
        return true;
    }
    public static bool TryReadTitle(AtkTextNode node,out string title)
    {
        title="";var text=node.NodeText;
        if(node.NodeId!=2||node.Type!=NodeType.Text||!text.StringPtr.HasValue||text.BufUsed<1||text.BufUsed>512||text.BufSize<text.BufUsed)return false;
        var bytes=new System.ReadOnlySpan<byte>(text.StringPtr,(int)text.BufUsed);
        if(bytes[^1]!=0)return false;
        title=System.Text.Encoding.UTF8.GetString(bytes[..^1]);return true;
    }
}
