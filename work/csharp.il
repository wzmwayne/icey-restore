WARNING: The runtime version supported by this application is unavailable.
Using default runtime: v4.0.30319
.assembly extern mscorlib
{
  .ver 2:0:0:0
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 ) // .z\V.4..
}
.assembly extern UnityEngine
{
  .ver 0:0:0:0
}
.assembly extern System
{
  .ver 2:0:0:0
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 ) // .z\V.4..
}
.assembly extern 'Assembly-CSharp-firstpass'
{
  .ver 0:0:0:0
}
.assembly extern System.Core
{
  .ver 3:5:0:0
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 ) // .z\V.4..
}
.assembly extern DOTween
{
  .ver 1:0:0:0
}
.assembly extern UnityEngine.UI
{
  .ver 1:0:0:0
}
.assembly extern System.Xml
{
  .ver 2:0:0:0
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 ) // .z\V.4..
}
.assembly extern DOTween43
{
  .ver 1:0:0:0
}
.assembly 'Assembly-CSharp'
{
  .custom instance void class [mscorlib]System.Reflection.AssemblyTitleAttribute::'.ctor'(string) =  (
		01 00 0C 73 70 69 6E 65 2D 63 73 68 61 72 70 00   // ...spine-csharp.
		00                                              ) // .

  .custom instance void class [mscorlib]System.Reflection.AssemblyProductAttribute::'.ctor'(string) =  (
		01 00 0C 73 70 69 6E 65 2D 63 73 68 61 72 70 00   // ...spine-csharp.
		00                                              ) // .

  .custom instance void class [mscorlib]System.Reflection.AssemblyDescriptionAttribute::'.ctor'(string) =  (01 00 00 00 00 ) // .....

  .custom instance void class [mscorlib]System.Reflection.AssemblyCompanyAttribute::'.ctor'(string) =  (01 00 09 4D 69 63 72 6F 73 6F 66 74 00 00 ) // ...Microsoft..

  .custom instance void class [mscorlib]System.Reflection.AssemblyCopyrightAttribute::'.ctor'(string) =  (
		01 00 1B 43 6F 70 79 72 69 67 68 74 20 C2 A9 20   // ...Copyright .. 
		4D 69 63 72 6F 73 6F 66 74 20 32 30 31 33 00 00 ) // Microsoft 2013..

  .custom instance void class [mscorlib]System.Reflection.AssemblyTrademarkAttribute::'.ctor'(string) =  (01 00 00 00 00 ) // .....

  .custom instance void class [mscorlib]System.Runtime.InteropServices.ComVisibleAttribute::'.ctor'(bool) =  (01 00 00 00 00 ) // .....

  .custom instance void class [mscorlib]System.Runtime.InteropServices.GuidAttribute::'.ctor'(string) =  (
		01 00 24 33 61 63 38 35 36 37 65 2D 39 61 65 38   // ..$3ac8567e-9ae8
		2D 34 36 32 34 2D 38 37 62 39 2D 61 38 34 66 30   // -4624-87b9-a84f0
		31 30 31 63 36 32 39 00 00                      ) // 101c629..

  .custom instance void class [mscorlib]System.Runtime.CompilerServices.ExtensionAttribute::'.ctor'() =  (01 00 00 00 ) // ....

  .custom instance void class [mscorlib]System.Diagnostics.DebuggableAttribute::'.ctor'(valuetype [mscorlib]System.Diagnostics.DebuggableAttribute/DebuggingModes) =  (01 00 02 00 00 00 00 00 ) // ........

  .custom instance void class [mscorlib]System.Runtime.CompilerServices.RuntimeCompatibilityAttribute::'.ctor'() =  (
		01 00 01 00 54 02 16 57 72 61 70 4E 6F 6E 45 78   // ....T..WrapNonEx
		63 65 70 74 69 6F 6E 54 68 72 6F 77 73 01       ) // ceptionThrows.

  .permissionset reqmin = {[mscorlib]System.Security.Permissions.SecurityPermissionAttribute = {property bool 'SkipVerification' = bool(true)}}
  .hash algorithm 0x00008004
  .ver  1:0:0:0
}
.module 'Assembly-CSharp.dll' // GUID = {68297DD5-46D6-453B-AD5A-D5F1C187F585}

.custom instance void class [mscorlib]System.Security.UnverifiableCodeAttribute::'.ctor'() =  (01 00 00 00 ) // ....


