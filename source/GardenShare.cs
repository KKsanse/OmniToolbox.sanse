using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GardenLayout;

public static class GardenShare
{
    public const string Prefix="GLW1:";
    public const int MaxCodeLength=32_768;
    private sealed class Envelope
    {
        public int Version { get; set; }=1;
        public string Name { get; set; }="";
        public uint DefaultSoilId { get; set; }
        public List<GardenCell> Cells { get; set; }=[];
        public List<int> PlantOrder { get; set; }=[];
    }
    public static string Export(GardenPreset preset)
    {
        GardenPlanner.Validate(preset);
        var dto=new Envelope{Name=preset.Name,DefaultSoilId=preset.DefaultSoilId,Cells=preset.Cells,PlantOrder=preset.PlantOrder};
        return Prefix+Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(dto));
    }
    public static GardenPreset Import(string code,IEnumerable<GardenPreset> existing,IReadOnlySet<uint> seeds,IReadOnlySet<uint> soils)
    {
        if(string.IsNullOrWhiteSpace(code)||code.Length>MaxCodeLength)throw new ArgumentException(L("Text3011E4D0474B"));
        code=code.Trim();if(!code.StartsWith(Prefix,StringComparison.Ordinal))throw new ArgumentException(L("Text2F714C7C3248"));
        Envelope? dto;
        try{
            byte[] bytes=Convert.FromBase64String(code[Prefix.Length..]);
            dto=JsonSerializer.Deserialize<Envelope>(bytes,new JsonSerializerOptions{MaxDepth=12});
        }catch(Exception e)when(e is FormatException or JsonException){throw new ArgumentException(L("TextE7AA88F2B160"));}
        if(dto==null||dto.Version!=1)throw new ArgumentException(L("TextD38B22005F79"));
        var result=new GardenPreset{Name=dto.Name,DefaultSoilId=dto.DefaultSoilId,Cells=dto.Cells,PlantOrder=dto.PlantOrder};
        if(GardenLayoutRecovery.TryRecover(result,out var recovered)){result.Cells=recovered.Cells;result.PlantOrder=recovered.PlantOrder;}
        GardenPlanner.Validate(result);
        if(result.Cells.Any(c=>c.SeedId!=0&&!seeds.Contains(c.SeedId))||result.Cells.Any(c=>c.SoilId!=0&&!soils.Contains(c.SoilId))||(result.DefaultSoilId!=0&&!soils.Contains(result.DefaultSoilId)))throw new ArgumentException(L("TextB524D7242D04"));
        result.Name=GardenPlanner.UniqueName(result.Name,existing);
        return result; // New identity; importing never rebinds a garden or overwrites a preset.
    }
}
