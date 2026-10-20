using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

class BuildPatch {
 static ModuleDefMD target;
 static Importer importer;
 static TypeDef T(string n) { return target.GetTypes().Single(t=>t.FullName==n); }
 static MethodDef M(string t,string n,int count) { return T(t).Methods.Single(m=>m.Name==n && m.MethodSig.Params.Count==count); }
 static string Hash(byte[] b) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-",""); }
 static object Map(object op, Dictionary<Instruction,Instruction> instructions, MethodDef from, MethodDef to) {
  if(op==null) return null;
  if(op is Instruction) return instructions[(Instruction)op];
  if(op is Instruction[]) return ((Instruction[])op).Select(i=>instructions[i]).ToArray();
  if(op is Local) return to.Body.Variables[((Local)op).Index];
  if(op is Parameter) return to.Parameters[((Parameter)op).Index];
  var method=op as MethodDef;
  if(method!=null) return T(method.DeclaringType.FullName).Methods.Single(m=>m.FullName==method.FullName);
  var field=op as FieldDef;
  if(field!=null) return T(field.DeclaringType.FullName).Fields.Single(f=>f.FullName==field.FullName);
  var type=op as TypeDef;
  if(type!=null) return T(type.FullName);
  var ms=op as MethodSpec;
  if(ms!=null) return importer.Import(ms);
  var mr=op as MemberRef;
  if(mr!=null) return importer.Import(mr);
  var tr=op as ITypeDefOrRef;
  if(tr!=null) return importer.Import(tr);
  return op;
 }
 static void CloneManualEntry(ModuleDefMD previous) {
  var from=previous.GetTypes().Single(t=>t.FullName=="PCL.ModModpack").Methods.Single(m=>m.Name=="ModpackInstall" && m.MethodSig.Params.Count==0);
  var to=M("PCL.ModModpack","ModpackInstall",0);
  var body=new CilBody(); body.InitLocals=from.Body.InitLocals;
  foreach(var v in from.Body.Variables) {
   if(v.Type.FullName.StartsWith("PCL.")) body.Variables.Add(new Local(T(v.Type.FullName).ToTypeSig()));
   else body.Variables.Add(new Local(importer.Import(v.Type)));
  }
  to.Body=body;
  var map=from.Body.Instructions.ToDictionary(i=>i,i=>new Instruction(i.OpCode));
  foreach(var i in from.Body.Instructions) { var ni=map[i]; ni.Operand=Map(i.Operand,map,from,to); body.Instructions.Add(ni); }
  if(from.Body.ExceptionHandlers.Count!=0) throw new Exception("Unexpected manual entry EH");
  Console.WriteLine("Restored manual modpack entry.");
 }
 static void RestorePackDispatch(ModuleDefMD previous) {
  var from=previous.GetTypes().Single(t=>t.FullName=="PCL.ModModpack").Methods.Single(m=>m.Name=="ModpackInstall" && m.MethodSig.Params.Count==3);
  var to=M("PCL.ModModpack","ModpackInstall",3);
  if(from.Body.Variables.Count!=to.Body.Variables.Count) throw new Exception("Unexpected locals");
  for(int i=0;i<from.Body.Variables.Count;i++) if(from.Body.Variables[i].Type.FullName!=to.Body.Variables[i].Type.FullName) throw new Exception("Local type mismatch");
  var sourceCatch=from.Body.ExceptionHandlers.Single(h=>h.HandlerType==ExceptionHandlerType.Catch);
  var sourceFinally=from.Body.ExceptionHandlers.Where(h=>h.HandlerType==ExceptionHandlerType.Finally).Last();
  var list=from.Body.Instructions;
  int begin=list.IndexOf(sourceCatch.HandlerEnd),end=list.IndexOf(sourceFinally.HandlerStart);
  var retBegin=sourceFinally.HandlerEnd;
  var originals=list.Skip(begin).Take(end-begin).Concat(list.Skip(list.IndexOf(retBegin))).ToArray();
  var destList=to.Body.Instructions;
  var anchor=destList.Single(i=>i.OpCode==OpCodes.Ldstr && ((string)i.Operand).Contains("不再提供 MC 下载"));
  int destAt=destList.IndexOf(anchor);
  if(destList[destAt+1].OpCode!=OpCodes.Newobj || destList[destAt+2].OpCode!=OpCodes.Throw) throw new Exception("Unexpected disabled dispatch");
  var map=originals.ToDictionary(i=>i,i=>new Instruction(i.OpCode));
  map[originals[0]]=anchor;
  foreach(var old in originals) { map[old].OpCode=old.OpCode; map[old].Operand=Map(old.Operand,map,from,to); }
  destList.RemoveAt(destAt+2);destList.RemoveAt(destAt+1);
  for(int i=1;i<end-begin;i++) destList.Insert(destAt+i,map[originals[i]]);
  var destinationFinally=to.Body.ExceptionHandlers.Where(h=>h.HandlerType==ExceptionHandlerType.Finally).Last();
  destinationFinally.HandlerEnd=map[retBegin];
  foreach(var old in list.Skip(list.IndexOf(retBegin))) destList.Add(map[old]);
  to.Body.SimplifyBranches();
  Console.WriteLine("Restored modpack dispatch; preserved original parsing and finally cleanup.");
 }
 static void RestoreLoader() {
  var method=M("PCL.PageDownloadInstall","LoaderInit",0);
  var list=method.Body.Instructions;
  var anchor=list.Single(i=>i.OpCode==OpCodes.Ldstr && (string)i.Operand=="关闭下载");
  int begin=list.IndexOf(anchor),end=begin;
  while(end<list.Count && !(list[end].OpCode==OpCodes.Newobj && ((IMethod)list[end].Operand).DeclaringType.FullName.StartsWith("PCL.ModLoader/LoaderTask`2"))) end++;
  if(end==list.Count) throw new Exception("Disabled loader not found");
  var field=T("PCL.ModDownload").Fields.Single(f=>f.Name=="DlClientListLoader");
  anchor.OpCode=OpCodes.Ldsfld;anchor.Operand=field;
  for(int i=end;i>begin;i--) list.RemoveAt(i);
  method.Body.SimplifyBranches();
  Console.WriteLine("Restored Minecraft list loader.");
 }
 static void RestoreModify() {
  var method=M("PCL.PageDownloadModify","LoadInstanceFinished",1);
  var list=method.Body.Instructions;
  var get=list.Single(i=>i.OpCode==OpCodes.Callvirt && ((IMethod)i.Operand).Name=="get_CardMinecraft");
  var constant=list[list.IndexOf(get)+1];
  if(constant.OpCode!=OpCodes.Ldc_I4_2) throw new Exception("Unexpected MC card state");
  constant.OpCode=OpCodes.Ldc_I4_0;
  Console.WriteLine("Restored MC version card.");
 }
 static void RestoreResourceInstall() {
  var type=T("PCL.PageDownloadResourceDetail");
  var method=M(type.FullName,"InitializeStackResourceVersions",1);
  var list=method.Body.Instructions;
  var call=list.Single(i=>i.OpCode==OpCodes.Callvirt && ((IMethod)i.Operand).Name=="ToListItem");
  var toList=(IMethod)call.Operand;
  int end=list.IndexOf(call),start=end-6;
  var versionLoad=list[start];var boolLoad=list[end-1];
  if(list[start+1].OpCode!=OpCodes.Ldarg_0 || list[start+2].OpCode!=OpCodes.Ldftn || list[end-2].OpCode!=OpCodes.Ldnull) throw new Exception("Unexpected resource item sequence");
  var listCtor=(IMethod)list[start+3].Operand;
  var save=M(type.FullName,"Save_Click",2);
  var install=M(type.FullName,"Install_Click",2);
  var iconCtor=T("PCL.MyIconButton/ClickEventHandler").Methods.Single(m=>m.Name==".ctor");
  var helper=new MethodDefUser("PclVaccine_CreateResourceItem",MethodSig.CreateInstance(toList.MethodSig.RetType,T("PCL.ResourceVersion").ToTypeSig(),target.CorLibTypes.Boolean),MethodImplAttributes.IL|MethodImplAttributes.Managed,MethodAttributes.Private|MethodAttributes.HideBySig);
  type.Methods.Add(helper);helper.Body=new CilBody();
  var h=helper.Body.Instructions;var normal=Instruction.Create(OpCodes.Ldarg_1);
  h.Add(Instruction.Create(OpCodes.Ldarg_0));h.Add(Instruction.Create(OpCodes.Ldfld,type.Fields.Single(f=>f.Name=="TargetResourceType")));h.Add(Instruction.Create(OpCodes.Ldc_I4_2));h.Add(Instruction.Create(OpCodes.Bne_Un,normal));
  h.Add(Instruction.Create(OpCodes.Ldarg_1));h.Add(Instruction.Create(OpCodes.Ldarg_0));h.Add(Instruction.Create(OpCodes.Ldftn,install));h.Add(Instruction.Create(OpCodes.Newobj,listCtor));
  h.Add(Instruction.Create(OpCodes.Ldarg_0));h.Add(Instruction.Create(OpCodes.Ldftn,save));h.Add(Instruction.Create(OpCodes.Newobj,iconCtor));h.Add(Instruction.Create(OpCodes.Ldarg_2));h.Add(Instruction.Create(OpCodes.Callvirt,toList));h.Add(Instruction.Create(OpCodes.Ret));
  h.Add(normal);h.Add(Instruction.Create(OpCodes.Ldarg_0));h.Add(Instruction.Create(OpCodes.Ldftn,save));h.Add(Instruction.Create(OpCodes.Newobj,listCtor));h.Add(Instruction.Create(OpCodes.Ldnull));h.Add(Instruction.Create(OpCodes.Ldarg_2));h.Add(Instruction.Create(OpCodes.Callvirt,toList));h.Add(Instruction.Create(OpCodes.Ret));
  var savedVersion=new Instruction(versionLoad.OpCode,versionLoad.Operand);
  var savedBool=new Instruction(boolLoad.OpCode,boolLoad.Operand);
  versionLoad.OpCode=OpCodes.Ldarg_0;versionLoad.Operand=null;
  for(int i=end;i>start;i--) list.RemoveAt(i);
  list.Insert(start+1,savedVersion);list.Insert(start+2,savedBool);list.Insert(start+3,Instruction.Create(OpCodes.Call,helper));
  method.Body.SimplifyBranches();
  Console.WriteLine("Restored resource page install/save actions.");
 }
 static void MarkBuild() {
  foreach(var type in target.GetTypes()) foreach(var method in type.Methods) {
   if(!method.HasBody) continue;
   foreach(var i in method.Body.Instructions) {
    if(i.OpCode!=OpCodes.Ldstr) continue;
    var s=(string)i.Operand;
    if(s.StartsWith("删除：为遵守相关法律法规，PCL 暂时不再提供 MC 下载")) i.Operand="恢复：PCL下载修复疫苗已在本地恢复下载入口（第三方修改）。";
    else if(s=="正式版 2.99.0.0") i.Operand="PCL 疫苗本地修复版 2.99.0.0";
   }
  }
 }
 static void Main(string[] args) {
  try { Run(args); } catch(Exception ex) { Console.WriteLine(ex.GetType().FullName + ": " + ex.Message + "\n" + ex.StackTrace); Environment.Exit(1); }
 }
 static void Run(string[] args) {
  var input=File.ReadAllBytes(args[0]);
  if(Hash(input)!="6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386") throw new Exception("Unsupported input hash");
  using(target=ModuleDefMD.Load(input)) using(var previous=ModuleDefMD.Load(args[1])) {
   importer=new Importer(target,ImporterOptions.TryToUseTypeDefs);
   RestoreLoader();RestoreModify();CloneManualEntry(previous);RestorePackDispatch(previous);RestoreResourceInstall();MarkBuild();
   foreach(var type in target.GetTypes()) foreach(var method in type.Methods) if(method.HasBody) method.Body.SimplifyBranches();
   target.Write(args[2],new ModuleWriterOptions(target));
  }
  using(var check=ModuleDefMD.Load(args[2])) {
   var self=check.GetAssemblyRefs().Where(r=>r.Name=="Plain Craft Launcher 2").ToArray();
   if(self.Length>0) throw new Exception("Accidental self assembly reference");
   Console.WriteLine("Output SHA256: "+Hash(File.ReadAllBytes(args[2])));
  }
 }
}
