namespace GardenLayout;
public static class GardenFertilizer
{
    // Item 7767 verified against the running client's Item sheet.
    public const uint DefaultItemId=7767;
    public static uint Resolve(uint selected)=>selected==0?DefaultItemId:selected;
}
