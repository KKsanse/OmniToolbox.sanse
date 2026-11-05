using System;

namespace GardenLayout;

// HouseId value layout follows the installed FFXIVClientStructs HouseId /
// HouseUnit contract. This decodes saved identities, not process memory.
public sealed record GardenAddress(ushort WorldId,ushort TerritoryTypeId,int Ward,int? Plot)
{
    public static GardenAddress? Decode(ulong id)
    {
        if(id==0)return null;
        var world=(ushort)(id>>48);var territory=(ushort)(id>>32);
        if(world==0||territory==0)return null;
        int ward=(int)((id>>16)&0x3f)+1;
        int unit=(int)(id&0xff);int? plot=(unit&0x80)==0?(unit&0x7f)+1:null;
        return new(world,territory,ward,plot);
    }
    public string Describe(string world,string region)=>F("Text231BD03834D1", world, region, Ward)+(Plot.HasValue?F("Text2D3C3B7FC393", Plot.Value):L("Text69A9FFABDD85"));
}
