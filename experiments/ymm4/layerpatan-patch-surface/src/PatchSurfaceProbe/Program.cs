using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

if (args.Length < 1) throw new ArgumentException("usage: probe <YMM4Dir>");
var dir = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var p = Path.Combine(dir, name.Name + ".dll");
    return File.Exists(p) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(p) : null;
};
var ymm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dir, "YukkuriMovieMaker.dll"));
var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dir, "YukkuriMovieMaker.Plugin.dll"));

var op1 = typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)
    .Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!)
    .Where(o=>o.Size==1).ToDictionary(o=>(byte)o.Value);
var op2 = typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)
    .Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!)
    .Where(o=>o.Size==2).ToDictionary(o=>(byte)(o.Value & 0xff));

record Ins(int Offset, OpCode Op, object? Operand);

List<Ins> Decode(MethodBase m)
{
    var body=m.GetMethodBody();
    if(body is null) return [];
    var il=body.GetILAsByteArray()!;
    var list=new List<Ins>();
    var module=m.Module;
    var typeArgs=m.DeclaringType?.GetGenericArguments();
    var methArgs=m is MethodInfo mi ? mi.GetGenericArguments() : null;
    int i=0;
    while(i<il.Length)
    {
        int offset=i;
        OpCode op;
        byte b=il[i++];
        if(b==0xfe) op=op2[il[i++]]; else op=op1[b];
        object? operand=null;
        int ReadI4(){var v=BitConverter.ToInt32(il,i);i+=4;return v;}
        long ReadI8(){var v=BitConverter.ToInt64(il,i);i+=8;return v;}
        switch(op.OperandType)
        {
            case OperandType.InlineNone: break;
            case OperandType.ShortInlineI: operand=(sbyte)il[i++]; break;
            case OperandType.InlineI: operand=ReadI4(); break;
            case OperandType.InlineI8: operand=ReadI8(); break;
            case OperandType.ShortInlineR: operand=BitConverter.ToSingle(il,i);i+=4;break;
            case OperandType.InlineR: operand=BitConverter.ToDouble(il,i);i+=8;break;
            case OperandType.ShortInlineBrTarget: operand=(sbyte)il[i++]+i;break;
            case OperandType.InlineBrTarget: {var d=ReadI4();operand=d+i;break;}
            case OperandType.InlineSwitch:
                {var n=ReadI4();var basePos=i+4*n;var a=new int[n];for(int k=0;k<n;k++){a[k]=BitConverter.ToInt32(il,i)+basePos;i+=4;}operand=a;break;}
            case OperandType.ShortInlineVar: operand=il[i++];break;
            case OperandType.InlineVar: operand=BitConverter.ToUInt16(il,i);i+=2;break;
            case OperandType.InlineString:
                {var t=ReadI4();try{operand=module.ResolveString(t);}catch{operand=$"str:0x{t:X8}";}break;}
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineType:
            case OperandType.InlineTok:
            case OperandType.InlineSig:
                {
                    var t=ReadI4();
                    try
                    {
                        operand=op.OperandType switch {
                            OperandType.InlineField=>module.ResolveField(t,typeArgs,methArgs),
                            OperandType.InlineMethod=>module.ResolveMethod(t,typeArgs,methArgs),
                            OperandType.InlineType=>module.ResolveType(t,typeArgs,methArgs),
                            OperandType.InlineTok=>module.ResolveMember(t,typeArgs,methArgs),
                            _=>$"token:0x{t:X8}"
                        };
                    } catch { operand=$"token:0x{t:X8}"; }
                    break;
                }
            default: throw new NotSupportedException(op.OperandType.ToString());
        }
        list.Add(new(offset,op,operand));
    }
    return list;
}

var ymmSettings=ymm.GetType("YukkuriMovieMaker.Settings.YMMSettings")!;
var settingsBaseDef=plugin.GetType("YukkuriMovieMaker.Plugin.SettingsBase`1")!;
var settingsBase=settingsBaseDef.MakeGenericType(ymmSettings);
var getDefault=settingsBase.GetProperty("Default")!.GetGetMethod()!;
var getLayerHeight=ymmSettings.GetProperty("LayerHeight")!.GetGetMethod()!;

bool IsCall(Ins x, MethodBase target)=> (x.Op==OpCodes.Call || x.Op==OpCodes.Callvirt) && x.Operand is MethodBase mb && mb.MetadataToken==target.MetadataToken && mb.Module==target.Module;
bool IsDiv(List<Ins> a,int i)=>i>=1&&i+3<a.Count&&IsCall(a[i-1],getDefault)&&IsCall(a[i],getLayerHeight)&&a[i+1].Op==OpCodes.Conv_R8&&a[i+2].Op==OpCodes.Div&&a[i+3].Op==OpCodes.Conv_I4;
bool IsMul(List<Ins> a,int i)=>i>=1&&i+1<a.Count&&IsCall(a[i-1],getDefault)&&IsCall(a[i],getLayerHeight)&&a[i+1].Op==OpCodes.Mul;

