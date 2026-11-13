using System;
using System.Buffers.Binary;
using System.Reflection;
using WorkIsland.App;

internal static partial class Program
{
    private static void RunInputOriginChecks(Action<string,bool,object?> check)
    {
        const BindingFlags fields=BindingFlags.Static|BindingFlags.NonPublic;
        var type=typeof(WindowsActivity);var pointer=type.GetField("pointerAt",fields)!;var keyboard=type.GetField("keyboardAt",fields)!;var target=type.GetField("pointerWindow",fields)!;
        var savedPointer=pointer.GetValue(null);var savedKeyboard=keyboard.GetValue(null);var savedTarget=target.GetValue(null);
        var record=type.GetMethod("RecordRawActivity",fields)!;
        // Payloads are synthetic RAWINPUT-layout fixtures, never captured input.
        byte[] Keyboard(ushort flags,ushort key,int prefix=24){var data=new byte[prefix+16];BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(prefix+2),flags);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(prefix+6),key);return data;}
        byte[] Mouse(ushort flags,ushort buttons,int x,int y,int prefix=24){var data=new byte[prefix+24];BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(prefix),flags);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(prefix+4),buttons);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(prefix+12),x);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(prefix+16),y);return data;}
        void Seed(bool lastPointer){pointer.SetValue(null,(long?)(lastPointer?200:100));keyboard.SetValue(null,lastPointer?100L:200L);}
        bool PointerWins()=>(long?)pointer.GetValue(null)>=(long)keyboard.GetValue(null)!;
        void Observe(uint device,byte[] packet,bool moved=false,int prefix=24)=>record.Invoke(null,[device,packet,prefix,packet.Length-prefix,moved,(nint)0,300L]);
        try
        {
            Seed(true);Observe(1,Keyboard(1,0x41));check("ordinary key release after pointer interaction keeps pointer origin",PointerWins(),null);
            Seed(true);Observe(1,Keyboard(0,0x41));check("new key press after pointer interaction selects foreground origin",!PointerWins(),null);
            foreach(var key in new ushort[]{0x10,0x11,0x12,0x5b,0x5c,0xa0,0xa1,0xa2,0xa3,0xa4,0xa5})
            {Seed(true);Observe(1,Keyboard(1,key));check($"modifier release 0x{key:X2} retains Mac flagsChanged semantics",!PointerWins(),null);}
            foreach(var up in new ushort[]{2,8,0x20,0x80,0x200})
            {Seed(false);Observe(0,Mouse(0,up,0,0));check($"mouse release 0x{up:X} after typing keeps foreground origin",!PointerWins(),null);}
            foreach(var down in new ushort[]{1,4,0x10,0x40,0x100,0x400,0x800})
            {Seed(false);Observe(0,Mouse(0,down,0,0));check($"mouse down or scroll 0x{down:X} selects pointer origin",PointerWins(),null);}
            Seed(false);Observe(0,Mouse(0,0,1,0));check("relative motion selects pointer origin",PointerWins(),null);
            Seed(false);Observe(0,Mouse(1,2,10000,10000),false);check("stationary absolute mouse release does not steal keyboard origin",!PointerWins(),null);
            Seed(false);Observe(0,Mouse(1,0,10000,10000),true);check("changed absolute pointer location selects pointer origin",PointerWins(),null);
            Seed(false);Observe(0,Mouse(0,0,0,0));check("empty mouse packet does not reset input origin",!PointerWins(),null);
            Seed(true);Observe(1,new byte[26]);check("truncated keyboard metadata cannot change origin",PointerWins(),null);
            Seed(true);Observe(1,Keyboard(0,255));check("unmapped keyboard packet cannot change origin",PointerWins(),null);
            Seed(true);Observe(1,Keyboard(1,0x41,16),prefix:16);check("32-bit header offset follows the same release policy",PointerWins(),null);
            Seed(true);Observe(1,Keyboard(0,0));check("empty virtual-key metadata cannot select keyboard origin",PointerWins(),null);
            Seed(true);Observe(1,Keyboard(8,0x41));check("unknown keyboard flags cannot select keyboard origin",PointerWins(),null);
            Seed(false);Observe(2,Mouse(0,1,0,0));check("non mouse or keyboard device cannot change origin",!PointerWins(),null);
            Seed(false);Observe(0,new byte[47]);check("truncated mouse metadata cannot change origin",!PointerWins(),null);
            Seed(false);Observe(0,Mouse(4,0,0,0));check("mouse attribute notification cannot change origin",!PointerWins(),null);
        }
        finally{pointer.SetValue(null,savedPointer);keyboard.SetValue(null,savedKeyboard);target.SetValue(null,savedTarget);}
    }
}
