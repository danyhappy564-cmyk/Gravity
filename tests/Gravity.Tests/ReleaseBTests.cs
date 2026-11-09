using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Gear;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using Xunit;
using Path = System.IO.Path;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Gravity.Tests;

public class ReleaseBTests
{
    private sealed class IdConverter : JsonConverter<MongoId>
    {
        public override MongoId Read(ref Utf8JsonReader r,Type t,JsonSerializerOptions o)=>new(r.GetString()!);
        public override MongoId ReadAsPropertyName(ref Utf8JsonReader r,Type t,JsonSerializerOptions o)=>new(r.GetString()!);
        public override void Write(Utf8JsonWriter w,MongoId v,JsonSerializerOptions o)=>w.WriteStringValue(v.ToString());
    }
    internal static string Id(int n) => n.ToString("x24");
    /// <summary>새 웹(poolVer 2)이 저장한 것처럼 무게 표를 읽는다.</summary>
    private static JsonNode V2(JsonNode n) { var o = n.AsObject(); if (o["gear"] is not JsonObject g) o["gear"] = g = new JsonObject(); g["poolVer"] ??= JsonValue.Create(2.0); return n; }
    private static GearSettings Config(string json) => ConfigModel.FromState(V2(JsonNode.Parse(json)!), []).Gear!;
    private static BotType Bot() => new() { BotInventory = new() { Equipment = new()
        { [EquipmentSlots.FirstPrimaryWeapon] = new() { [Id(1)] = 10, [Id(2)] = 10 } } } };
    private static GearItems Items() => new(new Dictionary<string, ItemInfo>
        { [Id(1)] = new(1, 0, 0, false, false, Weapon:true), [Id(2)] = new(2, 0, 0, false, false, Weapon:true) },
        [("FirstPrimaryWeapon", Id(1)), ("FirstPrimaryWeapon", Id(2))]);

    [Fact]
    public void ExplicitWeightsReplaceAutomaticWeightsAndZeroNeverReturns()
    {
        var g = Config("""{"tierData":{"pmc":[{"weapons":{"primaryShort":{"ONE":0,"TWO":123}}}]}}""".Replace("ONE", Id(1)).Replace("TWO", Id(2)));
        var bot = Bot();
        GearApply.Apply(bot, g, new("usec", "pmc", 0, true), Items(), new(null,0,0,0), false, new(1));
        var pool = bot.BotInventory!.Equipment![EquipmentSlots.FirstPrimaryWeapon];
        Assert.False(pool.ContainsKey(Id(1)));
        Assert.Equal(123, pool[Id(2)]);
        Assert.Equal(10, Bot().BotInventory!.Equipment![EquipmentSlots.FirstPrimaryWeapon][Id(1)]);
    }

    [Fact]
    public void AllZeroUserPoolIsNotRestoredByAutomaticSafetyFallback()
    {
        var g = Config("""{"tierData":{"pmc":[{"weapons":{"primaryLong":{"ONE":0,"TWO":0}}}]}}""".Replace("ONE", Id(1)).Replace("TWO", Id(2)));
        var bot = Bot();
        GearApply.Apply(bot, g, new("usec", "pmc", 0, true), Items(), new(null,0,0,0), false, new(1));
        Assert.Empty(bot.BotInventory!.Equipment![EquipmentSlots.FirstPrimaryWeapon]);
    }

    [Fact]
    public void CatalogUsesRealIdsNamesAndPriceTiersAndDoesNotMutateManualTier()
    {
        var items = new GearItems(Enumerable.Range(1,7).ToDictionary(Id, i=>new ItemInfo(i*10,0,0,false,i==7,Weapon:true)
            { Name="fallback",Section="weapons",Slot="ar",EquipmentSlots=["FirstPrimaryWeapon"],Source=i==7?"WTT-Test":"vanilla" }),
            Enumerable.Range(1,7).Select(i=>("FirstPrimaryWeapon",Id(i))));
        var rows = GearCatalog.Build(items, new Dictionary<string,string> { [Id(7)+" Name"]="모드 소총" }, new Dictionary<string,string>());
        Assert.Equal("모드 소총", rows[6].Ko);
        Assert.Equal("WTT-Test",rows[6].Source);
        Assert.Equal(7,rows[6].AutoTier);
        Assert.Equal("ar",rows[6].GunKind);
        Assert.Null(rows[6].ManualTier);
        Assert.Equal(("weapons","sniper"), ((Func<(string,string)>)(()=>{var c=GearCatalog.Classify(b=>b=="5447b6254bdc2dc3278b4568",false,"");return(c.Section,c.Slot);}))());
    }

