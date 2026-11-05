namespace GardenLayout;

public static class GardenTalkPolicy
{
    public static bool CanAdvance(bool enabled,bool busy,GardenAction action,bool chosen,bool contextValid,bool visible,bool alreadyAdvanced)=>
        enabled&&busy&&action==GardenAction.Care&&chosen&&contextValid&&visible&&!alreadyAdvanced;
}
