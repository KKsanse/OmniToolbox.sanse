using System.Text.Json;

namespace OmniToolbox.TreeHouseLocal;

public sealed unsafe partial class KotiyaGarden
{
    private static readonly JsonSerializerOptions stateJson=new(){IncludeFields=true,WriteIndented=true,MaxDepth=48};
    private void LoadState()
    {
        if(!File.Exists(stateFile))return;
        try{
            if(new FileInfo(stateFile).Length>8_388_608)throw new InvalidDataException(L("Text3DCD3E62C0D8"));
            var loaded=JsonSerializer.Deserialize<Settings>(File.ReadAllText(stateFile),stateJson)??throw new InvalidDataException(L("Text05B9C9100B75"));
            config.Layouts=loaded.Layouts??[];config.SelectedLayoutID=loaded.SelectedLayoutID;
            config.GardenBindings=loaded.GardenBindings??[];config.Records=loaded.Records??[];config.Operations=loaded.Operations??[];
            config.Preferences=loaded.Preferences??new();config.GardenNames=loaded.GardenNames??[];config.GardenSlots=loaded.GardenSlots??[];
            config.FertilizerSelected=GardenFertilizer.Resolve(loaded.FertilizerSelected);
        }catch(Exception e){throw new InvalidDataException(L("TextE2B9B73BD656")+e.Message,e);}
    }
    private void PersistState()
    {
        if(!stateReady||string.IsNullOrWhiteSpace(stateFile))return;
        string temporary=stateFile+".tmp";
        try{
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            File.WriteAllText(temporary,JsonSerializer.Serialize(config,stateJson));
            if(File.Exists(stateFile))File.Copy(stateFile,stateFile+".bak",true);
            File.Move(temporary,stateFile,true);
        }catch(Exception e){layoutNotice=L("Text59A427553380")+e.Message;OmniToolbox.Host.DalamudServices.PluginLog?.Error(e,"KotiyaGarden state save failed");}
    }
}