    [Fact]
    public void SourceScanPrefersTheModThatDefinesTheItemOverModsThatOnlyReferenceIt()
    {
        string root=Path.Combine(Path.GetTempPath(),"gravity-source-"+Guid.NewGuid());
        foreach(var d in new[]{"Maker/db/CustomItems","LootMap/db","Compat/db/CustomItems","LocaleOnly/db/CustomLocales","Ref1","Ref2"})Directory.CreateDirectory(Path.Combine(root,d));
        try
        {
            // 만든 모드: CustomItems의 아이템 키(WTT 형식). 맵 모드·호환 모드는 전리품·호환 부품으로 참조만 한다.
            File.WriteAllText(Path.Combine(root,"Maker/db/CustomItems/gun.json"),$$"""{ "{{Id(21)}}": { "itemTplToClone": "5447a9cd4bdc2dbd208b4567" } }""");
            File.WriteAllText(Path.Combine(root,"LootMap/db/looseLoot.json"),$$"""{"items":[{"_tpl":"{{Id(21)}}"}]}""");
            File.WriteAllText(Path.Combine(root,"Compat/db/CustomItems/rail.json"),$$"""{"filters":["{{Id(21)}}"]}""");
            // 이름 번역만 가진 모드(CAG처럼 정의가 다른 형식): 번역 키가 참조보다 강하다.
            File.WriteAllText(Path.Combine(root,"LocaleOnly/db/CustomLocales/en.json"),$$"""{"{{Id(22)}} Name":"Cover"}""");
            File.WriteAllText(Path.Combine(root,"Ref1/a.json"),$$"""{"x":"{{Id(22)}}"}""");
            // 근거가 같은 두 모드: 출처 미상
            File.WriteAllText(Path.Combine(root,"Ref1/b.json"),$$"""{"x":"{{Id(23)}}"}""");
            File.WriteAllText(Path.Combine(root,"Ref2/b.json"),$$"""{"x":"{{Id(23)}}"}""");
            var src=GearItemSource.Scan(root,new HashSet<string>{Id(21),Id(22),Id(23)});
            Assert.Equal("Maker",src[Id(21)]);
            Assert.Equal("LocaleOnly",src[Id(22)]);
            Assert.False(src.ContainsKey(Id(23)));
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public void SourceScanUsesRealInstalledIdsAndLeavesAmbiguousOwnerUnknown()
    {
        string root=Path.Combine(Path.GetTempPath(),"gravity-source-"+Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root,"WTT-Test"));Directory.CreateDirectory(Path.Combine(root,"Other"));
        try
        {
            File.WriteAllText(Path.Combine(root,"WTT-Test","items.json"),$$"""{"id":"{{Id(8)}}","ref":"{{Id(9)}}"}""");
            File.WriteAllText(Path.Combine(root,"Other","register.cs"),$"Register(\"{Id(9)}\");");
            var sources=GearItemSource.Scan(root,new HashSet<string>{Id(8),Id(9)});
            Assert.Equal("WTT-Test",GearItemSource.Resolve(Id(8),new HashSet<string>(),sources));
            Assert.Equal("모드(출처 미상)",GearItemSource.Resolve(Id(9),new HashSet<string>(),sources));
            Assert.Equal("vanilla",GearItemSource.Resolve(Id(8),new HashSet<string>{Id(8)},sources));
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public void SidePlateMinimumAndUserWeightsAreClamped()
    {
        var clamped=new List<string>();
        var cfg=ConfigModel.FromState(JsonNode.Parse("""{"gear":{"poolVer":2,"tiers":[{"sMin":5,"side":3}]},"tierData":{"pmc":[{"weapons":{"ar":{"000000000000000000000001":-10,"000000000000000000000002":99999}}}]}}"""),clamped).Gear!;
        Assert.Equal(5,cfg.Tiers[0].SMin);Assert.Equal(5,cfg.Tiers[0].Side);
        Assert.Equal(0,cfg.TableFor("usec",0).Pools["weapons/ar"][Id(1)]);
        Assert.Equal(10000,cfg.TableFor("usec",0).Pools["weapons/ar"][Id(2)]);
        Assert.Equal(2,clamped.Count);
    }

    [Fact]
    public void SidePlateRangeRejectsLowerAndHigherClassesWhenCompatiblePlateExists()
    {
        var info=Enumerable.Range(3,3).ToDictionary(Id,i=>new ItemInfo(1,i,0,false,false,Plate:true));
        var items=new GearItems(info,[]);var set=new HashSet<MongoId>(info.Keys.Select(k=>(MongoId)k));
        var g=Config("""{"gear":{"tiers":[{"sMin":4,"side":4}]}}""");
        var c=GearApply.Context(g,new("usec","pmc",0,true),items,new(null,0,0,0),false,new(1));
        GearApply.FilterModSet(set,"left_side_plate",c);
        Assert.Equal(new[]{Id(4)},set.Select(x=>x.ToString()));
    }

    [Fact]
    public void DatabaseBuilderClassifiesModWeaponsAndPlateCompatibilityWithoutFakeIds()
    {
        var templates=JsonSerializer.Deserialize<TemplateTable>("""
        {"items":{
          "5447b5f14bdc2d61278b4567":{"_id":"5447b5f14bdc2d61278b4567","_type":"Node","_name":"AssaultRifle","_parent":"5422acb9af1c889c16000029","_props":{}},
          "000000000000000000000001":{"_id":"000000000000000000000001","_type":"Item","_name":"real_ar","_parent":"5447b5f14bdc2d61278b4567","_props":{}},
          "000000000000000000000002":{"_id":"000000000000000000000002","_type":"Item","_name":"carrier","_parent":"5448e5284bdc2dcb718b4567","_props":{"Slots":[{"_name":"Front_plate","_props":{"filters":[{"Filter":["000000000000000000000003"]}]}}]}},
          "000000000000000000000003":{"_id":"000000000000000000000003","_type":"Item","_name":"plate","_parent":"644120aa86ffbe10ee032b6f","_props":{"armorClass":"4"}}
        },"prices":{},"handbook":{"Items":[]}}
        """,new JsonSerializerOptions{NumberHandling=JsonNumberHandling.AllowReadingFromString,Converters={new IdConverter()},
            TypeInfoResolver=new DefaultJsonTypeInfoResolver{Modifiers={ti=>{foreach(var p in ti.Properties)p.IsRequired=false;}}}})!;
        var items=GearItemsBuilder.Build(templates,new BotTable{Types=new(),Base=null!,Core=null!},new HashSet<string>{Id(2),Id(3)});
        var rows=GearCatalog.Build(items,new Dictionary<string,string>(),new Dictionary<string,string>());
        Assert.Equal(3,rows.Count);
        Assert.Equal("ar",rows.Single(x=>x.Id==Id(1)).GunKind);
        Assert.True(rows.Single(x=>x.Id==Id(1)).Mod);
        Assert.Equal("모드(출처 미상)",rows.Single(x=>x.Id==Id(1)).Source);
        Assert.Equal("armouredRig",rows.Single(x=>x.Id==Id(2)).Slot);
        Assert.Equal(4,rows.Single(x=>x.Id==Id(3)).ArmorClass);
        Assert.Contains("front_plate",rows.Single(x=>x.Id==Id(3)).PlateSlots);
    }

    [Fact]
    public void SecondaryWeightsAndSourceSwitchDoNotChangePrimaryPool()
    {
        var info=new Dictionary<string,ItemInfo>{[Id(1)]=new(1,0,0,false,false,Weapon:true){Section="weapons",Slot="ar",EquipmentSlots=["FirstPrimaryWeapon","SecondPrimaryWeapon"]},
            [Id(2)]=new(1,0,0,false,true,Weapon:true){Section="weapons",Slot="ar",EquipmentSlots=["FirstPrimaryWeapon","SecondPrimaryWeapon"],Source="WTT-Test"}};
        var items=new GearItems(info,[]);
        var g=Config("""{"mods":{"disabledSources":["WTT-Test"]},"tierData":{"pmc":[{"weapons":{"ar":{"000000000000000000000001":13,"000000000000000000000002":77}},"secondaryWeapons":{"ar":{"000000000000000000000001":4}}}]}}""");
        var bot=Bot();bot.BotInventory!.Equipment![EquipmentSlots.SecondPrimaryWeapon]=new(){[Id(1)]=1};
        GearApply.Apply(bot,g,new("usec","pmc",0,true),items,new(null,0,0,0),false,new(1));
        Assert.Equal(13,bot.BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon][Id(1)]);
        Assert.Equal(4,bot.BotInventory.Equipment[EquipmentSlots.SecondPrimaryWeapon][Id(1)]);
        Assert.DoesNotContain((MongoId)Id(2),bot.BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon].Keys);
    }

    [Fact]
    public void ExplicitPartZeroAndGenerationDistributionAreApplied()
    {
        var g=Config("""{"tierData":{"pmc":[{"parts":{"scope":{"000000000000000000000001":0,"000000000000000000000002":1}},"gen":{"magazines":{"min":1,"max":3}},"genWeights":{"magazines":{"1":80,"3":20}}}]}}""");
        var items=new GearItems(new Dictionary<string,ItemInfo>{[Id(1)]=new(1,0,0,false,false){Section="parts",Slot="scope"},[Id(2)]=new(2,0,0,false,false){Section="parts",Slot="scope"}},[]);
        var ctx=GearApply.Context(g,new("usec","pmc",0,true),items,new(null,0,0,0),false,new(1));
        HashSet<MongoId> set=[Id(1),Id(2)];GearApply.FilterModSet(set,"mod_scope",ctx);
        Assert.Equal(new[]{Id(2)},set.Select(x=>x.ToString()));
        var bot=new BotType{BotGeneration=new(){Items=new(){Magazines=new(){Weights=new()}}}};
        GearApply.Apply(bot,ctx,false,new(1));
        Assert.Equal(80,bot.BotGeneration.Items.Magazines.Weights![1]);
        // T1 탄창 상한은 2이므로 3개 무게는 2개 칸으로 합친다.
        Assert.Equal(20,bot.BotGeneration.Items.Magazines.Weights[2]);
    }

    [Fact]
    public void BundledApbsRealisticPresetKeepsOriginalWeightsAndKeepsModWeaponsOut()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        for(int i=0;i<10&&dir!=null&&!File.Exists(Path.Combine(dir.FullName,"Gravity.sln"));i++)dir=dir.Parent;
        Assert.NotNull(dir);
        var preset=JsonNode.Parse(File.ReadAllText(Path.Combine(dir!.FullName,"src/Gravity.Server/wwwroot/presets/apbs-realistic-mip01.json")))!;
        Assert.Equal("mip01",(string?)preset["credits"]!["presetAuthor"]);
        var state=preset["settings"]!.DeepClone();state["tierData"]=preset["tables"]!.DeepClone();state["tierOwn"]=preset["ownTables"]!.DeepClone();
        var g=ConfigModel.FromState(V2(state!),[]).Gear!;
        const string m4="5447a9cd4bdc2dbd208b4567";
        var mod=Id(77);
        var items=new GearItems(new Dictionary<string,ItemInfo>{
            [m4]=new(100,0,0,false,false,Weapon:true){Section="weapons",Slot="ar",EquipmentSlots=["FirstPrimaryWeapon","SecondPrimaryWeapon"]},
            [mod]=new(100,0,0,false,true,Weapon:true){Section="weapons",Slot="special",EquipmentSlots=["FirstPrimaryWeapon"],Source="SomeWeaponMod"}},[]);
        int[] apbsLevels=[1,11,21,31,41,51,61];
        for(int tier=0;tier<7;tier++)
        {
            Assert.True(g.TableFor("usec",tier).Only);
            Assert.Equal(apbsLevels[tier],g.Tiers[tier].Min);
            var bot=new BotType{BotInventory=new(){Equipment=new(){[EquipmentSlots.FirstPrimaryWeapon]=new(){[m4]=999,[mod]=50}}},BotChances=new(){EquipmentChances=new(),EquipmentModsChances=new(),WeaponModsChances=new()}};
            GearApply.Apply(bot,g,new("usec","pmc",tier,true),items,new(null,0,0,0),false,new(1));
            var pool=bot.BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon];
            Assert.False(pool.ContainsKey(mod)); // 원작 목록에 없는 모드 무기는 빠진다
            var expected=(double?)preset["tables"]!["usec"]![tier]!["weapons"]!["ar"]![m4];
            if(expected is >0)Assert.Equal(expected.Value,pool[m4]); else Assert.False(pool.ContainsKey(m4));
            var mainPlate=new[]{65,80,90,95,100,100,100}[tier];
            Assert.Equal(mainPlate,bot.BotChances.EquipmentModsChances["front_plate"]);
            Assert.Equal((double)preset["tables"]!["usec"]![tier]!["slotChances"]!["Headwear"]!,bot.BotChances.EquipmentChances["Headwear"]);
        }
        // 원작에 없는 감염자·팩션 봇은 목록 전용 표를 쓰지 않는다
        Assert.False(g.TableFor("infected",0).Only);
        Assert.False(g.TableFor("ruaf",0).Only);
    }

    [Fact]
    public void AmmoAndMedsUsePriceTiersInsideTheirOwnGroup()
    {
        // 같은 구경 탄 7개(가격 1~7)와 의약품 7개: 가격순 7등분이라 i번째가 T(i). 관통은 모두 상한 아래.
        var info=new Dictionary<string,ItemInfo>();
        for(int i=1;i<=7;i++){info[Id(30+i)]=new(i,0,5,false,false){Section="ammo",Slot="fixture"};info[Id(40+i)]=new(i,0,0,false,false){Section="meds",Slot="meds"};}
        var items=new GearItems(info,info.Keys.Select(id=>(info[id].Section+"/"+info[id].Slot,id)));
        var g=Config("""{"gear":{"tiers":[{"pen":60},{"pen":60},{"pen":60}]}}""");
        BotType Bot()=>new(){BotInventory=new(){Equipment=new(),Ammo=new(){["fixture"]=Enumerable.Range(31,7).ToDictionary(i=>(MongoId)Id(i),_=>10.0)}},
            BotGeneration=new(){Items=new(){Healing=new(){Weights=new(){[1]=1},Whitelist=Enumerable.Range(41,7).ToDictionary(i=>(MongoId)Id(i),_=>1.0)}}}};
        var t1=Bot();GearApply.Apply(t1,g,new("usec","pmc",0,true),items,new(null,0,0,0),false,new(1));
        Assert.Equal(new[]{Id(31)},t1.BotInventory!.Ammo!["fixture"].Keys.Select(x=>x.ToString()));      // T1: 가장 싼 탄만
        Assert.Equal(new[]{Id(41)},t1.BotGeneration!.Items!.Healing!.Whitelist!.Keys.Select(x=>x.ToString()));
        var t3=Bot();GearApply.Apply(t3,g,new("usec","pmc",2,true),items,new(null,0,0,0),false,new(1));
        Assert.Equal(3,t3.BotInventory!.Ammo!["fixture"].Count);                                         // T3: T1~T3
        Assert.True(t3.BotInventory.Ammo["fixture"][Id(33)]>t3.BotInventory.Ammo["fixture"][Id(31)]);    // 낮은 티어일수록 적게
        Assert.Equal(3,t3.BotGeneration!.Items!.Healing!.Whitelist!.Count);
    }

    [Fact]
    public void ZeroAmmoDoesNotRestoreRoundsOrLeaveAWeaponRequiringThoseRounds()
    {
        var items=new GearItems(new Dictionary<string,ItemInfo>{[Id(1)]=new(1,0,0,false,false,Weapon:true){Section="weapons",Slot="ar",Caliber="fixture"},[Id(2)]=new(1,0,5,false,false){Section="ammo",Slot="fixture"}},[("FirstPrimaryWeapon",Id(1))]);
        var g=Config("""{"tierData":{"pmc":[{"ammo":{"fixture":{"000000000000000000000002":0}}}]}}""");
        var bot=new BotType{BotInventory=new(){Equipment=new(){[EquipmentSlots.FirstPrimaryWeapon]=new(){[Id(1)]=1}},Ammo=new(){["fixture"]=new(){[Id(2)]=1}}},BotChances=new(){EquipmentChances=new()}};
        GearApply.Apply(bot,g,new("usec","pmc",0,true),items,new(null,0,0,0),false,new(1));
        Assert.Empty(bot.BotInventory.Ammo["fixture"]);Assert.Empty(bot.BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon]);
        Assert.Equal(0,bot.BotChances.EquipmentChances["FirstPrimaryWeapon"]);
    }

    [Fact]
    public void NewWeaponCompatibilityIsCopiedAndCyclesAndDepthAreBounded()
    {
        var graph=new Dictionary<string,IReadOnlyDictionary<string,string[]>>
        {
            [Id(1)]=new Dictionary<string,string[]>{{"mod_barrel",[Id(2)]}},
            [Id(2)]=new Dictionary<string,string[]>{{"mod_mount",[Id(1),Id(3)]}}
        };
        var items=new GearItems(new Dictionary<string,ItemInfo>(),[],graph);
        var copy=new Dictionary<MongoId,Dictionary<string,HashSet<MongoId>>>();
        Assert.Equal(2,GearCompatibility.AddMissing(copy,[Id(1)],items));
        Assert.DoesNotContain((MongoId)Id(1),copy[Id(2)]["mod_mount"]);
        copy[Id(1)]["mod_barrel"].Clear();Assert.Single(graph[Id(1)]["mod_barrel"]);
        var chain=Enumerable.Range(1,100).ToDictionary(Id,i=>(IReadOnlyDictionary<string,string[]>)new Dictionary<string,string[]>{{"mod_mount",[Id(i+1)]}});
        copy.Clear();GearCompatibility.AddMissing(copy,[Id(1)],new(new Dictionary<string,ItemInfo>(),[],chain));
        Assert.Equal(GearCompatibility.MaxDepth,copy.Count);
    }

    [Fact]
    public void CatalogShowsWhyAnItemHasItsAutomaticTier()
    {
        // 같은 칸(주무기) 14개를 가격순으로 7등분: 가장 비싼 것은 14개 중 14위 → T7, 가장 싼 것은 1위 → T1
        var info=Enumerable.Range(1,14).ToDictionary(Id,i=>new ItemInfo(i*100,0,0,false,false,Weapon:true){Section="weapons",Slot="ar",EquipmentSlots=["FirstPrimaryWeapon"]});
        var items=new GearItems(info,info.Keys.Select(id=>("FirstPrimaryWeapon",id)));
        var cat=GearCatalog.Build(items,new Dictionary<string,string>(),new Dictionary<string,string>()).ToDictionary(x=>x.Id);
        Assert.Equal(("FirstPrimaryWeapon",14,14,7),(cat[Id(14)].TierGroup,cat[Id(14)].TierRank,cat[Id(14)].TierSize,cat[Id(14)].AutoTier));
        Assert.Equal((1,1),(cat[Id(1)].TierRank,cat[Id(1)].AutoTier));
        Assert.Equal((5,3),(cat[Id(5)].TierRank,cat[Id(5)].AutoTier));
    }

    [Fact]
    public void LegacyMockOnlyPools_AreIgnored_AsAuto()
    {
        // 0.5~0.6 웹은 예시 아이템(가짜 ID)으로 무게 표를 저장했다. 그런 칸은 자동이어야 한다(빈 표로 읽으면 주무기가 전부 빠짐).
        var cfg = ConfigModel.FromState(System.Text.Json.Nodes.JsonNode.Parse(
            """{"gear":{"poolVer":2},"tierData":{"pmc":[{"weapons":{"primaryLong":{"mosin":10,"ak74m":5}},"ammo":{"c556":{"59e690b686f7746c9f75e848":3}}}]}}"""), []);
        var t = cfg.Gear!.TableFor("usec", 0);
        Assert.DoesNotContain("weapons/primaryLong", t.Pools.Keys);
        Assert.Contains("ammo/c556", t.Pools.Keys);
    }

    [Fact]
    public void PoolsBeforePoolVer2_AreIgnored()
    {
        // 0.5~0.6 웹이 예시 목록으로 저장한 표(진짜 ID가 일부 섞임)는 적용하지 않는다.
        var cfg = ConfigModel.FromState(System.Text.Json.Nodes.JsonNode.Parse(
            """{"tierData":{"pmc":[{"ammo":{"c556":{"59e690b686f7746c9f75e848":3}}}]}}"""), []);
        Assert.Empty(cfg.Gear!.TableFor("usec", 0).Pools);
    }
}
