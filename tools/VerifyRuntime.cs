using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
class VerifyRuntime {
 static Dictionary<string,byte[]> libs=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
 static Assembly Resolve(object sender,ResolveEventArgs a) { var name=new AssemblyName(a.Name).Name; return libs.ContainsKey(name)?Assembly.Load(libs[name]):null; }
 static void Main(string[] args) {
  try {
   AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
   var asm=Assembly.Load(File.ReadAllBytes(args[0]));
   foreach(var n in asm.GetManifestResourceNames()) if(n.EndsWith(".resources")) {
    using(var rr=new ResourceReader(asm.GetManifestResourceStream(n))) foreach(DictionaryEntry e in rr) if(e.Value is byte[]) libs[(string)e.Key]=(byte[])e.Value;
   }
   var names=new [] {"PCL.PageDownloadInstall:LoaderInit","PCL.PageDownloadModify:LoadInstanceFinished","PCL.ModModpack:ModpackInstall","PCL.PageDownloadResourceDetail:InitializeStackResourceVersions","PCL.PageDownloadResourceDetail:PclVaccine_CreateResourceItem"};
   foreach(var pair in names) {
    var p=pair.Split(':');var type=asm.GetType(p[0],true);
    foreach(var m in type.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Where(m=>m.Name==p[1])) {
     RuntimeHelpers.PrepareMethod(m.MethodHandle); Console.WriteLine("JIT PASS "+m);
    }
   }
  } catch(Exception ex) {Console.WriteLine(ex.ToString());Environment.Exit(1);}
 }
}