IEnumerable<MethodInfo> AllMethods(Type t)
{
    const BindingFlags flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
    foreach(var m in t.GetMethods(flags)) yield return m;
    foreach(var nt in t.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))
        if(!nt.ContainsGenericParameters) foreach(var m in AllMethods(nt)) yield return m;
}

Console.WriteLine($"ymm_version={ymm.GetName().Version}");
Console.WriteLine($"plugin_version={plugin.GetName().Version}");

var total=0;
foreach(var typeName in new[]{"YukkuriMovieMaker.ViewModels.MainViewModel","YukkuriMovieMaker.ViewModels.TimelineViewModel"})
{
    var t=ymm.GetType(typeName)!;
    foreach(var m in AllMethods(t))
    {
        List<Ins> il;
        try{il=Decode(m);}catch{continue;}
        int d=0,u=0;
        for(int i=0;i<il.Count;i++){if(IsDiv(il,i))d++;if(IsMul(il,i))u++;}
        if(d+u>0)
        {
            total+=d+u;
            Console.WriteLine($"site|{t.Name}|{m.DeclaringType?.FullName}|{m.Name}|visibility={(m.IsPublic?"public":m.IsFamily?"protected":m.IsAssembly?"internal":"private")}|y_to_layer={d}|layer_to_y={u}");
        }
    }
}
Console.WriteLine($"site_total={total}");

var targets = new (string label,string type,string member,string kind)[]
{
    ("item_top","YukkuriMovieMaker.ViewModels.TimelineItemViewModel","Top","property"),
    ("item_height","YukkuriMovieMaker.ViewModels.TimelineItemViewModel","Height","property"),
    ("item_update_height","YukkuriMovieMaker.ViewModels.TimelineItemViewModel","UpdateItemHeight","method"),
    ("label_top","YukkuriMovieMaker.ViewModels.TimelineLayerLabelItemViewModel","Top","property"),
    ("label_height","YukkuriMovieMaker.ViewModels.TimelineLayerLabelItemViewModel","Height","property"),
    ("line_top","YukkuriMovieMaker.ViewModels.TimelineLayerLineViewModel","Top","property"),
    ("line_height","YukkuriMovieMaker.ViewModels.TimelineLayerLineViewModel","Height","property"),
    ("delta","YukkuriMovieMaker.ViewModels.TimelineItemViewModel","GetDeltaFrameAndLayer","method"),
    ("item_mousemove","YukkuriMovieMaker.Views.TimelineItemView","OnMouseMove","method"),
    ("add_position","YukkuriMovieMaker.Views.Converters.AddItemCommandParameterConverterBase","GetTimelinePosition","method"),
    ("space_top","YukkuriMovieMaker.ViewModels.TimelineItemSpaceViewModel","Top","property"),
    ("space_height","YukkuriMovieMaker.ViewModels.TimelineItemSpaceViewModel","Height","property"),
    ("scroll_to_item","YukkuriMovieMaker.ViewModels.TimelineViewModel","ScrollToItem","method"),
    ("background_top","YukkuriMovieMaker.ViewModels.TimelineItemBackgroundViewModel","Top","property"),
    ("background_height","YukkuriMovieMaker.ViewModels.TimelineItemBackgroundViewModel","Height","property"),
};
const BindingFlags BF=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
foreach(var x in targets)
{
    var t=ymm.GetType(x.type);
    if(t is null){Console.WriteLine($"target|{x.label}|missing_type");continue;}
    if(x.kind=="property")
    {
        var p=t.GetProperty(x.member,BF);
        var g=p?.GetGetMethod(true);var s=p?.GetSetMethod(true);
        Console.WriteLine($"target|{x.label}|type={x.type}|property={x.member}|getter={(g is null?"none":g.IsPublic?"public":"nonpublic")}|setter={(s is null?"none":s.IsPublic?"public":"nonpublic")}");
    }
    else
    {
        var ms=t.GetMethods(BF).Where(m=>m.Name==x.member).ToArray();
        Console.WriteLine($"target|{x.label}|type={x.type}|method={x.member}|count={ms.Length}|vis={string.Join(",",ms.Select(m=>m.IsPublic?"public":m.IsFamily?"protected":m.IsAssembly?"internal":"private"))}");
    }
}
