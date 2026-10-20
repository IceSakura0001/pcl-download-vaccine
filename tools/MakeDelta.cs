using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Security.Cryptography;
class MakeDelta {
 static byte[] Hash(byte[] b) { using(var h=SHA256.Create()) return h.ComputeHash(b); }
 static void Main(string[] args) {
  byte[] source=File.ReadAllBytes(args[0]),target=File.ReadAllBytes(args[1]);
  var index=new Dictionary<string,int>();
  for(int i=0;i+64<=source.Length;i+=64) {var key=Convert.ToBase64String(source,i,64);if(!index.ContainsKey(key))index.Add(key,i);}
  int pos=0,literal=0,copies=0,literals=0;
  using(var fs=File.Create(args[2])) using(var gzip=new GZipStream(fs,CompressionMode.Compress)) using(var w=new BinaryWriter(gzip)) {
   w.Write(System.Text.Encoding.ASCII.GetBytes("PVAC0001"));w.Write(Hash(source));w.Write(Hash(target));w.Write(target.Length);
   while(pos+64<=target.Length) {
    int at;
    if(index.TryGetValue(Convert.ToBase64String(target,pos,64),out at)) {
     if(pos>literal){w.Write((byte)1);w.Write(pos-literal);w.Write(target,literal,pos-literal);literals++;}
     int length=64;while(at+length<source.Length&&pos+length<target.Length&&source[at+length]==target[pos+length])length++;
     w.Write((byte)0);w.Write(at);w.Write(length);copies++;pos+=length;literal=pos;
    } else pos++;
   }
   if(literal<target.Length){w.Write((byte)1);w.Write(target.Length-literal);w.Write(target,literal,target.Length-literal);literals++;}
   w.Write((byte)255);
  }
  Console.WriteLine("Delta: "+new FileInfo(args[2]).Length+" bytes; "+copies+" copies, "+literals+" literal blocks.");
 }
}