.namespace FlyingWormConsole3
{
  .class public auto ansi beforefieldinit ConsoleProRemoteServer
  	extends [UnityEngine]UnityEngine.MonoBehaviour
  {
    .field  public  int32 port
    .field  private static  class [System]System.Net.HttpListener listener
    .field  public notserialized  class [mscorlib]System.Collections.Generic.List`1<<BROKEN CLASS token_ 2000004 due to Could not load file or assembly 'UnityEngine, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null' or one of its dependencies.>> logs

    // method line 1
    .method public hidebysig specialname rtspecialname 
           instance default void '.ctor' ()  cil managed 
    {
        // Method begins at RVA 0x21ac
	// Code size 29 (0x1d)
	.maxstack 8
	IL_0000:  ldarg.0 
	IL_0001:  ldc.i4 51000
	IL_0006:  stfld int32 FlyingWormConsole3.ConsoleProRemoteServer::port
	IL_000b:  ldarg.0 
	IL_000c:  newobj instance void class [mscorlib]System.Collections.Generic.List`1<class QueuedLog>::'.ctor'()
	IL_0011:  stfld class [mscorlib]System.Collections.Generic.List`1<class QueuedLog> FlyingWormConsole3.ConsoleProRemoteServer::logs
	IL_0016:  ldarg.0 
	IL_0017:  call instance void [UnityEngine]UnityEngine.MonoBehaviour::.ctor()
	IL_001c:  ret 
    } // end of method ConsoleProRemoteServer::.ctor

    // method line 2
    .method private hidebysig 
           instance default void Awake ()  cil managed 
    {
        // Method begins at RVA 0x1dd38
	// Code size 113 (0x71)
	.maxstack 4
	IL_0000:  ldarg.0 
	IL_0001:  call instance <BROKEN CLASS token_ 100001b due to Could not load file or assembly 'UnityEngine, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null' or one of its dependencies.> [UnityEngine]UnityEngine.Component::get_gameObject()
	IL_0006:  call void [UnityEngine]UnityEngine.Object::DontDestroyOnLoad(<BROKEN CLASS token_ 1000058 due to Could not load file or assembly 'UnityEngine, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null' or one of its dependencies.>)
	IL_000b:  ldstr "Starting Console Pro Server on port : "
	IL_0010:  ldarg.0 
	IL_0011:  ldfld int32 FlyingWormConsole3.ConsoleProRemoteServer::port
	IL_0016:  box [mscorlib]System.Int32
	IL_001b:  call string string::Concat(object, object)
	IL_0020:  call void [UnityEngine]UnityEngine.Debug::Log(object)
	IL_0025:  ldsfld class [System]System.Net.HttpListener FlyingWormConsole3.ConsoleProRemoteServer::listener
	IL_002a:  callvirt instance class [System]System.Net.HttpListenerPrefixCollection class [System]System.Net.HttpListener::get_Prefixes()
	IL_002f:  ldstr "http://*:"
	IL_0034:  ldarg.0 
	IL_0035:  ldfld int32 FlyingWormConsole3.ConsoleProRemoteServer::port
	IL_003a:  box [mscorlib]System.Int32
	IL_003f:  ldstr "/"
	IL_0044:  call string string::Concat(object, object, object)
	IL_0049:  callvirt instance void class [System]System.Net.HttpListenerPrefixCollection::Add(string)
	IL_004e:  ldsfld class [System]System.Net.HttpListener FlyingWormConsole3.ConsoleProRemoteServer::listener
	IL_0053:  callvirt instance void class [System]System.Net.HttpListener::Start()
	IL_0058:  ldsfld class [System]System.Net.HttpListener FlyingWormConsole3.ConsoleProRemoteServer::listener
	IL_005d:  ldarg.0 
	IL_005e:  ldftn instance void class FlyingWormConsole3.ConsoleProRemoteServer::ListenerCallback(class [mscorlib]System.IAsyncResult)
	IL_0064:  newobj instance void class [mscorlib]System.AsyncCallback::'.ctor'(object, native int)
	IL_0069:  ldnull 
	IL_006a:  callvirt instance class [mscorlib]System.IAsyncResult class [System]System.Net.HttpListener::BeginGetContext(class [mscorlib]System.AsyncCallback, object)
	IL_006f:  pop 
	IL_0070:  ret 
    } // end of method ConsoleProRemoteServer::Awake

    // method line 3
    .method private hidebysig 
           instance default void OnEnable ()  cil managed 
    {
        // Method begins at RVA 0x21ca
	// Code size 18 (0x12)
	.maxstack 8
	IL_0000:  ldarg.0 
	IL_0001:  ldftn Could not load signature of FlyingWormConsole3.ConsoleProRemoteServer:LogCallback due to: Could not load file or assembly 'UnityEngine, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null' or one of its dependencies.
