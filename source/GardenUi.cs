using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace GardenLayout;

// This renderer has no game/service dependency and is exercised in an offline
// native Dear ImGui host. All game actions go through explicit commands.
public sealed class GardenUi
{
    public bool Overview { get; set; }=true;
    public string RequestedTab { get; set; }="";
    public uint SelectedGroup { get; set; }
    public ulong SelectedHouse { get; set; }
    public int SelectedCell { get; set; }
    private int selectedRecordSlot;
    private string activeTab=L("TextC9E0F3954B5C");
    private string nameEdit="",editedLayout="",seedSearch="",soilSearch="",cellSoilSearch="",fertilizerSearch="",shareCode="",shareNotice="",manualTime="",localNotice="";
    private bool showShare,deleteConfirm;
    private int manualAction;
    private int newLayoutSlots=8;
    private IReadOnlyList<int> RecordSlots=>Bed?.NativeSlots is {Count:>0} slots?slots:Binding?.NativeSlots is {Count:>0} saved?saved:Enumerable.Range(0,Bed?.Slots??8).ToArray();
    private int shareDrawFrame=-1;
    private string regionFilter="";
    private ulong houseFilter;
    private string bedNameEdit="",editingBedKey="",deleteBedKey="";
    private readonly GardenSeedClipboard clipboard=new();
    private GardenUiData data=null!;
    private Action<GardenUiCommand> command=null!;
    private float S=>Math.Max(.75f,data.Scale);
    private GardenBedView? Bed=>data.Beds.FirstOrDefault(b=>b.GroupKey==SelectedGroup&&b.HouseId==SelectedHouse);
    private IEnumerable<GardenBedView> VisibleBeds=>GardenGrouping.Filter(data.Beds,data.Preferences.GroupByHouse,regionFilter,houseFilter);
    private static string RegionOf(GardenBedView bed)=>GardenGrouping.RegionOf(bed);
    private static string BedLabel(GardenBedView bed)=>string.IsNullOrWhiteSpace(bed.Address)?bed.Name:bed.Name+" · "+bed.Address;
    private GardenBinding? Binding=>data.Bindings.FirstOrDefault(b=>b.GroupKey==SelectedGroup&&b.HouseId==SelectedHouse);
    private GardenPreset? Preset=>data.Layouts.FirstOrDefault(p=>p.Id==data.SelectedLayoutId);
    private IEnumerable<GardenRecord> BedRecords=>data.Records.Where(r=>r.HouseId==SelectedHouse&&r.GroupKey==SelectedGroup);
    private void Send(GardenUiCommandKind kind)=>command(new(kind,SelectedHouse,SelectedGroup==0?null:SelectedGroup,data.SelectedLayoutId,SelectedCell));
    private void Save()=>command(new(GardenUiCommandKind.Save));
    private static string Time(DateTimeOffset? value)=>value?.ToLocalTime().ToString("MM/dd HH:mm",CultureInfo.InvariantCulture)??L("Text7450CC5B16E2");
    private string Item(uint id)=>id==0?L("Text23E1665F9447"):data.Seeds.Concat(data.Soils).FirstOrDefault(i=>i.Id==id)?.Name??F("Text4BD2AB225AE6", id);
    public void Draw(GardenUiData model,Action<GardenUiCommand> execute)
    {
        data=model;command=execute;
        if(Bed==null&&data.Beds.Count>0)SelectBed(data.Beds[0]);
        ImGui.PushID("GardenLayoutWorkV2");
        try{
            ImGui.TextUnformatted(L("Text970C506868E3"));ImGui.SameLine();ImGui.TextDisabled(data.Busy?L("Text8F38FD93B5A0"):L("TextB073DB9C0BD6"));
            if(ImGui.Button(Overview?L("TextF6F58196C9DF"):L("Text103FC1ECAA72")))Overview=!Overview;
            ImGui.SameLine();if(ImGui.Button(L("TextA1C82BEE001D")))Send(GardenUiCommandKind.Refresh);
            ImGui.SameLine();using(new Disabled(!data.Busy))if(ImGui.Button(L("Text6687AC8617E4")))Send(GardenUiCommandKind.Stop);
            ImGui.Separator();
            DrawRegionPicker();
            if(Overview)DrawOverview();else DrawWorkbench();
            ImGui.Separator();
            if(!string.IsNullOrEmpty(localNotice))ImGui.TextWrapped(localNotice);
            if(!string.IsNullOrEmpty(data.Notice))ImGui.TextWrapped(data.Notice);
            DrawShare();
        }finally{ImGui.PopID();}
    }
    private void DrawRegionPicker()
    {
        ImGui.TextDisabled(L("TextCE5DB3DBEA01"));ImGui.SameLine();
        if(ImGui.RadioButton(L("Text09D51EF2311F"),!data.Preferences.GroupByHouse)){data.Preferences.GroupByHouse=false;Save();}
        ImGui.SameLine();if(ImGui.RadioButton(L("Text5983AFD4D8B4"),data.Preferences.GroupByHouse)){data.Preferences.GroupByHouse=true;Save();}
        if(data.Preferences.GroupByHouse){
            var houses=GardenGrouping.Houses(data.Beds);
            if(houseFilter!=0&&!houses.Any(h=>h.HouseId==houseFilter))houseFilter=0;
            string preview=houses.FirstOrDefault(h=>h.HouseId==houseFilter)?.Label??L("Text49A9DB457E39");
            if(BeginBoundedCombo(L("TextA8D4AB6781D4"),preview,360*S)){
                if(ImGui.Selectable(L("Text49A9DB457E39"),houseFilter==0))houseFilter=0;
                foreach(var house in houses){
                    ImGui.PushID(house.HouseId.ToString());if(ImGui.Selectable(house.Label,house.HouseId==houseFilter))houseFilter=house.HouseId;
                    if(ImGui.IsItemHovered())ImGui.SetTooltip(house.Label);ImGui.PopID();
                }
                ImGui.EndCombo();
            }
        }else{
            var regions=data.Beds.Select(RegionOf).Distinct().Order().ToList();
            if(regionFilter.Length>0&&!regions.Contains(regionFilter))regionFilter="";
            if(BeginBoundedCombo(L("Text0343BE98DB6E"),regionFilter.Length==0?L("TextF6C02CD10EDC"):regionFilter,250*S)){
                if(ImGui.Selectable(L("TextF6C02CD10EDC"),regionFilter.Length==0))regionFilter="";
                foreach(string region in regions)if(ImGui.Selectable(region,regionFilter==region))regionFilter=region;
                ImGui.EndCombo();
            }
        }
        var visible=VisibleBeds.ToList();
        if(Bed==null||!visible.Contains(Bed)){
            if(visible.Count>0)SelectBed(visible[0]);else{SelectedGroup=0;SelectedHouse=0;}
        }
        ImGui.TextDisabled(L("TextBA615C1A763C"));
    }
    private void SelectBed(GardenBedView b)
    {
        SelectedGroup=b.GroupKey;SelectedHouse=b.HouseId;
        var bound=data.Bindings.FirstOrDefault(x=>x.GroupKey==b.GroupKey&&x.HouseId==b.HouseId);
        if(bound!=null&&data.Layouts.Any(p=>p.Id==bound.LayoutId))data.SelectedLayoutId=bound.LayoutId;
        SelectedCell=0;newLayoutSlots=b.Slots is 4 or 6 or 8?b.Slots:8;selectedRecordSlot=RecordSlots.FirstOrDefault();Save();
    }
    private string DueText(IEnumerable<GardenRecord> records,GardenReminderRule? rule,GardenAction action)
    {
        if(rule==null)return L("Text2F5F1D6FBFB0");
        if(action==GardenAction.Care?!rule.CareEnabled:!rule.FertilizerEnabled)return L("Text6744B4C6A9AA");
        var active=records.Where(GardenLedger.NeedsMaintenance).ToList();
        var due=active.Select(r=>GardenLedger.DueAt(r,rule,action)).Where(t=>t.HasValue).Select(t=>t!.Value).Order().ToList();
        if(due.Count==0)return L("Text6D7517E3B158");
        if(action==GardenAction.Fertilize&&due.Count!=active.Count)return F("TextFC69966E9183", active.Count-due.Count);
        var delta=(action==GardenAction.Fertilize?due[^1]:due[0])-data.Now;
        if(delta<=TimeSpan.Zero)return action==GardenAction.Fertilize?F("Text90E5970B3801", due.Count):F("Text188ACF28DC43", due.Count(t=>t<=data.Now));
        return action==GardenAction.Fertilize?F("TextADF564B6196C", (int)delta.TotalHours, delta.Minutes):F("TextD05E22AE8BE8", (int)delta.TotalHours, delta.Minutes);
    }
    private void DrawOverview()
    {
        ImGui.TextDisabled(F("Text0836BB6E2FBE", data.Bindings.Count));
        if(data.Beds.Count==0){ImGui.Spacing();ImGui.TextWrapped(L("Text2040C3417A5C"));}
        float desiredHeight=Math.Clamp((VisibleBeds.Count()*185+(data.Preferences.GroupByHouse?GardenGrouping.Houses(VisibleBeds).Count*50:0))*S+16*S,180*S,430*S);
        float bodyHeight=Math.Min(Math.Max(180*S,ImGui.GetContentRegionAvail().Y-110*S),desiredHeight);
        ImGui.BeginChild("GardenOverview",new(0,bodyHeight),true);
        if(data.Preferences.GroupByHouse){
            foreach(var house in GardenGrouping.Houses(VisibleBeds)){
                ImGui.TextWrapped(house.Label);ImGui.TextDisabled(F("TextF0181DAF9737", house.Beds.Count));ImGui.Separator();
                foreach(var bed in house.Beds.ToList())DrawOverviewBed(bed);
            }
        }else foreach(var bed in VisibleBeds.ToList())DrawOverviewBed(bed);
        ImGui.EndChild();
        if(ImGui.Button(L("Text000CF763ECE4"))){Overview=false;RequestedTab=L("TextC9E0F3954B5C");}
        using(new Disabled(data.Busy)){
            ImGui.SameLine();if(ImGui.Button(L("TextE376E8ADBD45")))command(new(GardenUiCommandKind.Preview));
            ImGui.SameLine();if(ImGui.Button(L("Text1A74696FCD7C")))command(new(GardenUiCommandKind.Plant));
            if(ImGui.Button(L("Text602C8BC3F14E")))command(new(GardenUiCommandKind.Care));
            ImGui.SameLine();if(ImGui.Button(L("TextC21B197E84D3")))command(new(GardenUiCommandKind.Fertilize));
            ImGui.SameLine();if(ImGui.Button(L("Text0E7E37EE6749")))command(new(GardenUiCommandKind.Harvest));
        }
        ImGui.TextDisabled(L("Text6D5C8CF2837A"));
    }
    private void DrawOverviewBed(GardenBedView b)
    {
        ImGui.PushID($"{b.HouseId}-{b.GroupKey}");
        var binding=data.Bindings.FirstOrDefault(x=>x.HouseId==b.HouseId&&x.GroupKey==b.GroupKey);
        var preset=data.Layouts.FirstOrDefault(p=>p.Id==binding?.LayoutId);
        var rows=data.Records.Where(r=>r.HouseId==b.HouseId&&r.GroupKey==b.GroupKey).ToList();
        ImGui.TextWrapped(BedLabel(b));ImGui.TextDisabled(b.Nearby?F("Text1327527E9B22", b.Slots):L("Text55E63C9D2A06"));
        ImGui.TextUnformatted(L("Text7889BE2CF966")+(preset?.Name??L("TextE026C6693DC5")));
        ImGui.TextDisabled(F("Text50924E4237E2", Time(rows.Where(r=>r.Active).Select(r=>r.PlantedAt).Min()), Time(rows.Where(r=>r.Active).Select(r=>r.CaredAt).Min())));
        ImGui.TextUnformatted(L("Text071FBCC539B1")+DueText(rows,binding?.Reminders,GardenAction.Care)+L("Text597DBF065A28")+DueText(rows,binding?.Reminders,GardenAction.Fertilize));
        if(ImGui.Button(L("Text0677F30054B1"))){SelectBed(b);Overview=false;RequestedTab=L("TextC9E0F3954B5C");}
        ImGui.SameLine();if(ImGui.Button(L("Text0DBF5C72C69C"))){SelectBed(b);Overview=false;RequestedTab=L("Text0DBF5C72C69C");}
        using(new Disabled(data.Busy||!b.Nearby)){
            ImGui.SameLine();if(ImGui.Button(L("Text69CA8500A419")))command(new(GardenUiCommandKind.Care,b.HouseId,b.GroupKey));
            ImGui.SameLine();if(ImGui.Button(L("TextDDAE47CD1786")))command(new(GardenUiCommandKind.Fertilize,b.HouseId,b.GroupKey));
        }
        DrawDeleteBed(b);
        ImGui.Separator();ImGui.PopID();
    }
    private void DrawWorkbench()
    {
        if(BeginBoundedCombo(L("Text9F8B0DA2F890"),Bed==null?L("Text1182C5454F11"):BedLabel(Bed),250*S)){
            foreach(var b in VisibleBeds.ToList()){ImGui.PushID($"{b.HouseId}-{b.GroupKey}");if(ImGui.Selectable(BedLabel(b),b==Bed))SelectBed(b);if(ImGui.IsItemHovered())ImGui.SetTooltip(BedLabel(b));ImGui.PopID();}
            ImGui.EndCombo();
        }
        if(Bed is {} selectedBed)ImGui.TextWrapped(BedLabel(selectedBed));
        if(Bed is {} namedBed){
            string key=GardenNames.Key(namedBed.HouseId,namedBed.GroupKey);
            if(editingBedKey!=key){editingBedKey=key;bedNameEdit=namedBed.Name;}
            bool inlineRename=ImGui.GetContentRegionAvail().X>=360*S;
            ImGui.SetNextItemWidth(inlineRename?220*S:Math.Max(1,ImGui.GetContentRegionAvail().X));ImGui.InputText("##GardenBedName",ref bedNameEdit,81);
            if(inlineRename)ImGui.SameLine();if(ImGui.Button(L("TextB0493E50F288"))){
                try{
                    string name=GardenNames.Set(data.BedNames,namedBed.HouseId,namedBed.GroupKey,bedNameEdit);
                    var binding=Binding;if(binding!=null)binding.Name=name;
                    int at=data.Beds.IndexOf(namedBed);if(at>=0)data.Beds[at]=namedBed with{Name=name};
                    Save();localNotice=L("Text47BDE3679332");
                }catch(ArgumentException e){localNotice=e.Message;}
            }
        }
        if(Bed is {} deletableBed)DrawDeleteBed(deletableBed);
        if(ImGui.BeginTabBar("GardenWorkbenchTabs")){
            Tab(L("TextC9E0F3954B5C"),DrawLayout);Tab(L("Text0DBF5C72C69C"),DrawRecords);Tab(L("Text73201667202E"),DrawReminders);Tab(L("Text17337902D692"),DrawOperations);
            ImGui.EndTabBar();
        }
        RequestedTab="";
        if(activeTab==L("TextC9E0F3954B5C")){
            using(new Disabled(data.Busy||Bed is not {Nearby:true})){
                if(ImGui.Button(L("Text389C6CAA7B70")))Send(GardenUiCommandKind.Preview);
                ImGui.SameLine();if(ImGui.Button(L("Text33300F84CAC2")))Send(GardenUiCommandKind.Plant);
            }
        }
        using(new Disabled(data.Busy||Bed is not {Nearby:true})){
            if(ImGui.Button(L("TextA31DA82E8C3E")))Send(GardenUiCommandKind.Care);
            ImGui.SameLine();if(ImGui.Button(L("Text883FD78F653E")))Send(GardenUiCommandKind.Fertilize);
            ImGui.SameLine();if(ImGui.Button(L("Text0C5948A18120")))Send(GardenUiCommandKind.Harvest);
        }
    }
    private void DrawDeleteBed(GardenBedView bed)
    {
        string key=GardenNames.Key(bed.HouseId,bed.GroupKey);ImGui.PushID("deleteBed-"+key);
        using(new Disabled(data.Busy)){
            if(ImGui.Button(L("Text1A3B3AA31F3F")))deleteBedKey=deleteBedKey==key?"":key;
            if(deleteBedKey==key){
                ImGui.TextWrapped(L("Text5B64DEB3D98D"));
                if(ImGui.Button(L("TextA3EA3C17B401"))){
                    command(new(GardenUiCommandKind.RemoveGarden,bed.HouseId,bed.GroupKey));deleteBedKey="";
                    if(SelectedHouse==bed.HouseId&&SelectedGroup==bed.GroupKey){SelectedHouse=0;SelectedGroup=0;}
                }
                ImGui.SameLine();if(ImGui.Button(L("Text2CD0F3BE8738")))deleteBedKey="";
            }
        }
        ImGui.PopID();
    }
    private void Tab(string name,Action draw)
    {
        if(ImGui.BeginTabItem(name,RequestedTab==name?ImGuiTabItemFlags.SetSelected:ImGuiTabItemFlags.None)){
            activeTab=name;
            float height=Math.Clamp(ImGui.GetContentRegionAvail().Y-85*S,250*S,470*S);
            ImGui.BeginChild("tab-"+name,new(0,height),false);draw();ImGui.EndChild();ImGui.EndTabItem();
        }
    }
    private void DrawLayout()
    {
        using(new Disabled(data.Busy)){
            if(BeginBoundedCombo(L("Text972977B48445"),Preset?.Name??L("TextC2C6BB3B7664"),230*S)){
                foreach(var p in data.Layouts){ImGui.PushID(p.Id);if(ImGui.Selectable(p.Name,p==Preset)){data.SelectedLayoutId=p.Id;Save();}ImGui.PopID();}
                ImGui.EndCombo();
            }
            if(BeginBoundedCombo(L("Text20CA79FC956A"),F("Text0558A73DC22D", newLayoutSlots),100*S)){
                foreach(int count in new[]{4,6,8})if(ImGui.Selectable(F("Text0558A73DC22D", count),newLayoutSlots==count))newLayoutSlots=count;
                ImGui.EndCombo();
            }
            if(ImGui.Button(L("TextDCBE4EF55A55"))){
                var p=GardenPlanner.Create(newLayoutSlots,Bed?.Slots==newLayoutSlots?Bed.NativeSlots:null);p.Name=GardenPlanner.UniqueName(F("Text961CB99AFAE8", newLayoutSlots),data.Layouts);data.Layouts.Add(p);data.SelectedLayoutId=p.Id;SelectedCell=0;deleteConfirm=false;Save();
            }
            ImGui.SameLine();if(ImGui.Button(L("Text12F4907D4A1B"))){shareCode="";shareNotice="";showShare=true;}
            var preset=Preset;if(preset==null){ImGui.TextDisabled(L("Text5533D5C44E87"));return;}
            if(editedLayout!=preset.Id){editedLayout=preset.Id;nameEdit=preset.Name;deleteConfirm=false;}
            ImGui.SetNextItemWidth(210*S);ImGui.InputText("##LayoutName",ref nameEdit,65);
            ImGui.SameLine();if(ImGui.Button(L("Text83C37E8A4A5F"))){
                string name=nameEdit.Trim();
                if(name.Length is <1 or >64||name.Any(char.IsControl)||data.Layouts.Any(p=>p.Id!=preset.Id&&string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)))localNotice=L("Text2406FFD195E1");
                else{preset.Name=name;Save();}
            }
            ImGui.SameLine();if(ImGui.Button(L("Text4891F08CD3FF"))){
                var p=new GardenPreset{Name=GardenPlanner.UniqueName(preset.Name+L("Text6DED4E175CD7"),data.Layouts),DefaultSoilId=preset.DefaultSoilId,Cells=preset.Cells.Select(c=>new GardenCell{NativeSlot=c.NativeSlot,SeedId=c.SeedId,SoilId=c.SoilId}).ToList(),PlantOrder=new(preset.PlantOrder)};
                data.Layouts.Add(p);data.SelectedLayoutId=p.Id;Save();
            }
            ImGui.SameLine();if(ImGui.Button(deleteConfirm?L("TextA3EA3C17B401"):L("Text2F9DAA828907"))){
                if(deleteConfirm){data.Layouts.Remove(preset);data.Bindings.RemoveAll(b=>b.LayoutId==preset.Id);data.SelectedLayoutId=data.Layouts.FirstOrDefault()?.Id??"";deleteConfirm=false;Save();return;}deleteConfirm=true;
            }
            using(new Disabled(Bed==null||!Bed.Nearby||preset.Cells.Count!=Bed.Slots))if(ImGui.Button(L("TextCB4FBA11A986")))Send(GardenUiCommandKind.Bind);
            if(Bed!=null&&preset.Cells.Count!=Bed.Slots)ImGui.TextWrapped(F("TextFAC8BA101C60", Bed.Slots, preset.Cells.Count));
            if(Binding!=null){ImGui.SameLine();if(ImGui.Button(L("Text157989A96492")))Send(GardenUiCommandKind.Unbind);}
            ImGui.SameLine();ImGui.TextDisabled(L("TextFAA3DAB035C7")+(data.Layouts.FirstOrDefault(p=>p.Id==Binding?.LayoutId)?.Name??L("TextE026C6693DC5")));
            ImGui.Separator();
            if(preset.Cells==null||preset.Cells.Count is not (4 or 6 or 8)||preset.Cells.Any(c=>c==null)||preset.PlantOrder==null){ImGui.TextWrapped(L("TextC6A489896851"));return;}
            bool columns=ImGui.GetContentRegionAvail().X>=520*S;
            if(columns&&ImGui.BeginTable("GridAndEditor",2,ImGuiTableFlags.SizingStretchProp)){
                ImGui.TableSetupColumn(L("TextFAA2F2ED6735"),ImGuiTableColumnFlags.WidthStretch,1.35f);ImGui.TableSetupColumn(L("TextABE087F4B3C5"),ImGuiTableColumnFlags.WidthStretch,1);
                ImGui.TableNextRow();ImGui.TableNextColumn();DrawGrid(preset);ImGui.TableNextColumn();DrawEditor(preset);ImGui.EndTable();
            }else{DrawGrid(preset);ImGui.Separator();DrawEditor(preset);}
        }
    }
    private void DrawGrid(GardenPreset preset)
    {
        SelectedCell=Math.Clamp(SelectedCell,0,preset.Cells.Count-1);int[] ring=GardenPlanner.Grid(preset.Cells.Count);int columns=preset.Cells.Count==4?2:3;int rows=preset.Cells.Count==8?3:2;
        float width=Math.Max(65*S,(ImGui.GetContentRegionAvail().X-(columns-1)*8*S)/columns);
        for(int r=0;r<rows;r++)for(int c=0;c<columns;c++){
            if(c>0)ImGui.SameLine(0,6*S);int i=ring[r*columns+c];
            if(i<0){
                var origin=ImGui.GetCursorScreenPos();var cellSize=new Vector2(width,64*S);string label=F("Text8F96EA05108C", preset.Cells.Count);var textSize=ImGui.CalcTextSize(label);
                ImGui.Dummy(cellSize);ImGui.GetWindowDrawList().AddText(origin+(cellSize-textSize)/2,ImGui.GetColorU32(ImGuiCol.TextDisabled),label);continue;
            }
            var cell=preset.Cells[i];bool selected=SelectedCell==i;
            if(selected)ImGui.PushStyleColor(ImGuiCol.Button,ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            string name=Item(cell.SeedId);string shortName=name.Length>9?name[..8]+"…":name;
            if(ImGui.Button(F("TextD6A95703B17E", i+1, shortName, i),new(width,64*S))){SelectedCell=i;seedSearch=cellSoilSearch="";}
            if(selected)ImGui.PopStyleColor();
            if(ImGui.IsItemHovered())ImGui.SetTooltip(name+F("Text67811821AC3B", cell.NativeSlot+1));
        }
        ImGui.TextDisabled(F("Text8C7E4E44CC47", preset.Cells.Count));
        ImGui.TextWrapped(L("Text6724A2574B1D")+string.Join(" > ",preset.PlantOrder.Select(i=>i+1)));
        var record=BedRecords.FirstOrDefault(r=>r.NativeSlot==preset.Cells[SelectedCell].NativeSlot);
        ImGui.TextDisabled(L("TextFDEF305597E9")+Time(record?.PlantedAt));ImGui.TextDisabled(L("Text071FBCC539B1")+Time(record?.CaredAt));ImGui.TextDisabled(L("Text56CBD453708D")+Time(record?.FertilizedAt));
    }
    private bool ItemPicker(string id,string label,List<GardenItemOption> items,ref uint value,ref string search,bool allowEmpty=false)
    {
        ImGui.TextDisabled(label);
        bool changed=false;
        uint selected=value;
        float fieldWidth=Math.Max(1,Math.Min(360*S,ImGui.GetContentRegionAvail().X-4*S));
        if(BeginBoundedCombo(id,value==0?(allowEmpty?L("Text23E1665F9447"):id.Contains("override")?L("Text89A6F0471B72"):L("Text1182C5454F11")):items.FirstOrDefault(i=>i.Id==selected)?.Name??F("Text4BD2AB225AE6", value),fieldWidth)){
            // Never use negative fill-width input inside an auto-sizing popup.
            ImGui.SetNextItemWidth(Math.Max(1,ImGui.GetContentRegionAvail().X));ImGui.InputText(L("Text92065FFB5EE8")+id,ref search,96);
            if(allowEmpty&&ImGui.Selectable(L("Text23E1665F9447"),value==0)){value=0;changed=true;}
            int rendered=0;string query=search;
            foreach(var item in items.Where(i=>string.IsNullOrWhiteSpace(query)||i.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||i.Id.ToString().Contains(query)).Take(250)){
                if(ImGui.Selectable($"{item.Name}##item{item.Id}",item.Id==value)){value=item.Id;changed=true;}
                if(ImGui.IsItemHovered())ImGui.SetTooltip(item.Name);
                rendered++;
            }
            if(rendered==0)ImGui.TextDisabled(L("Text7730996A394D"));ImGui.EndCombo();
        }
        return changed;
    }
    private bool BeginBoundedCombo(string id,string preview,float requestedWidth)
    {
        int marker=id.IndexOf("##",StringComparison.Ordinal);string label=marker<0?id:id[..marker];
        float labelWidth=label.Length==0?0:ImGui.CalcTextSize(label).X+ImGui.GetStyle().ItemInnerSpacing.X;
        float fieldWidth=Math.Max(1,Math.Min(requestedWidth,Math.Max(1,ImGui.GetContentRegionAvail().X-labelWidth)));
        var viewport=ImGui.GetMainViewport().WorkSize;
        float popupWidth=Math.Min(Math.Clamp(fieldWidth,220*S,420*S),Math.Max(1,viewport.X-24*S));
        float popupHeight=Math.Min(320*S,Math.Max(1,viewport.Y-32*S));
        ImGui.SetNextItemWidth(fieldWidth);
        // The parent fixes the popup width before BeginCombo switches windows.
        ImGui.SetNextWindowSizeConstraints(new(popupWidth,0),new(popupWidth,popupHeight));
        return ImGui.BeginCombo(id,preview);
    }
    private void DrawEditor(GardenPreset p)
    {
        var cell=p.Cells[SelectedCell];ImGui.TextWrapped(F("Text0D92C8EF2C4E", SelectedCell+1, cell.NativeSlot+1));
        uint seed=cell.SeedId;if(ItemPicker("##seed",L("Text5E20FC223736"),data.Seeds,ref seed,ref seedSearch,true)){cell.SeedId=seed;Save();}
        using(new Disabled(cell.SeedId==0))if(ImGui.Button(L("TextC6AE39AB184A"))){clipboard.Copy(cell);localNotice=L("TextB42D66998A4D")+Item(clipboard.SeedId);}
        ImGui.SameLine();using(new Disabled(clipboard.SeedId==0))if(ImGui.Button(L("Text42651CE97A5D"))){clipboard.Paste(cell);Save();}
        ImGui.TextDisabled(L("Text6D20732C4232"));
        var candidates=GardenSeedClipboard.CandidateSeeds(p,data.Layouts);
        float candidateWidth=Math.Max(1,(ImGui.GetContentRegionAvail().X-ImGui.GetStyle().ItemSpacing.X)/2);
        for(int n=0;n<4;n++){
            uint id=candidates[n];if(n%2!=0)ImGui.SameLine();string full=id==0?L("TextBE1A6F5EBF75"):Item(id);string label=full;
            while(label.Length>1&&ImGui.CalcTextSize(label+(label==full?"":"…")).X>Math.Max(1,candidateWidth-2*ImGui.GetStyle().FramePadding.X))label=label[..^1];
            if(label!=full)label+="…";
            using(new Disabled(id==0))if(ImGui.Button(label+"##candidate"+n,new(candidateWidth,ImGui.GetFrameHeight()))){cell.SeedId=id;Save();}
            if(id!=0&&ImGui.IsItemHovered())ImGui.SetTooltip(full);
        }
        uint soil=p.DefaultSoilId;if(ItemPicker("##soil",L("Text02F1A371FB49"),data.Soils,ref soil,ref soilSearch)){p.DefaultSoilId=soil;Save();}
        uint overrideSoil=cell.SoilId;if(ItemPicker("##overrideSoil",L("Text99BD299E6406"),data.Soils,ref overrideSoil,ref cellSoilSearch)){cell.SoilId=overrideSoil;Save();}
        if(ImGui.Button(L("Text2C7E22630DF7"))){cell.SoilId=0;Save();}
        if(ImGui.Button(L("Text79B0A54090DC")))Send(GardenUiCommandKind.Calibrate);
        int order=p.PlantOrder.IndexOf(SelectedCell);
        if(ImGui.Button(L("TextEF5C77416E28"))&&order>0){(p.PlantOrder[order-1],p.PlantOrder[order])=(p.PlantOrder[order],p.PlantOrder[order-1]);Save();}
        ImGui.SameLine();if(ImGui.Button(L("Text2925C65A2BD3"))&&order>=0&&order<p.PlantOrder.Count-1){(p.PlantOrder[order+1],p.PlantOrder[order])=(p.PlantOrder[order],p.PlantOrder[order+1]);Save();}
    }
    private string RecordedSeed(GardenRecord? record)=>record==null?L("TextBDD2CB287011"):record.SeedId!=0?Item(record.SeedId):string.IsNullOrWhiteSpace(record.CropName)?L("TextBDD2CB287011"):F("TextF1BCDC0D31BD", record.CropName);
    private void DrawRecords()
    {
        ImGui.TextDisabled(L("Text7CE5BBCB659E"));
        if(ImGui.BeginTable("GardenRecords",5,ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersInnerH|ImGuiTableFlags.SizingStretchProp)){
            foreach(string label in new[]{L("TextB2ED7CB58887"),L("TextF2B57DBFDD7C"),L("Text1AF52ACC0106"),L("Text245D92BBF960"),L("Text335F69AF03CF")})ImGui.TableSetupColumn(label);
            ImGui.TableHeadersRow();
            foreach(int i in RecordSlots){
                var record=BedRecords.FirstOrDefault(r=>r.NativeSlot==i);ImGui.TableNextRow();ImGui.TableNextColumn();
                if(ImGui.Selectable((i+1)+L("Text7F4660153423")+i,selectedRecordSlot==i,ImGuiSelectableFlags.SpanAllColumns))selectedRecordSlot=i;
                ImGui.TableNextColumn();ImGui.TextWrapped(RecordedSeed(record));if(ImGui.IsItemHovered()&&record?.SeedIdentifiedAt is {} identified)ImGui.SetTooltip(F("Text3B2335DDF624", record.CropName, Time(identified)));ImGui.TextDisabled(GardenCropObservation.Name(record));
                ImGui.TableNextColumn();ImGui.TextWrapped(Time(record?.PlantedAt));ImGui.TableNextColumn();ImGui.TextWrapped(Time(record?.CaredAt));ImGui.TableNextColumn();ImGui.TextWrapped(Time(record?.FertilizedAt));
            }ImGui.EndTable();
        }
        ImGui.TextWrapped(L("TextB5D74A3CD691"));
        ImGui.Separator();ImGui.TextUnformatted(F("Text8853B77260FF", selectedRecordSlot+1));
        string[] actions={L("TextCFA6207EC025"),L("Text69CA8500A419"),L("TextDDAE47CD1786"),L("TextEA079BC32197")};
        if(BeginBoundedCombo(L("TextED31FBB483EE"),actions[manualAction],150*S)){
            for(int i=0;i<actions.Length;i++)if(ImGui.Selectable(actions[i],manualAction==i))manualAction=i;
            ImGui.EndCombo();
        }
        if(string.IsNullOrEmpty(manualTime))manualTime=data.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ImGui.SetNextItemWidth(210*S);ImGui.InputText(L("Text9F457338B705"),ref manualTime,40);
        using(new Disabled(data.Busy||Bed==null)){
            if(ImGui.Button(L("TextD976DAF6059C"))){
                if(DateTime.TryParseExact(manualTime,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var dt)){
                    var at=new DateTimeOffset(DateTime.SpecifyKind(dt,DateTimeKind.Local));
                    if(at>data.Now.AddMinutes(2))localNotice=L("Text3EBEB20A1809");
                    else command(new(GardenUiCommandKind.ManualRecord,SelectedHouse,SelectedGroup,data.SelectedLayoutId,selectedRecordSlot,(GardenAction)manualAction,at));
                }else localNotice=L("TextC85A760307E9");
            }
        }
        ImGui.SameLine();if(ImGui.Button(L("Text14FE1ED84EE6")))manualTime=data.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ImGui.TextDisabled(L("TextD273BCA035D5"));
    }
    private void DrawReminders()
    {
        var prefs=data.Preferences;bool enabled=prefs.RemindersEnabled;if(ImGui.Checkbox(L("TextCFA7DA4CF992"),ref enabled)){prefs.RemindersEnabled=enabled;Save();}
        bool chat=prefs.ChatReminder;if(ImGui.Checkbox(L("TextD3DF2CB34811"),ref chat)){prefs.ChatReminder=chat;Save();}
        ImGui.SameLine();bool sound=prefs.SoundReminder;if(ImGui.Checkbox(L("Text9CD88EEFAEC2"),ref sound)){prefs.SoundReminder=sound;Save();}
        bool quiet=prefs.QuietInCombat;if(ImGui.Checkbox(L("TextE7FA65456AC4"),ref quiet)){prefs.QuietInCombat=quiet;Save();}
        bool compact=prefs.CompactHome;if(ImGui.Checkbox(L("Text434FF978E57F"),ref compact)){prefs.CompactHome=compact;Save();}
        bool advance=prefs.AdvanceCareTalk;if(ImGui.Checkbox(L("Text829C10C8BFCF"),ref advance)){prefs.AdvanceCareTalk=advance;Save();}
        if(ImGui.IsItemHovered())ImGui.SetTooltip(L("Text22A10DF5A50C"));
        uint fertilizer=data.FertilizerId;if(ItemPicker("##fertilizer",L("TextFB451A39AF12"),data.Fertilizers,ref fertilizer,ref fertilizerSearch)){data.FertilizerId=fertilizer;Save();}
        ImGui.TextDisabled(L("TextB4AF1F2CABF1"));
        ImGui.Separator();var b=Binding;if(b==null){ImGui.TextWrapped(L("Text652411AF1B67"));return;}
        var rule=b.Reminders??=new();bool care=rule.CareEnabled;if(ImGui.Checkbox(L("TextD28629B1F701"),ref care)){rule.CareEnabled=care;Save();}
        int cm=rule.CareMinutes;ImGui.SetNextItemWidth(130*S);if(ImGui.InputInt(L("Text2203B787CB3D"),ref cm)){rule.CareMinutes=Math.Clamp(cm,1,43_200);Save();}
        bool fert=rule.FertilizerEnabled;if(ImGui.Checkbox(L("Text55887CF0F413"),ref fert)){rule.FertilizerEnabled=fert;Save();}
        int fm=rule.FertilizerMinutes;ImGui.SetNextItemWidth(130*S);if(ImGui.InputInt(L("TextF5B941B63FF5"),ref fm)){rule.FertilizerMinutes=Math.Clamp(fm,1,43_200);Save();}
        int repeat=rule.RepeatMinutes;ImGui.SetNextItemWidth(130*S);if(ImGui.InputInt(L("TextDD1384AE67EA"),ref repeat)){rule.RepeatMinutes=Math.Clamp(repeat,0,1440);Save();}
        ImGui.TextUnformatted(L("Text071FBCC539B1")+DueText(BedRecords,rule,GardenAction.Care));ImGui.TextUnformatted(L("Text56CBD453708D")+DueText(BedRecords,rule,GardenAction.Fertilize));
        ImGui.TextWrapped(L("TextAD29A08E1A06"));
    }
    private void DrawOperations()
    {
        ImGui.TextDisabled(L("Text47566C7CA554"));
        foreach(var row in data.Operations.Where(r=>SelectedGroup==0||(r.HouseId==SelectedHouse&&(r.GroupKey==SelectedGroup||r.GroupKey==0))).OrderByDescending(r=>r.At).Take(200))
            ImGui.TextWrapped($"{row.At.ToLocalTime():MM/dd HH:mm:ss}  {GardenLedger.ActionName(row.Action)}  {(row.NativeSlot<0?L("Text79FA1230B3F7"):F("Text5565F8254F5D", row.NativeSlot+1))}  {row.Stage}{(row.Manual?L("Text5B140B922B0D"):"")}");
    }
    private void DrawShare()
    {
        if(!showShare||shareDrawFrame==ImGui.GetFrameCount())return;
        shareDrawFrame=ImGui.GetFrameCount();ImGui.SetNextWindowSize(new(560*S,350*S),ImGuiCond.FirstUseEver);
        if(ImGui.Begin(L("TextF4D8C22939D4"),ref showShare,ImGuiWindowFlags.NoCollapse)){
            ImGui.TextWrapped(L("Text10FE95A596BA"));
            using(new Disabled(Preset==null))if(ImGui.Button(L("Text68A26702EF7F"))){
                try{shareCode=GardenShare.Export(Preset!);shareNotice=L("TextF708841160AB");}catch(Exception e){shareNotice=e.Message;}
            }
            ImGui.SameLine();if(ImGui.Button(L("TextAE71EE588850"))){ImGui.SetClipboardText(shareCode);shareNotice=L("TextE446DD387E50");}
            ImGui.SameLine();if(ImGui.Button(L("TextEF8629C8A5B7"))){string text=ImGui.GetClipboardText();if(text.Length<=GardenShare.MaxCodeLength)shareCode=text;else shareNotice=L("TextD534FA0E2807");}
            ImGui.InputTextMultiline("##GardenShareCode",ref shareCode,GardenShare.MaxCodeLength,new(-1,150*S));
            using(new Disabled(data.Busy))if(ImGui.Button(L("Text89C3159F3EDA"))){
                try{var p=GardenShare.Import(shareCode,data.Layouts,data.Seeds.Select(i=>i.Id).ToHashSet(),data.Soils.Select(i=>i.Id).ToHashSet());data.Layouts.Add(p);data.SelectedLayoutId=p.Id;Save();shareNotice=L("Text0E9B556DA1B4")+p.Name+L("Text487D90DB5B77");}
                catch(Exception e){shareNotice=e.Message;}
            }
            if(!string.IsNullOrEmpty(shareNotice))ImGui.TextWrapped(shareNotice);
        }ImGui.End();
    }
    private readonly struct Disabled:IDisposable
    {
        public Disabled(bool disabled)=>ImGui.BeginDisabled(disabled);
        public void Dispose()=>ImGui.EndDisabled();
    }
}
